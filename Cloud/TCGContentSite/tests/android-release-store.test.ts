import assert from "node:assert/strict";
import test from "node:test";
import { ApiError } from "../lib/content/api-error.ts";
import type {
  ContentBucket,
  StoredObjectBody,
  StoredObjectHead,
} from "../lib/content/content-store.ts";
import {
  androidApkObjectKey,
  latestAndroidReleaseObjectKey,
  parseAndroidRelease,
  publishAndroidRelease,
  readLatestAndroidRelease,
  serveAndroidApk,
  serveLatestAndroidRelease,
} from "../lib/releases/android-release-store.ts";

const RELEASE_CERTIFICATE_SHA256 = "a".repeat(64);

test("continues to parse the legacy schema 1 manifest during stable migration", () => {
  const sha256 = "1".repeat(64);
  const release = parseAndroidRelease({
    schemaVersion: 1,
    productId: "universal-gacha-simulator",
    versionName: "0.1.0",
    versionCode: 1,
    fileName: `universal-gacha-simulator-0.1.0+1-${sha256.slice(0, 8)}.apk`,
    sha256,
    downloadBytes: 100,
    publishedAt: "2026-08-01T00:00:00.000Z",
    downloadUrl: `/api/releases/android/${sha256}.apk`,
  });
  assert.equal(release.schemaVersion, 1);
});

test("migrates a stored schema 1 development package to schema 2 stable", async () => {
  const bucket = new FakeBucket();
  const legacyApk = apkFixture("legacy-development");
  const legacySha = await sha256Hex(new Uint8Array(legacyApk.bytes));
  const legacyFileName = `universal-gacha-simulator-0.1.0+1-${legacySha.slice(0, 8)}.apk`;
  await bucket.put(androidApkObjectKey(legacySha), legacyApk.bytes, {
    customMetadata: {
      kind: "android-apk",
      sha256: legacySha,
      versionName: "0.1.0",
      versionCode: "1",
      downloadBytes: String(legacyApk.bytes.byteLength),
      fileName: legacyFileName,
    },
  });
  await bucket.put(latestAndroidReleaseObjectKey, JSON.stringify({
    schemaVersion: 1,
    productId: "universal-gacha-simulator",
    versionName: "0.1.0",
    versionCode: 1,
    fileName: legacyFileName,
    sha256: legacySha,
    downloadBytes: legacyApk.bytes.byteLength,
    publishedAt: "2026-08-01T00:00:00.000Z",
    downloadUrl: `/api/releases/android/${legacySha}.apk`,
  }));
  await assert.rejects(
    serveAndroidApk(bucket, { sha256: legacySha, method: "GET", rangeHeader: null }),
    /不是当前公开正式版/,
  );

  const stableApk = apkFixture("first-stable");
  const stableSha = await sha256Hex(new Uint8Array(stableApk.bytes));
  const result = await publishAndroidRelease(
    bucket,
    releaseInput(stableApk, stableSha, "0.2.0", 2, 1),
  );
  assert.equal(result.release.schemaVersion, 2);
  assert.equal(result.release.releaseChannel, "stable");
  assert.equal(bucket.objects.has(androidApkObjectKey(legacySha)), false);
});

type Stored = {
  bytes: Uint8Array;
  etag: string;
  customMetadata?: Record<string, string>;
};

class FakeBucket implements ContentBucket {
  readonly objects = new Map<string, Stored>();
  readonly operations: string[] = [];
  failPutKey: string | null = null;
  failDeleteKey: string | null = null;
  failConditionalPutKey: string | null = null;
  materializeConditionalConflictKey: string | null = null;
  private etagSequence = 0;

  async head(key: string): Promise<StoredObjectHead | null> {
    const object = this.objects.get(key);
    return object ? { size: object.bytes.byteLength, etag: object.etag, customMetadata: object.customMetadata } : null;
  }

  async get(
    key: string,
    options?: { range?: { offset: number; length: number } },
  ): Promise<StoredObjectBody | null> {
    const object = this.objects.get(key);
    if (!object) return null;
    const bytes = options?.range
      ? object.bytes.slice(options.range.offset, options.range.offset + options.range.length)
      : object.bytes.slice();
    return {
      size: object.bytes.byteLength,
      etag: object.etag,
      customMetadata: object.customMetadata,
      body: new Response(bytes).body as ReadableStream<Uint8Array>,
      async arrayBuffer() {
        return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
      },
    };
  }

