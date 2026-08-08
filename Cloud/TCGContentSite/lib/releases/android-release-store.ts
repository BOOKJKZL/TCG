import { ApiError } from "../content/api-error.ts";
import type {
  ContentBucket,
  StoredObjectHead,
} from "../content/content-store.ts";
import { parseOpenEndedRange } from "../content/range.ts";

export const MAX_APK_BYTES = 60 * 1024 * 1024;
export const latestAndroidReleaseObjectKey = "releases/android/latest.json";

const SHA256_PATTERN = /^[a-f0-9]{64}$/;
const VERSION_NAME_PATTERN = /^[0-9A-Za-z][0-9A-Za-z._+-]{0,39}$/;
const MAX_MANIFEST_BYTES = 16 * 1024;
const MAX_RELEASE_NOTES_CHARS = 2_000;
const MAX_AUDIT_AGE_MS = 15 * 60 * 1000;
const MAX_AUDIT_FUTURE_SKEW_MS = 5 * 60 * 1000;
const PRODUCT_ID = "universal-gacha-simulator";
const PACKAGE_ID = "com.personal.universalgacha";
const ALLOWED_PERMISSIONS = new Set([
  "android.permission.ACCESS_NETWORK_STATE",
  "android.permission.INTERNET",
  "android.permission.VIBRATE",
  `${PACKAGE_ID}.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION`,
]);
const REQUIRED_AUDIT_CHECKS = [
  "package identity",
  "release version",
  "ARM64 ABI",
  "SDK and permissions",
  "non-debuggable manifest",
  "release signature",
  "zipalign",
  "release payload boundary",
] as const;

type AndroidReleaseCommon = {
  productId: typeof PRODUCT_ID;
  versionName: string;
  versionCode: number;
  fileName: string;
  sha256: string;
  downloadBytes: number;
  publishedAt: string;
  downloadUrl: string;
};

export type LegacyAndroidRelease = AndroidReleaseCommon & {
  schemaVersion: 1;
};

export type StableAndroidRelease = AndroidReleaseCommon & {
  schemaVersion: 2;
  releaseChannel: "stable";
  releaseNotes: string;
  certificateSha256: string;
  targetSdk: number;
  abis: ["arm64-v8a"];
  auditedAt: string;
};

export type AndroidRelease = LegacyAndroidRelease | StableAndroidRelease;

export type PublishAndroidReleaseInput = {
  versionName: string;
  versionCode: number;
  declaredSha256: string;
  bytes: ArrayBuffer;
  audit: unknown;
  releaseNotes: string;
  expectedCertificateSha256: string;
  verifyPublicReadback: (artifact: { sha256: string; downloadBytes: number }) => Promise<void>;
};

export type PublishAndroidReleaseResult = {
  release: StableAndroidRelease;
  reused: boolean;
  previousReleaseDeleted: boolean;
  cleanupPending: boolean;
};

export function androidApkObjectKey(sha256: string): string {
  assertSha256(sha256);
  return `releases/android/apks/${sha256}.apk`;
}

