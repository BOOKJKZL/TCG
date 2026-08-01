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
  publishAndroidRelease,
  readLatestAndroidRelease,
  serveAndroidApk,
  serveLatestAndroidRelease,
} from "../lib/releases/android-release-store.ts";

type Stored = {
  bytes: Uint8Array;
  customMetadata?: Record<string, string>;
};

class FakeBucket implements ContentBucket {
  readonly objects = new Map<string, Stored>();
  readonly operations: string[] = [];
  failPutKey: string | null = null;
  failDeleteKey: string | null = null;

  async head(key: string): Promise<StoredObjectHead | null> {
    const object = this.objects.get(key);
    return object ? { size: object.bytes.byteLength, customMetadata: object.customMetadata } : null;
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
      customMetadata: object.customMetadata,
      body: new Response(bytes).body as ReadableStream<Uint8Array>,
      async arrayBuffer() {
        return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
      },
    };
  }

  async put(
    key: string,
    value: ArrayBuffer | Uint8Array | string,
    options?: {
      httpMetadata?: { contentType?: string };
      customMetadata?: Record<string, string>;
    },
  ): Promise<void> {
    this.operations.push(`put:${key}`);
    if (this.failPutKey === key) throw new Error("injected put failure");
    const bytes = typeof value === "string"
      ? new TextEncoder().encode(value)
      : value instanceof Uint8Array
        ? value.slice()
        : new Uint8Array(value.slice(0));
    this.objects.set(key, { bytes, customMetadata: options?.customMetadata });
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
    }),
    ApiError,
  );
  await assert.rejects(
    publishAndroidRelease(bucket, {
      versionName: "0.1.0",
      versionCode: 1,
      declaredSha256: await sha256Hex(new TextEncoder().encode("not-apk")),
      bytes: arrayBuffer(new TextEncoder().encode("not-apk")),
    }),
    ApiError,
  );
  assert.equal(bucket.objects.size, 0);
});

test("publishes APK first, switches latest last, and keeps only the latest release", async () => {
  const bucket = new FakeBucket();
  const first = apkFixture("first");
  const firstSha = await sha256Hex(new Uint8Array(first.bytes));
  const firstResult = await publishAndroidRelease(bucket, {
    versionName: "0.1.0",
    versionCode: 1,
    declaredSha256: firstSha,
    bytes: first.bytes,
  }, new Date("2026-08-01T00:00:00.000Z"));
  assert.equal(firstResult.reused, false);
  assert.deepEqual(bucket.operations, [
    `put:${androidApkObjectKey(firstSha)}`,
    `put:${latestAndroidReleaseObjectKey}`,
  ]);

  bucket.operations.length = 0;
  const idempotent = await publishAndroidRelease(bucket, {
    versionName: "0.1.0",
    versionCode: 1,
    declaredSha256: firstSha,
    bytes: first.bytes,
  });
  assert.equal(idempotent.reused, true);
  assert.deepEqual(bucket.operations, []);

  const second = apkFixture("second");
  const secondSha = await sha256Hex(new Uint8Array(second.bytes));
  const secondResult = await publishAndroidRelease(bucket, {
    versionName: "0.2.0",
    versionCode: 2,
    declaredSha256: secondSha,
    bytes: second.bytes,
  }, new Date("2026-08-02T00:00:00.000Z"));
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
  await publishAndroidRelease(bucket, {
    versionName: "1.0.0",
    versionCode: 10,
    declaredSha256: firstSha,
    bytes: first.bytes,
  });

  const second = apkFixture("candidate");
  const secondSha = await sha256Hex(new Uint8Array(second.bytes));
  bucket.failPutKey = latestAndroidReleaseObjectKey;
  await assert.rejects(publishAndroidRelease(bucket, {
    versionName: "1.1.0",
    versionCode: 11,
    declaredSha256: secondSha,
    bytes: second.bytes,
  }));
  bucket.failPutKey = null;

  assert.equal((await readLatestAndroidRelease(bucket))?.sha256, firstSha);
  assert.equal(bucket.objects.has(androidApkObjectKey(firstSha)), true);
  assert.equal(bucket.objects.has(androidApkObjectKey(secondSha)), true);
});

test("serves latest metadata and immutable APK downloads with strict range semantics", async () => {
  const bucket = new FakeBucket();
  const apk = apkFixture("downloadable-release");
  const sha256 = await sha256Hex(new Uint8Array(apk.bytes));
  const result = await publishAndroidRelease(bucket, {
    versionName: "2.3.4",
    versionCode: 23,
    declaredSha256: sha256,
    bytes: apk.bytes,
  }, new Date("2026-08-03T12:34:56.000Z"));

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
  await publishAndroidRelease(bucket, {
    versionName: "3.0.0",
    versionCode: 30,
    declaredSha256: firstSha,
    bytes: first.bytes,
  });

  const next = apkFixture("new");
  const nextSha = await sha256Hex(new Uint8Array(next.bytes));
  bucket.failDeleteKey = androidApkObjectKey(firstSha);
  const result = await publishAndroidRelease(bucket, {
    versionName: "3.1.0",
    versionCode: 31,
    declaredSha256: nextSha,
    bytes: next.bytes,
  });
  assert.equal(result.cleanupPending, true);
  assert.equal(result.previousReleaseDeleted, false);
  assert.equal((await readLatestAndroidRelease(bucket))?.sha256, nextSha);
});

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