  async put(
    key: string,
    value: ArrayBuffer | Uint8Array | string | ReadableStream<Uint8Array>,
    options?: {
      httpMetadata?: { contentType?: string };
      customMetadata?: Record<string, string>;
      onlyIf?: { etagMatches?: string; etagDoesNotMatch?: string };
      sha256?: string;
    },
  ): Promise<unknown> {
    this.operations.push(`put:${key}`);
    if (this.failPutKey === key) throw new Error("injected put failure");
    if (this.failConditionalPutKey === key && options?.onlyIf) return null;
    const materializeConflict = this.materializeConditionalConflictKey === key && Boolean(options?.onlyIf);
    const current = this.objects.get(key);
    if (options?.onlyIf?.etagMatches && current?.etag !== options.onlyIf.etagMatches) return null;
    if (options?.onlyIf?.etagDoesNotMatch === "*" && current) return null;
    if (value instanceof ReadableStream) throw new Error("stream fixtures are not supported");
    const bytes = typeof value === "string"
      ? new TextEncoder().encode(value)
      : value instanceof Uint8Array
        ? value.slice()
        : new Uint8Array(value.slice(0));
    const etag = `etag-${++this.etagSequence}`;
    this.objects.set(key, { bytes, etag, customMetadata: options?.customMetadata });
    if (materializeConflict) return null;
    return { etag };
  }

  async delete(key: string): Promise<void> {
    this.operations.push(`delete:${key}`);
    if (this.failDeleteKey === key) throw new Error("injected delete failure");
    this.objects.delete(key);
  }
}

test("rejects non-APK bytes and mismatched declared hashes", async () => {
  const bucket = new FakeBucket();
  const apk = apkFixture("first");
  await assert.rejects(
    publishAndroidRelease(bucket, {
      versionName: "0.1.0",
      versionCode: 1,
      declaredSha256: "0".repeat(64),
      bytes: apk.bytes,
      audit: {},
      releaseNotes: "first release",
      expectedCertificateSha256: RELEASE_CERTIFICATE_SHA256,
      verifyPublicReadback: async () => {},
    }),
    ApiError,
  );
  await assert.rejects(
    publishAndroidRelease(bucket, {
      versionName: "0.1.0",
      versionCode: 1,
      declaredSha256: await sha256Hex(new TextEncoder().encode("not-apk")),
      bytes: arrayBuffer(new TextEncoder().encode("not-apk")),
      audit: {},
      releaseNotes: "invalid release",
      expectedCertificateSha256: RELEASE_CERTIFICATE_SHA256,
      verifyPublicReadback: async () => {},
    }),
    ApiError,
  );
  assert.equal(bucket.objects.size, 0);
});

test("publishes APK first, switches latest last, and keeps only the latest release", async () => {
  const bucket = new FakeBucket();
  const first = apkFixture("first");
  const firstSha = await sha256Hex(new Uint8Array(first.bytes));
  const firstResult = await publishAndroidRelease(
    bucket,
    releaseInput(first, firstSha, "0.1.0", 1, 0, "2026-08-01T00:00:00.000Z"),
    new Date("2026-08-01T00:00:00.000Z"),
  );
  assert.equal(firstResult.reused, false);
  assert.deepEqual(bucket.operations, [
    `put:${androidApkObjectKey(firstSha)}`,
    `put:${latestAndroidReleaseObjectKey}`,
  ]);

  bucket.operations.length = 0;
  const idempotent = await publishAndroidRelease(
    bucket,
    releaseInput(first, firstSha, "0.1.0", 1, 0),
  );
  assert.equal(idempotent.reused, true);
  assert.deepEqual(bucket.operations, []);

  const second = apkFixture("second");
  const secondSha = await sha256Hex(new Uint8Array(second.bytes));
  const secondResult = await publishAndroidRelease(
    bucket,
    releaseInput(second, secondSha, "0.2.0", 2, 1, "2026-08-02T00:00:00.000Z"),
    new Date("2026-08-02T00:00:00.000Z"),
  );
  assert.equal(secondResult.previousReleaseDeleted, true);
  assert.deepEqual(bucket.operations, [
    `put:${androidApkObjectKey(secondSha)}`,
    `put:${latestAndroidReleaseObjectKey}`,
    `delete:${androidApkObjectKey(firstSha)}`,
  ]);
  assert.equal(bucket.objects.has(androidApkObjectKey(firstSha)), false);
  assert.equal((await readLatestAndroidRelease(bucket))?.sha256, secondSha);
});

