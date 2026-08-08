import { env } from "cloudflare:workers";
import type { ContentBucket } from "./content-store.ts";

type ContentBindings = {
  FILES: R2Bucket;
  TCG_ANDROID_RELEASE_CERT_SHA256?: string;
};

export function getContentBucket(): ContentBucket {
  const bindings = env as unknown as Partial<ContentBindings>;
  if (!bindings.FILES) {
    throw new Error("Sites R2 binding FILES 尚未配置。");
  }
  return bindings.FILES as unknown as ContentBucket;
}

export function getAndroidReleaseCertificateSha256(): string {
  const bindings = env as unknown as Partial<ContentBindings>;
  const value = bindings.TCG_ANDROID_RELEASE_CERT_SHA256?.trim().replace(/:/g, "").toLowerCase() ?? "";
  if (!/^[a-f0-9]{64}$/.test(value)) {
    throw new Error("Sites 正式版签名证书 SHA-256 绑定尚未配置。");
  }
  return value;
}
