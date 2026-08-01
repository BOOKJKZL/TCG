import { apiErrorResponse } from "@/lib/content/api-error";
import { getContentBucket } from "@/lib/content/bindings";
import { rejectPublicReleaseMutation } from "@/lib/content/read-only-api";
import { serveAndroidApk } from "@/lib/releases/android-release-store";

export const dynamic = "force-dynamic";

type RouteContext = {
  params: Promise<{ sha256: string }>;
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
    const { sha256: fileName } = await context.params;
    const sha256 = fileName.endsWith(".apk") ? fileName.slice(0, -4) : fileName;
    return await serveAndroidApk(getContentBucket(), {
      sha256,
      method,
      rangeHeader: request.headers.get("range"),
    });
  } catch (error) {
    return apiErrorResponse(error);
  }
}

export const POST = rejectPublicReleaseMutation;
export const PUT = rejectPublicReleaseMutation;
export const PATCH = rejectPublicReleaseMutation;
export const DELETE = rejectPublicReleaseMutation;
