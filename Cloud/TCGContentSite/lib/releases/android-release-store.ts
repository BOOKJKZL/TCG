import { ApiError } from "../content/api-error.ts";
import type {
  ContentBucket,
  StoredObjectHead,
} from "../content/content-store.ts";
import { parseOpenEndedRange } from "../content/range.ts";

export const MAX_APK_BYTES = 200 * 1024 * 1024;
export const latestAndroidReleaseObjectKey = "releases/android/latest.json";

const SHA256_PATTERN = /^[a-f0-9]{64}$/;
const VERSION_NAME_PATTERN = /^[0-9A-Za-z][0-9A-Za-z._+-]{0,39}$/;
const MAX_MANIFEST_BYTES = 16 * 1024;
const PRODUCT_ID = "universal-gacha-simulator";

export type AndroidRelease = {
  schemaVersion: 1;
  productId: typeof PRODUCT_ID;
  versionName: string;
  versionCode: number;
  fileName: string;
  sha256: string;
  downloadBytes: number;
  publishedAt: string;
  downloadUrl: string;
};

export type PublishAndroidReleaseInput = {
  versionName: string;
  versionCode: number;
  declaredSha256: string;
  bytes: ArrayBuffer;
};

export type PublishAndroidReleaseResult = {
  release: AndroidRelease;
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
  assertApkBytes(input.bytes);

  const actualSha256 = await sha256Hex(input.bytes);
  if (actualSha256 !== input.declaredSha256) {
    throw new ApiError(400, `APK SHA-256 不一致；实际值为 ${actualSha256}。`);
  }

  const current = await readLatestAndroidRelease(bucket);
  if (current?.sha256 === actualSha256) {
    if (current.versionName !== versionName || current.versionCode !== versionCode) {
      throw new ApiError(409, "同一 APK 已使用不同版本资料发布，拒绝改写标签。");
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
    });
    reused = true;
  } else {
    await bucket.put(key, input.bytes, {
      httpMetadata: { contentType: "application/vnd.android.package-archive" },
      customMetadata: {
        kind: "android-apk",
        sha256: actualSha256,
        versionName,
        versionCode: String(versionCode),
        downloadBytes: String(input.bytes.byteLength),
        fileName,
      },
    });
  }

  const release: AndroidRelease = {
    schemaVersion: 1,
    productId: PRODUCT_ID,
    versionName,
    versionCode,
    fileName,
    sha256: actualSha256,
    downloadBytes: input.bytes.byteLength,
    publishedAt: validPublishedAt(now),
    downloadUrl: `/api/releases/android/${actualSha256}.apk`,
  };
  const manifest = encodeManifest(release);
  await bucket.put(latestAndroidReleaseObjectKey, manifest, {
    httpMetadata: { contentType: "application/json; charset=utf-8" },
    customMetadata: {
      schemaVersion: "1",
      sha256: actualSha256,
      versionName,
      versionCode: String(versionCode),
      downloadBytes: String(input.bytes.byteLength),
    },
  });

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
  const object = await bucket.get(latestAndroidReleaseObjectKey);
  if (!object) return null;
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
  return release;
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
    "Cache-Control": "public, max-age=31536000, immutable",
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
  const release = value as Partial<AndroidRelease>;
  if (
    release.schemaVersion !== 1 ||
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
  return release as AndroidRelease;
}

function assertApkBytes(bytes: ArrayBuffer): void {
  if (bytes.byteLength <= 0) throw new ApiError(400, "Android 安装包不能为空。");
  if (bytes.byteLength > MAX_APK_BYTES) {
    throw new ApiError(413, "Android 安装包不能超过 200 MiB。");
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
    metadata.fileName !== expected.fileName
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
