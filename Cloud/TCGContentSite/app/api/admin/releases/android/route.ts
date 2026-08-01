import { apiErrorResponse, ApiError } from "@/lib/content/api-error";
import { getContentBucket } from "@/lib/content/bindings";
import { requirePublisherWriteRequest } from "@/lib/content/publisher-credential";
import {
  MAX_APK_BYTES,
  publishAndroidRelease,
  readLatestAndroidRelease,
} from "@/lib/releases/android-release-store";

export const dynamic = "force-dynamic";

export async function POST(request: Request): Promise<Response> {
  try {
    await requirePublisherWriteRequest(request, getContentBucket);
    const contentType = request.headers.get("content-type")?.toLowerCase() ?? "";
    if (
      !contentType.startsWith("application/vnd.android.package-archive") &&
      !contentType.startsWith("application/octet-stream")
    ) {
      throw new ApiError(415, "APK 上传必须使用 Android APK 或二进制媒体类型。");
    }

    const contentLength = Number(request.headers.get("content-length"));
    if (Number.isFinite(contentLength) && contentLength > MAX_APK_BYTES) {
      throw new ApiError(413, "Android 安装包不能超过 200 MiB。");
    }
    const bytes = await request.arrayBuffer();
    const result = await publishAndroidRelease(getContentBucket(), {
      versionName: request.headers.get("x-release-version") ?? "",
      versionCode: Number(request.headers.get("x-release-code")),
      declaredSha256: request.headers.get("x-apk-sha256") ?? "",
      bytes,
    });
    return Response.json(result, { status: result.reused ? 200 : 201 });
  } catch (error) {
    return apiErrorResponse(error);
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
