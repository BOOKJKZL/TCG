import { apiErrorResponse, ApiError } from "@/lib/content/api-error";
import { getContentBucket } from "@/lib/content/bindings";
import { catalogObjectKey, MAX_CATALOG_BYTES } from "@/lib/content/content-contract";
import { publishCatalog } from "@/lib/content/content-store";
import { requirePublisherWriteRequest } from "@/lib/content/publisher-credential";

export const dynamic = "force-dynamic";

export async function POST(request: Request): Promise<Response> {
  try {
    await requirePublisherWriteRequest(request, getContentBucket);
    const contentType = request.headers.get("content-type")?.toLowerCase() ?? "";
    if (!contentType.startsWith("application/json")) {
      throw new ApiError(415, "Catalog 上传必须使用 application/json。");
    }
    const contentLength = Number(request.headers.get("content-length"));
    if (Number.isFinite(contentLength) && contentLength > MAX_CATALOG_BYTES) {
      throw new ApiError(413, "Catalog 超过 1 MiB 上限。");
    }

    const source = await request.text();
    if (new TextEncoder().encode(source).byteLength > MAX_CATALOG_BYTES) {
      throw new ApiError(413, "Catalog 超过 1 MiB 上限。");
    }
    let input: unknown;
    try {
      input = JSON.parse(source);
    } catch {
      throw new ApiError(400, "Catalog 不是有效 JSON。");
    }

    const result = await publishCatalog(getContentBucket(), input);
    return Response.json({
      revision: result.catalog.revision,
      packageCount: result.catalog.packages.length,
      bytes: result.bytes,
    });
  } catch (error) {
    return apiErrorResponse(error);
  }
}

export async function HEAD(request: Request): Promise<Response> {
  try {
    await requirePublisherWriteRequest(request, getContentBucket);
    const object = await getContentBucket().head(catalogObjectKey);
    if (!object) return new Response(null, { status: 404 });
    return new Response(null, {
      status: 200,
      headers: {
        "Content-Length": String(object.size),
        "X-Content-Sha256": object.customMetadata?.sha256 ?? "",
      },
    });
  } catch (error) {
    return apiErrorResponse(error);
  }
}
