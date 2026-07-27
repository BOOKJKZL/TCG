import { apiErrorResponse, ApiError } from "@/lib/content/api-error";
import { getContentBucket } from "@/lib/content/bindings";
import { MAX_CATALOG_BYTES } from "@/lib/content/content-contract";
import { publishCatalog } from "@/lib/content/content-store";
import { requireOwnerWriteRequest } from "@/lib/content/owner-access";

export const dynamic = "force-dynamic";

export async function POST(request: Request): Promise<Response> {
  try {
    requireOwnerWriteRequest(request);
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
