import { apiErrorResponse } from "@/lib/content/api-error";
import { getContentBucket } from "@/lib/content/bindings";
import { serveArchive } from "@/lib/content/content-store";

export const dynamic = "force-dynamic";

type RouteContext = {
  params: Promise<{ packageId: string; sha256: string }>;
};

export async function GET(request: Request, context: RouteContext): Promise<Response> {
  return respond(request, context, "GET");
}

export async function HEAD(request: Request, context: RouteContext): Promise<Response> {
  return respond(request, context, "HEAD");
}

async function respond(
  request: Request,
  context: RouteContext,
  method: "GET" | "HEAD",
): Promise<Response> {
  try {
    const { packageId, sha256: fileName } = await context.params;
    const sha256 = fileName.endsWith(".zip") ? fileName.slice(0, -4) : fileName;
    return await serveArchive(getContentBucket(), {
      packageId,
      sha256,
      method,
      rangeHeader: request.headers.get("range"),
    });
  } catch (error) {
    return apiErrorResponse(error);
  }
}