export async function publishAndroidRelease(
  bucket: ContentBucket,
  input: PublishAndroidReleaseInput,
  now = new Date(),
): Promise<PublishAndroidReleaseResult> {
  const versionName = parseVersionName(input.versionName);
  const versionCode = parseVersionCode(input.versionCode);
  assertSha256(input.declaredSha256);
  const expectedCertificateSha256 = normalizeCertificateSha256(input.expectedCertificateSha256);
  const releaseNotes = parseReleaseNotes(input.releaseNotes);
  assertApkBytes(input.bytes);

  const actualSha256 = await sha256Hex(input.bytes);
  if (actualSha256 !== input.declaredSha256) {
    throw new ApiError(400, `APK SHA-256 不一致；实际值为 ${actualSha256}。`);
  }

  const currentState = await readLatestAndroidReleaseState(bucket);
  const current = currentState?.release ?? null;
  const isStableRetry = current?.schemaVersion === 2 && current.sha256 === actualSha256;
  if (current && !isStableRetry && versionCode <= current.versionCode) {
    throw new ApiError(409, `正式版 versionCode 必须高于当前公开版本 ${current.versionCode}。`);
  }
  const audit = parseReleaseAudit(input.audit, {
    actualSha256,
    downloadBytes: input.bytes.byteLength,
    versionName,
    versionCode,
    expectedCertificateSha256,
    currentVersionCode: current?.versionCode ?? 0,
    now,
    allowEarlierBaseline: isStableRetry,
  });
  if (isStableRetry && current.schemaVersion === 2) {
    if (
      current.versionName !== versionName ||
      current.versionCode !== versionCode ||
      current.releaseNotes !== releaseNotes ||
      current.certificateSha256 !== expectedCertificateSha256
    ) {
      throw new ApiError(409, "同一 APK 已使用不同正式版资料发布，拒绝改写标签。");
    }
    return {
      release: current,
      reused: true,
      previousReleaseDeleted: false,
      cleanupPending: false,
    };
  }

  const key = androidApkObjectKey(actualSha256);
  const fileName = releaseFileName(versionName, versionCode, actualSha256);
  const existing = await bucket.head(key);
  let reused = false;
  if (existing) {
    assertStoredApk(existing, {
      sha256: actualSha256,
      versionName,
      versionCode,
      downloadBytes: input.bytes.byteLength,
      fileName,
      releaseChannel: "stable",
      certificateSha256: expectedCertificateSha256,
      targetSdk: audit.targetSdk,
    });
    reused = true;
  } else {
    const candidatePutResult = await bucket.put(key, input.bytes, {
      onlyIf: { etagDoesNotMatch: "*" },
      httpMetadata: { contentType: "application/vnd.android.package-archive" },
      customMetadata: {
        kind: "android-apk",
        sha256: actualSha256,
        versionName,
        versionCode: String(versionCode),
        downloadBytes: String(input.bytes.byteLength),
        fileName,
        releaseChannel: "audited-stable",
        certificateSha256: expectedCertificateSha256,
        targetSdk: String(audit.targetSdk),
        abis: "arm64-v8a",
        auditExpiresAt: new Date(now.getTime() + MAX_AUDIT_AGE_MS).toISOString(),
      },
      sha256: actualSha256,
    });
    if (candidatePutResult === null) {
      const racedCandidate = await bucket.head(key);
      if (!racedCandidate) {
        throw new ApiError(409, "正式版候选对象并发创建失败，请重新审计后重试。");
      }
      assertStoredApk(racedCandidate, {
        sha256: actualSha256,
        versionName,
        versionCode,
        downloadBytes: input.bytes.byteLength,
        fileName,
        releaseChannel: "stable",
        certificateSha256: expectedCertificateSha256,
        targetSdk: audit.targetSdk,
      });
      reused = true;
    }
  }

  await input.verifyPublicReadback({
    sha256: actualSha256,
    downloadBytes: input.bytes.byteLength,
  });

  const release: StableAndroidRelease = {
    schemaVersion: 2,
    productId: PRODUCT_ID,
    releaseChannel: "stable",
    versionName,
    versionCode,
    fileName,
    sha256: actualSha256,
    downloadBytes: input.bytes.byteLength,
    publishedAt: validPublishedAt(now),
    downloadUrl: `/api/releases/android/${actualSha256}.apk`,
    releaseNotes,
    certificateSha256: expectedCertificateSha256,
    targetSdk: audit.targetSdk,
    abis: ["arm64-v8a"],
    auditedAt: audit.auditedAt,
  };
  const manifest = encodeManifest(release);
  const latestPutResult = await bucket.put(latestAndroidReleaseObjectKey, manifest, {
    onlyIf: currentState
      ? { etagMatches: currentState.etag }
      : { etagDoesNotMatch: "*" },
    httpMetadata: { contentType: "application/json; charset=utf-8" },
    customMetadata: {
      schemaVersion: "2",
      releaseChannel: "stable",
      sha256: actualSha256,
      versionName,
      versionCode: String(versionCode),
      downloadBytes: String(input.bytes.byteLength),
      certificateSha256: expectedCertificateSha256,
      targetSdk: String(audit.targetSdk),
      abis: "arm64-v8a",
    },
  });
  if (latestPutResult === null) {
    throw new ApiError(409, "另一项 Android 发布已抢先更新最新版，请重新审计后重试。");
  }

  let previousReleaseDeleted = false;
  let cleanupPending = false;
  if (current && current.sha256 !== actualSha256) {
    try {
      await bucket.delete(androidApkObjectKey(current.sha256));
      previousReleaseDeleted = true;
    } catch {
      cleanupPending = true;
    }
  }

  return { release, reused, previousReleaseDeleted, cleanupPending };
}

