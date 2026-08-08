import { apiErrorResponse, ApiError } from "@/lib/content/api-error";
import {
  getAndroidReleaseCertificateSha256,
  getContentBucket,
} from "@/lib/content/bindings";
import { requirePublisherWriteRequest } from "@/lib/content/publisher-credential";
import {
  MAX_APK_BYTES,
  publishAndroidRelease,
  readLatestAndroidRelease,
  serveAndroidApk,
} from "@/lib/releases/android-release-store";

export const dynamic = "force-dynamic";
const MAX_AUDIT_HEADER_BYTES = 16 * 1024;
const MAX_RELEASE_NOTES_HEADER_BYTES = 6 * 1024;

export async function POST(request: Request): Promise<Response> {
  try {
    const authority = await requirePublisherWriteRequest(request, getContentBucket);
    if (authority !== "publisher-token") {
      throw new ApiError(403, "正式版 APK 只能由已绑定的电脑发布器提交。");
    }
    const contentType = request.headers.get("content-type")?.toLowerCase() ?? "";
    if (
      !contentType.startsWith("application/vnd.android.package-archive") &&
      !contentType.startsWith("application/octet-stream")
    ) {
      throw new ApiError(415, "APK 上传必须使用 Android APK 或二进制媒体类型。");
    }

    const audit = decodeBase64JsonHeader(
      request.headers.get("x-release-audit"),
      MAX_AUDIT_HEADER_BYTES,
      "正式版审计报告",
    );
    const releaseNotes = decodeBase64TextHeader(
      request.headers.get("x-release-notes"),
      MAX_RELEASE_NOTES_HEADER_BYTES,
      "正式版更新说明",
    );
    const expectedCertificateSha256 = getAndroidReleaseCertificateSha256();

    const contentLengthHeader = request.headers.get("content-length");
    const contentLength = Number(contentLengthHeader);
    if (!contentLengthHeader || !Number.isSafeInteger(contentLength) || contentLength <= 0) {
      throw new ApiError(411, "APK 上传必须提供有效的 Content-Length。");
    }
    if (contentLength > MAX_APK_BYTES) {
      throw new ApiError(413, "Android 安装包不能超过 60 MiB。");
    }
    const bytes = await request.arrayBuffer();
    if (bytes.byteLength !== contentLength) {
      throw new ApiError(400, "APK 实际大小与 Content-Length 不一致。");
    }
    const result = await publishAndroidRelease(getContentBucket(), {
      versionName: request.headers.get("x-release-version") ?? "",
      versionCode: Number(request.headers.get("x-release-code")),
      declaredSha256: request.headers.get("x-apk-sha256") ?? "",
      bytes,
      audit,
      releaseNotes,
      expectedCertificateSha256,
      verifyPublicReadback: (artifact) => verifyPublicArtifactReadback(artifact),
    });
    return Response.json(result, { status: result.reused ? 200 : 201 });
  } catch (error) {
    return apiErrorResponse(error);
  }
}

async function verifyPublicArtifactReadback(
  artifact: { sha256: string; downloadBytes: number },
): Promise<void> {
  const response = await serveAndroidApk(getContentBucket(), {
    sha256: artifact.sha256,
    method: "GET",
    rangeHeader: null,
  });
  if (
    !response.ok ||
    response.headers.get("x-content-sha256") !== artifact.sha256 ||
    Number(response.headers.get("content-length")) !== artifact.downloadBytes ||
    !response.body
  ) {
    throw new ApiError(503, "正式版候选包未通过公开下载头验证，latest 未切换。");
  }
  const reader = response.body.getReader();
  let received = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      received += value.byteLength;
      if (received > artifact.downloadBytes) {
        throw new ApiError(503, "正式版候选包公开回读超过预期大小，latest 未切换。");
      }
    }
  } finally {
    reader.releaseLock();
  }
  if (received !== artifact.downloadBytes) {
    throw new ApiError(503, "正式版候选包公开回读不完整，latest 未切换。");
  }
}

function decodeBase64JsonHeader(value: string | null, maxBytes: number, label: string): unknown {
  const decoded = decodeBase64TextHeader(value, maxBytes, label);
  try {
    return JSON.parse(decoded) as unknown;
  } catch {
    throw new ApiError(400, `${label}不是有效 JSON。`);
  }
}

function decodeBase64TextHeader(value: string | null, maxBytes: number, label: string): string {
  if (!value || value.length > Math.ceil(maxBytes * 4 / 3) + 4) {
    throw new ApiError(400, `${label}缺失或超过大小上限。`);
  }
  try {
    const binary = atob(value);
    if (binary.length > maxBytes) throw new Error("too large");
    const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
    return new TextDecoder("utf-8", { fatal: true }).decode(bytes);
  } catch {
    throw new ApiError(400, `${label}不是有效的 Base64 UTF-8 内容。`);
  }
}

export async function HEAD(request: Request): Promise<Response> {
  try {
    await requirePublisherWriteRequest(request, getContentBucket);
    const release = await readLatestAndroidRelease(getContentBucket());
    if (!release) return new Response(null, { status: 404 });
    return new Response(null, {
      headers: {
        "Content-Length": String(release.downloadBytes),
        "X-Apk-Sha256": release.sha256,
        "X-Release-Version": release.versionName,
        "X-Release-Code": String(release.versionCode),
      },
    });
  } catch (error) {
    return apiErrorResponse(error);
  }
}
