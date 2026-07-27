import { env } from "cloudflare:workers";
import type { ContentBucket } from "./content-store.ts";

type ContentBindings = {
  FILES: R2Bucket;
  TCG_CONTENT_OWNER_EMAIL?: string;
};

export function getContentBucket(): ContentBucket {
  const bindings = env as unknown as Partial<ContentBindings>;
  if (!bindings.FILES) {
    throw new Error("Sites R2 binding FILES 尚未配置。");
  }
  return bindings.FILES as unknown as ContentBucket;
}

export function getConfiguredOwnerEmail(): string {
  const bindings = env as unknown as Partial<ContentBindings>;
  const ownerEmail = (
    bindings.TCG_CONTENT_OWNER_EMAIL ?? process.env.TCG_CONTENT_OWNER_EMAIL
  )?.trim().toLowerCase();
  if (!ownerEmail) {
    throw new Error("TCG_CONTENT_OWNER_EMAIL 尚未配置。");
  }
  return ownerEmail;
}