export async function readLatestAndroidRelease(
  bucket: ContentBucket,
): Promise<AndroidRelease | null> {
  return (await readLatestAndroidReleaseState(bucket))?.release ?? null;
}

async function readLatestAndroidReleaseState(
  bucket: ContentBucket,
): Promise<{ release: AndroidRelease; etag: string } | null> {
  const object = await bucket.get(latestAndroidReleaseObjectKey);
  if (!object) return null;
  if (!object.etag) throw new ApiError(503, "最新版 APK 清单缺少并发控制标识。");
  if (object.size <= 0 || object.size > MAX_MANIFEST_BYTES) {
    throw new ApiError(503, "最新版 APK 清单损坏或超过大小上限。");
  }

  let release: AndroidRelease;
  try {
    const source = new TextDecoder().decode(await object.arrayBuffer());
    release = parseAndroidRelease(JSON.parse(source));
  } catch {
    throw new ApiError(503, "最新版 APK 清单不是有效且受支持的 JSON。");
  }

  const head = await bucket.head(androidApkObjectKey(release.sha256));
  if (!head) throw new ApiError(503, "最新版 APK 文件缺失，已停止公开下载。");
  try {
    assertStoredApk(head, release);
  } catch {
    throw new ApiError(503, "最新版 APK 文件验证元数据损坏，已停止公开下载。");
  }
  return { release, etag: object.etag };
}

export async function serveLatestAndroidRelease(
  bucket: ContentBucket,
  method: "GET" | "HEAD",
): Promise<Response> {
  const release = await readLatestAndroidRelease(bucket);
  if (!release) throw new ApiError(404, "尚未发布 Android 安装包。");
  const bytes = encodeManifest(release);
  return new Response(method === "HEAD" ? null : bytes.buffer as ArrayBuffer, {
    headers: {
      "Cache-Control": "public, max-age=60, must-revalidate",
      "Content-Length": String(bytes.byteLength),
      "Content-Type": "application/json; charset=utf-8",
      "X-Content-Type-Options": "nosniff",
    },
  });
}

export async function serveAndroidApk(
  bucket: ContentBucket,
  input: {
    sha256: string;
    method: "GET" | "HEAD";
    rangeHeader: string | null;
  },
): Promise<Response> {
  assertSha256(input.sha256);
  const key = androidApkObjectKey(input.sha256);
  const head = await bucket.head(key);
  if (!head) throw new ApiError(404, "找不到指定 Android 安装包。");
  const latest = await readLatestAndroidRelease(bucket);
  const candidateExpiresAt = head.customMetadata?.releaseChannel === "audited-stable"
    ? new Date(head.customMetadata.auditExpiresAt ?? "").getTime()
    : Number.NaN;
  const isCurrentStable = latest?.schemaVersion === 2 && latest.sha256 === input.sha256;
  const isFreshAuditedCandidate = Number.isFinite(candidateExpiresAt) && candidateExpiresAt > Date.now();
  if (!isCurrentStable && !isFreshAuditedCandidate) {
    throw new ApiError(404, "指定文件不是当前公开正式版 Android 安装包。");
  }
  const fileName = storedApkFileName(head, input.sha256);

  let range;
  try {
    range = parseOpenEndedRange(input.rangeHeader, head.size);
  } catch (error) {
    if (error instanceof ApiError && error.status === 416) {
      return Response.json(
        { error: error.message },
        {
          status: 416,
          headers: {
            "Accept-Ranges": "bytes",
            "Content-Range": `bytes */${head.size}`,
          },
        },
      );
    }
    throw error;
  }

  const headers: Record<string, string> = {
    "Accept-Ranges": "bytes",
    "Cache-Control": "private, no-store",
    "Content-Disposition": `attachment; filename="${fileName}"`,
    "Content-Length": String(range?.length ?? head.size),
    "Content-Type": "application/vnd.android.package-archive",
    "ETag": `"sha256-${input.sha256}"`,
    "X-Content-Sha256": input.sha256,
    "X-Content-Type-Options": "nosniff",
  };
  if (range) {
    headers["Content-Range"] = `bytes ${range.offset}-${range.end}/${head.size}`;
  }
  if (input.method === "HEAD") {
    return new Response(null, { status: range ? 206 : 200, headers });
  }

  const object = await bucket.get(
    key,
    range ? { range: { offset: range.offset, length: range.length } } : undefined,
  );
  if (!object) throw new ApiError(404, "Android 安装包在读取期间消失，请重新发布。");
  return new Response(object.body, { status: range ? 206 : 200, headers });
}

