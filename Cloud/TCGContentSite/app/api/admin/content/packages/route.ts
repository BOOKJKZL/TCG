import { apiErrorResponse, ApiError } from "@/lib/content/api-error";
import { getContentBucket } from "@/lib/content/bindings";
import { archiveObjectKey, MAX_PACKAGE_BYTES } from "@/lib/content/content-contract";
import { publishArchive } from "@/lib/content/content-store";
import { requirePublisherWriteRequest } from "@/lib/content/publisher-credential";

export const dynamic = "force-dynamic";

export async function POST(request: Request): Promise<Response> {
  try {
    await requirePublisherWriteRequest(request, getContentBucket);
    const contentType = request.headers.get("content-type")?.toLowerCase() ?? "";
    if (!contentType.startsWith("application/zip")) {
      throw new ApiError(415, "ZIP 上传必须使用 application/zip。");
    }
    const url = new URL(request.url);
    const packageId = url.searchParams.get("packageId") ?? "";
    const sha256 = url.searchParams.get("sha256") ?? "";
    const downloadBytes = Number(url.searchParams.get("downloadBytes"));
    const contentLength = Number(request.headers.get("content-length"));
    if (Number.isFinite(contentLength) && contentLength > MAX_PACKAGE_BYTES) {
      throw new ApiError(413, "ZIP 超过 100 MiB 上传上限。");
    }

    const bytes = await request.arrayBuffer();
    if (bytes.byteLength > MAX_PACKAGE_BYTES) {
      throw new ApiError(413, "ZIP 超过 100 MiB 上传上限。");
    }
    const result = await publishArchive(getContentBucket(), {
      packageId,
      sha256,
      downloadBytes,
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
    const url = new URL(request.url);
    const packageId = url.searchParams.get("packageId") ?? "";
    const sha256 = url.searchParams.get("sha256") ?? "";
    const downloadBytes = Number(url.searchParams.get("downloadBytes"));
    if (!Number.isSafeInteger(downloadBytes) || downloadBytes <= 0) {
      throw new ApiError(400, "内容包声明大小不正确。");
    }
    const object = await getContentBucket().head(archiveObjectKey(packageId, sha256));
    if (!object) return new Response(null, { status: 404 });
    return new Response(null, {
      status: 200,
      headers: {
        "Content-Length": String(object.size),
        "X-Content-Sha256": object.customMetadata?.sha256 ?? "",
        "X-Content-Package-Id": object.customMetadata?.packageId ?? "",
      },
    });
  } catch (error) {
    return apiErrorResponse(error);
  }
}
