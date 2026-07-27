import { apiErrorResponse } from "@/lib/content/api-error";
import { getContentBucket } from "@/lib/content/bindings";
import { serveCatalog } from "@/lib/content/content-store";

export const dynamic = "force-dynamic";

export async function GET(): Promise<Response> {
  try {
    return await serveCatalog(getContentBucket(), "GET");
  } catch (error) {
    return apiErrorResponse(error);
  }
}

export async function HEAD(): Promise<Response> {
  try {
    return await serveCatalog(getContentBucket(), "HEAD");
  } catch (error) {
    return apiErrorResponse(error);
  }
}