export function parseAndroidRelease(value: unknown): AndroidRelease {
  if (!isRecord(value)) throw new ApiError(503, "最新版 APK 清单结构无效。");
  const release = value as Partial<AndroidReleaseCommon> & Record<string, unknown>;
  if (
    (release.schemaVersion !== 1 && release.schemaVersion !== 2) ||
    release.productId !== PRODUCT_ID ||
    typeof release.versionName !== "string" ||
    typeof release.versionCode !== "number" ||
    typeof release.fileName !== "string" ||
    typeof release.sha256 !== "string" ||
    typeof release.downloadBytes !== "number" ||
    typeof release.publishedAt !== "string" ||
    typeof release.downloadUrl !== "string"
  ) {
    throw new ApiError(503, "最新版 APK 清单字段缺失或类型错误。");
  }

  const versionName = parseVersionName(release.versionName);
  const versionCode = parseVersionCode(release.versionCode);
  assertSha256(release.sha256);
  if (
    !Number.isSafeInteger(release.downloadBytes) ||
    release.downloadBytes <= 0 ||
    release.downloadBytes > MAX_APK_BYTES
  ) {
    throw new ApiError(503, "最新版 APK 清单的文件大小无效。");
  }
  if (release.publishedAt !== validPublishedAt(new Date(release.publishedAt))) {
    throw new ApiError(503, "最新版 APK 清单的发布时间无效。");
  }

  const expectedFileName = releaseFileName(versionName, versionCode, release.sha256);
  const expectedDownloadUrl = `/api/releases/android/${release.sha256}.apk`;
  if (release.fileName !== expectedFileName || release.downloadUrl !== expectedDownloadUrl) {
    throw new ApiError(503, "最新版 APK 清单的下载身份无效。");
  }
  if (release.schemaVersion === 1) return release as LegacyAndroidRelease;
  if (
    release.releaseChannel !== "stable" ||
    typeof release.releaseNotes !== "string" ||
    typeof release.certificateSha256 !== "string" ||
    typeof release.targetSdk !== "number" ||
    !Array.isArray(release.abis) ||
    typeof release.auditedAt !== "string"
  ) {
    throw new ApiError(503, "正式版 APK 清单缺少审计字段。");
  }
  parseReleaseNotes(release.releaseNotes);
  normalizeCertificateSha256(release.certificateSha256);
  if (!Number.isSafeInteger(release.targetSdk) || release.targetSdk < 34) {
    throw new ApiError(503, "正式版 APK 清单的 targetSdk 无效。");
  }
  if (release.abis.length !== 1 || release.abis[0] !== "arm64-v8a") {
    throw new ApiError(503, "正式版 APK 清单的 ABI 无效。");
  }
  validPublishedAt(new Date(release.auditedAt));
  return release as StableAndroidRelease;
}

function assertApkBytes(bytes: ArrayBuffer): void {
  if (bytes.byteLength <= 0) throw new ApiError(400, "Android 安装包不能为空。");
  if (bytes.byteLength > MAX_APK_BYTES) {
    throw new ApiError(413, "Android 安装包不能超过 60 MiB。");
  }
  const prefix = new Uint8Array(bytes, 0, Math.min(4, bytes.byteLength));
  if (
    prefix.length < 4 ||
    prefix[0] !== 0x50 ||
    prefix[1] !== 0x4b ||
    prefix[2] !== 0x03 ||
    prefix[3] !== 0x04
  ) {
    throw new ApiError(400, "上传内容不是有效的 APK/ZIP 文件。");
  }
}

