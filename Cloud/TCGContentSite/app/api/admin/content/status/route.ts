import { apiErrorResponse } from "@/lib/content/api-error";
import { getContentBucket } from "@/lib/content/bindings";
import { readPublishedStatus } from "@/lib/content/content-store";
import { requireOwnerRequest } from "@/lib/content/owner-access";

export const dynamic = "force-dynamic";

export async function GET(request: Request): Promise<Response> {
  try {
    requireOwnerRequest(request);
    return Response.json(await readPublishedStatus(getContentBucket()));
  } catch (error) {
    return apiErrorResponse(error);
  }
}