test("does not switch the public release when the latest manifest write fails", async () => {
  const bucket = new FakeBucket();
  const first = apkFixture("stable");
  const firstSha = await sha256Hex(new Uint8Array(first.bytes));
  await publishAndroidRelease(bucket, releaseInput(first, firstSha, "1.0.0", 10, 0));

  const second = apkFixture("candidate");
  const secondSha = await sha256Hex(new Uint8Array(second.bytes));
  bucket.failPutKey = latestAndroidReleaseObjectKey;
  await assert.rejects(publishAndroidRelease(
    bucket,
    releaseInput(second, secondSha, "1.1.0", 11, 10),
  ));
  bucket.failPutKey = null;

  assert.equal((await readLatestAndroidRelease(bucket))?.sha256, firstSha);
  assert.equal(bucket.objects.has(androidApkObjectKey(firstSha)), true);
  assert.equal(bucket.objects.has(androidApkObjectKey(secondSha)), true);
});

test("does not switch latest when full public candidate readback fails", async () => {
  const bucket = new FakeBucket();
  const apk = apkFixture("readback-failure");
  const sha = await sha256Hex(new Uint8Array(apk.bytes));
  const input = releaseInput(apk, sha, "1.0.0", 10, 0);
  input.verifyPublicReadback = async () => {
    assert.equal(bucket.objects.has(androidApkObjectKey(sha)), true);
    assert.equal(bucket.objects.has(latestAndroidReleaseObjectKey), false);
    throw new Error("injected public readback failure");
  };
  await assert.rejects(publishAndroidRelease(bucket, input), /readback failure/);
  assert.equal(bucket.objects.has(latestAndroidReleaseObjectKey), false);
  assert.equal(bucket.objects.has(androidApkObjectKey(sha)), true);
});

test("reuses an atomically-created same-SHA candidate without deleting it", async () => {
  const bucket = new FakeBucket();
  const apk = apkFixture("same-sha-race");
  const sha = await sha256Hex(new Uint8Array(apk.bytes));
  const key = androidApkObjectKey(sha);
  bucket.materializeConditionalConflictKey = key;
  const result = await publishAndroidRelease(bucket, releaseInput(apk, sha, "1.0.0", 10, 0));
  assert.equal(result.reused, true);
  assert.equal(result.release.sha256, sha);
  assert.equal(bucket.objects.has(key), true);
});

test("rejects a stale concurrent latest write instead of allowing version rollback", async () => {
  const bucket = new FakeBucket();
  const first = apkFixture("concurrent-stable");
  const firstSha = await sha256Hex(new Uint8Array(first.bytes));
  await publishAndroidRelease(bucket, releaseInput(first, firstSha, "1.0.0", 10, 0));

  const candidate = apkFixture("concurrent-candidate");
  const candidateSha = await sha256Hex(new Uint8Array(candidate.bytes));
  bucket.failConditionalPutKey = latestAndroidReleaseObjectKey;
  await assert.rejects(
    publishAndroidRelease(bucket, releaseInput(candidate, candidateSha, "1.1.0", 11, 10)),
    /已抢先更新最新版/,
  );
  bucket.failConditionalPutKey = null;
  assert.equal((await readLatestAndroidRelease(bucket))?.sha256, firstSha);
  assert.equal(bucket.objects.has(androidApkObjectKey(candidateSha)), true);
});