function assertStoredApk(
  head: StoredObjectHead,
  expected: {
    sha256: string;
    versionName: string;
    versionCode: number;
    downloadBytes: number;
    fileName: string;
    releaseChannel?: "stable";
    certificateSha256?: string;
    targetSdk?: number;
  },
): void {
  const metadata = head.customMetadata;
  if (
    head.size !== expected.downloadBytes ||
    metadata?.kind !== "android-apk" ||
    metadata.sha256 !== expected.sha256 ||
    metadata.versionName !== expected.versionName ||
    metadata.versionCode !== String(expected.versionCode) ||
    metadata.downloadBytes !== String(expected.downloadBytes) ||
    metadata.fileName !== expected.fileName ||
    (expected.releaseChannel === "stable" && (
      metadata.releaseChannel !== "audited-stable" ||
      metadata.certificateSha256 !== expected.certificateSha256 ||
      metadata.targetSdk !== String(expected.targetSdk) ||
      metadata.abis !== "arm64-v8a" ||
      !Number.isFinite(new Date(metadata.auditExpiresAt ?? "").getTime())
    ))
  ) {
    throw new ApiError(409, "Android 安装包对象的大小或验证元数据不匹配。");
  }
}

function storedApkFileName(head: StoredObjectHead, sha256: string): string {
  const metadata = head.customMetadata;
  if (
    metadata?.kind !== "android-apk" ||
    metadata.sha256 !== sha256 ||
    metadata.downloadBytes !== String(head.size) ||
    !metadata.versionName ||
    !metadata.versionCode ||
    !metadata.fileName
  ) {
    throw new ApiError(503, "Android 安装包验证元数据损坏。");
  }
  const expected = releaseFileName(
    parseVersionName(metadata.versionName),
    parseVersionCode(Number(metadata.versionCode)),
    sha256,
  );
  if (metadata.fileName !== expected) {
    throw new ApiError(503, "Android 安装包文件名元数据损坏。");
  }
  return expected;
}

function encodeManifest(release: AndroidRelease): Uint8Array {
  return new TextEncoder().encode(`${JSON.stringify(release, null, 2)}\n`);
}

function parseVersionName(value: string): string {
  const normalized = value.trim();
  if (!VERSION_NAME_PATTERN.test(normalized)) {
    throw new ApiError(400, "APK 版本名称格式不正确。");
  }
  return normalized;
}

function parseVersionCode(value: number): number {
  if (!Number.isSafeInteger(value) || value <= 0) {
    throw new ApiError(400, "APK versionCode 必须是正整数。");
  }
  return value;
}

function parseReleaseNotes(value: string): string {
  const normalized = value.trim();
  if (!normalized || normalized.length > MAX_RELEASE_NOTES_CHARS || /[\u0000-\u0008\u000b\u000c\u000e-\u001f]/.test(normalized)) {
    throw new ApiError(400, `正式版更新说明必须为 1-${MAX_RELEASE_NOTES_CHARS} 个安全字符。`);
  }
  return normalized;
}

function normalizeCertificateSha256(value: string): string {
  const normalized = value.trim().replace(/:/g, "").toLowerCase();
  if (!SHA256_PATTERN.test(normalized)) {
    throw new ApiError(503, "正式版签名证书 SHA-256 绑定缺失或无效。");
  }
  return normalized;
}

