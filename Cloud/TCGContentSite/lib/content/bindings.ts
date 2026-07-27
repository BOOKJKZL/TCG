import { env } from "cloudflare:workers";
import type { ContentBucket } from "./content-store.ts";

type ContentBindings = {
  FILES: R2Bucket;
};

export function getContentBucket(): ContentBucket {
  const bindings = env as unknown as Partial<ContentBindings>;
  if (!bindings.FILES) {
    throw new Error("Sites R2 binding FILES 尚未配置。");
  }
  return bindings.FILES as unknown as ContentBucket;
}
