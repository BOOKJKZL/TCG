import { apiErrorResponse, ApiError } from "@/lib/content/api-error";
import { getContentBucket } from "@/lib/content/bindings";
import { requireOwnerRequest, requireOwnerWriteRequest } from "@/lib/content/owner-access";
import {
  bindPublisherCredential,
  getPublisherCredentialStatus,
  revokePublisherCredential,
} from "@/lib/content/publisher-credential";

export const dynamic = "force-dynamic";

export async function GET(request: Request): Promise<Response> {
  try {
    requireOwnerRequest(request);
    return Response.json(await getPublisherCredentialStatus(getContentBucket()));
  } catch (error) {
    return apiErrorResponse(error);
  }
}

export async function POST(request: Request): Promise<Response> {
  try {
    requireOwnerWriteRequest(request);
    const contentType = request.headers.get("content-type")?.toLowerCase() ?? "";
    if (!contentType.startsWith("application/json")) {
      throw new ApiError(415, "发布器绑定必须使用 application/json。");
    }
    const source = await request.text();
    if (new TextEncoder().encode(source).byteLength > 4096) {
      throw new ApiError(413, "发布器绑定请求过大。");
    }
    let input: unknown;
    try {
      input = JSON.parse(source);
    } catch {
      throw new ApiError(400, "发布器绑定请求不是有效 JSON。");
    }
    if (!input || typeof input !== "object" || Array.isArray(input)) {
      throw new ApiError(400, "发布器绑定请求必须是 JSON 对象。");
    }
    const record = input as Record<string, unknown>;
    if (Object.keys(record).length !== 1 || typeof record.tokenSha256 !== "string") {
      throw new ApiError(400, "发布器绑定请求只能包含 tokenSha256。");
    }
    return Response.json(await bindPublisherCredential(getContentBucket(), record.tokenSha256));
  } catch (error) {
    return apiErrorResponse(error);
  }
}

export async function DELETE(request: Request): Promise<Response> {
  try {
    requireOwnerWriteRequest(request);
    await revokePublisherCredential(getContentBucket());
    return new Response(null, { status: 204 });
  } catch (error) {
    return apiErrorResponse(error);
  }
}