function parseReleaseAudit(
  value: unknown,
  expected: {
    actualSha256: string;
    downloadBytes: number;
    versionName: string;
    versionCode: number;
    expectedCertificateSha256: string;
    currentVersionCode: number;
    now: Date;
    allowEarlierBaseline: boolean;
  },
): { targetSdk: number; auditedAt: string } {
  if (!isRecord(value) || value.schemaVersion !== 1 || value.channel !== "stable-candidate" || value.valid !== true) {
    throw new ApiError(400, "正式版审计报告无效或未通过。");
  }
  const artifact = value.artifact;
  if (!isRecord(artifact)) throw new ApiError(400, "正式版审计报告缺少 artifact。");
  const auditedAt = typeof value.auditedAtUtc === "string" ? value.auditedAtUtc : "";
  const auditedTime = new Date(auditedAt).getTime();
  const nowTime = expected.now.getTime();
  if (
    !Number.isFinite(auditedTime) ||
    auditedTime < nowTime - MAX_AUDIT_AGE_MS ||
    auditedTime > nowTime + MAX_AUDIT_FUTURE_SKEW_MS
  ) {
    throw new ApiError(400, "正式版审计报告已过期或时间无效，请重新审计。");
  }
  const certificateSha256 = typeof artifact.certificateSha256 === "string"
    ? artifact.certificateSha256.trim().replace(/:/g, "").toLowerCase()
    : "";
  if (
    artifact.sha256 !== expected.actualSha256 ||
    artifact.downloadBytes !== expected.downloadBytes ||
    artifact.packageId !== PACKAGE_ID ||
    artifact.versionName !== expected.versionName ||
    artifact.versionCode !== expected.versionCode ||
    artifact.debuggable !== false ||
    artifact.signerCount !== 1 ||
    certificateSha256 !== expected.expectedCertificateSha256 ||
    !Number.isSafeInteger(artifact.targetSdk) ||
    (artifact.targetSdk as number) < 34 ||
    !Array.isArray(artifact.abis) ||
    artifact.abis.length !== 1 ||
    artifact.abis[0] !== "arm64-v8a" ||
    typeof artifact.fileName !== "string" ||
    !isReleaseArtifactName(artifact.fileName)
  ) {
    throw new ApiError(400, "正式版审计报告与上传 APK 或发布策略不匹配。");
  }
  if (
    !Number.isSafeInteger(artifact.publishedVersionCode) ||
    (artifact.publishedVersionCode as number) < 0 ||
    (artifact.publishedVersionCode as number) >= expected.versionCode ||
    (!expected.allowEarlierBaseline && artifact.publishedVersionCode !== expected.currentVersionCode)
  ) {
    throw new ApiError(409, "审计报告所用的线上 versionCode 基线已过期。");
  }
  if (
    !Array.isArray(artifact.permissions) ||
    artifact.permissions.some((permission) => typeof permission !== "string" || !ALLOWED_PERMISSIONS.has(permission)) ||
    new Set(artifact.permissions).size !== artifact.permissions.length
  ) {
    throw new ApiError(400, "正式版审计报告包含无效或未允许的权限。");
  }
  if (!Array.isArray(value.checks) || value.checks.length !== REQUIRED_AUDIT_CHECKS.length) {
    throw new ApiError(400, "正式版审计报告缺少检查结果。");
  }
  const passedNames = new Set<string>();
  for (const check of value.checks) {
    if (
      !isRecord(check) ||
      typeof check.name !== "string" ||
      check.passed !== true ||
      !REQUIRED_AUDIT_CHECKS.includes(check.name as typeof REQUIRED_AUDIT_CHECKS[number]) ||
      passedNames.has(check.name)
    ) {
      throw new ApiError(400, "正式版审计报告含有重复、未知或失败的检查结果。");
    }
    passedNames.add(check.name);
  }
  if (REQUIRED_AUDIT_CHECKS.some((name) => !passedNames.has(name))) {
    throw new ApiError(400, "正式版审计报告未通过全部必需检查。");
  }
  return { targetSdk: artifact.targetSdk as number, auditedAt: new Date(auditedTime).toISOString() };
}

function isReleaseArtifactName(value: string): boolean {
  const normalized = value.toLowerCase();
  return normalized.endsWith(".apk") && normalized.includes("release") &&
    !normalized.includes("smoke") && !normalized.includes("emulator") && !normalized.includes("development");
}

function assertSha256(value: string): void {
  if (!SHA256_PATTERN.test(value)) {
    throw new ApiError(400, "APK SHA-256 必须是 64 位小写十六进制字符串。");
  }
}

function releaseFileName(versionName: string, versionCode: number, sha256: string): string {
  return `universal-gacha-simulator-${versionName}+${versionCode}-${sha256.slice(0, 8)}.apk`;
}

function validPublishedAt(value: Date): string {
  if (!Number.isFinite(value.getTime())) throw new ApiError(503, "APK 发布时间无效。");
  return value.toISOString();
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

async function sha256Hex(bytes: ArrayBuffer): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(digest), (value) => value.toString(16).padStart(2, "0")).join("");
}
