import { apiErrorResponse } from "@/lib/content/api-error";
import { getContentBucket } from "@/lib/content/bindings";
import { rejectPublicReleaseMutation } from "@/lib/content/read-only-api";
import { serveLatestAndroidRelease } from "@/lib/releases/android-release-store";

export const dynamic = "force-dynamic";

export async function GET(): Promise<Response> {
  return respond("GET");
}

export async function HEAD(): Promise<Response> {
  return respond("HEAD");
}

async function respond(method: "GET" | "HEAD"): Promise<Response> {
  try {
    return await serveLatestAndroidRelease(getContentBucket(), method);
  } catch (error) {
    return apiErrorResponse(error);
  }
}

export const POST = rejectPublicReleaseMutation;
export const PUT = rejectPublicReleaseMutation;
export const PATCH = rejectPublicReleaseMutation;
export const DELETE = rejectPublicReleaseMutation;