test("serves latest metadata and stable-only APK downloads with strict range semantics", async () => {
  const bucket = new FakeBucket();
  const apk = apkFixture("downloadable-release");
  const sha256 = await sha256Hex(new Uint8Array(apk.bytes));
  const result = await publishAndroidRelease(
    bucket,
    releaseInput(apk, sha256, "2.3.4", 23, 0, "2026-08-03T12:34:56.000Z"),
    new Date("2026-08-03T12:34:56.000Z"),
  );

  const manifest = await serveLatestAndroidRelease(bucket, "GET");
  assert.equal(manifest.status, 200);
  assert.equal(((await manifest.json()) as { sha256: string }).sha256, sha256);

  const full = await serveAndroidApk(bucket, { sha256, method: "GET", rangeHeader: null });
  assert.equal(full.status, 200);
  assert.equal(full.headers.get("content-type"), "application/vnd.android.package-archive");
  assert.ok((full.headers.get("content-disposition") ?? "").includes(result.release.fileName));
  assert.deepEqual(new Uint8Array(await full.arrayBuffer()), new Uint8Array(apk.bytes));

  const offset = 6;
  const partial = await serveAndroidApk(bucket, {
    sha256,
    method: "GET",
    rangeHeader: `bytes=${offset}-`,
  });
  assert.equal(partial.status, 206);
  assert.equal(partial.headers.get("content-range"), `bytes ${offset}-${apk.bytes.byteLength - 1}/${apk.bytes.byteLength}`);
  assert.deepEqual(
    new Uint8Array(await partial.arrayBuffer()),
    new Uint8Array(apk.bytes).slice(offset),
  );

  const head = await serveAndroidApk(bucket, { sha256, method: "HEAD", rangeHeader: null });
  assert.equal(head.status, 200);
  assert.equal(await head.text(), "");

  const bounded = await serveAndroidApk(bucket, {
    sha256,
    method: "GET",
    rangeHeader: "bytes=1-4",
  });
  assert.equal(bounded.status, 416);
  assert.equal(bounded.headers.get("content-range"), `bytes */${apk.bytes.byteLength}`);
});

test("reports cleanup as pending without rolling back an already-published release", async () => {
  const bucket = new FakeBucket();
  const first = apkFixture("old");
  const firstSha = await sha256Hex(new Uint8Array(first.bytes));
  await publishAndroidRelease(bucket, releaseInput(first, firstSha, "3.0.0", 30, 0));

  const next = apkFixture("new");
  const nextSha = await sha256Hex(new Uint8Array(next.bytes));
  bucket.failDeleteKey = androidApkObjectKey(firstSha);
  const result = await publishAndroidRelease(
    bucket,
    releaseInput(next, nextSha, "3.1.0", 31, 30),
  );
  assert.equal(result.cleanupPending, true);
  assert.equal(result.previousReleaseDeleted, false);
  assert.equal((await readLatestAndroidRelease(bucket))?.sha256, nextSha);
});

test("rejects non-increasing version codes and stale audit baselines", async () => {
  const bucket = new FakeBucket();
  const first = apkFixture("stable-version");
  const firstSha = await sha256Hex(new Uint8Array(first.bytes));
  await publishAndroidRelease(bucket, releaseInput(first, firstSha, "4.0.0", 40, 0));

  const downgrade = apkFixture("downgrade");
  const downgradeSha = await sha256Hex(new Uint8Array(downgrade.bytes));
  await assert.rejects(
    publishAndroidRelease(bucket, releaseInput(downgrade, downgradeSha, "3.9.0", 39, 40)),
    /versionCode 必须高于当前公开版本/,
  );

  const stale = apkFixture("stale-baseline");
  const staleSha = await sha256Hex(new Uint8Array(stale.bytes));
  await assert.rejects(
    publishAndroidRelease(bucket, releaseInput(stale, staleSha, "4.1.0", 41, 0)),
    /基线已过期/,
  );
});

test("rejects audit reports for another APK, signer, or incomplete checks", async () => {
  const bucket = new FakeBucket();
  const apk = apkFixture("audited");
  const sha = await sha256Hex(new Uint8Array(apk.bytes));

  const wrongHash = releaseInput(apk, sha, "5.0.0", 50, 0);
  (wrongHash.audit as { artifact: { sha256: string } }).artifact.sha256 = "b".repeat(64);
  await assert.rejects(publishAndroidRelease(bucket, wrongHash), /审计报告与上传 APK/);

  const wrongSigner = releaseInput(apk, sha, "5.0.0", 50, 0);
  (wrongSigner.audit as { artifact: { certificateSha256: string } }).artifact.certificateSha256 = "b".repeat(64);
  await assert.rejects(publishAndroidRelease(bucket, wrongSigner), /审计报告与上传 APK/);

  const incomplete = releaseInput(apk, sha, "5.0.0", 50, 0);
  (incomplete.audit as { checks: unknown[] }).checks.pop();
  await assert.rejects(publishAndroidRelease(bucket, incomplete), /缺少检查结果/);

  const duplicate = releaseInput(apk, sha, "5.0.0", 50, 0);
  const duplicateChecks = (duplicate.audit as { checks: Array<{ name: string; passed: boolean; detail: string }> }).checks;
  duplicateChecks[7] = { ...duplicateChecks[0] };
  await assert.rejects(publishAndroidRelease(bucket, duplicate), /重复、未知或失败/);

  const forbiddenPermission = releaseInput(apk, sha, "5.0.0", 50, 0);
  (forbiddenPermission.audit as { artifact: { permissions: string[] } }).artifact.permissions.push("android.permission.CAMERA");
  await assert.rejects(publishAndroidRelease(bucket, forbiddenPermission), /未允许的权限/);
  assert.equal(bucket.objects.size, 0);
});

test("rejects expired audits and non-release artifact names", async () => {
  const bucket = new FakeBucket();
  const apk = apkFixture("expired");
  const sha = await sha256Hex(new Uint8Array(apk.bytes));
  const now = new Date("2026-08-08T12:00:00.000Z");
  await assert.rejects(
    publishAndroidRelease(
      bucket,
      releaseInput(apk, sha, "6.0.0", 60, 0, "2026-08-08T11:30:00.000Z"),
      now,
    ),
    /审计报告已过期/,
  );

  const smoke = releaseInput(apk, sha, "6.0.0", 60, 0, now.toISOString());
  (smoke.audit as { artifact: { fileName: string } }).artifact.fileName = "game-smoke.apk";
  await assert.rejects(publishAndroidRelease(bucket, smoke, now), /审计报告与上传 APK/);
});

function releaseInput(
  apk: { bytes: ArrayBuffer },
  sha256: string,
  versionName: string,
  versionCode: number,
  publishedVersionCode: number,
  auditedAtUtc = new Date().toISOString(),
) {
  const requiredChecks = [
    "package identity",
    "release version",
    "ARM64 ABI",
    "SDK and permissions",
    "non-debuggable manifest",
    "release signature",
    "zipalign",
    "release payload boundary",
  ];
  return {
    versionName,
    versionCode,
    declaredSha256: sha256,
    bytes: apk.bytes,
    releaseNotes: `Release ${versionName}`,
    expectedCertificateSha256: RELEASE_CERTIFICATE_SHA256,
    verifyPublicReadback: async () => {},
    audit: {
      schemaVersion: 1,
      channel: "stable-candidate",
      valid: true,
      auditedAtUtc,
      artifact: {
        fileName: `UniversalGachaSimulator-release-${versionName}+${versionCode}.apk`,
        downloadBytes: apk.bytes.byteLength,
        sha256,
        packageId: "com.personal.universalgacha",
        versionName,
        versionCode,
        publishedVersionCode,
        minSdk: 23,
        targetSdk: 35,
        abis: ["arm64-v8a"],
        permissions: ["android.permission.INTERNET"],
        debuggable: false,
        certificateSha256: RELEASE_CERTIFICATE_SHA256,
        signerCount: 1,
      },
      checks: requiredChecks.map((name) => ({ name, passed: true, detail: "fixture" })),
    },
  };
}

function apkFixture(label: string): { bytes: ArrayBuffer } {
  const suffix = new TextEncoder().encode(label);
  const bytes = new Uint8Array(4 + suffix.byteLength);
  bytes.set([0x50, 0x4b, 0x03, 0x04]);
  bytes.set(suffix, 4);
  return { bytes: arrayBuffer(bytes) };
}

function arrayBuffer(bytes: Uint8Array): ArrayBuffer {
  return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
}

async function sha256Hex(bytes: Uint8Array): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", arrayBuffer(bytes));
  return Array.from(new Uint8Array(digest), (value) => value.toString(16).padStart(2, "0")).join("");
}
