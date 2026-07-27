import { ApiError } from "./api-error.ts";
import type { ContentBucket } from "./content-store.ts";
import { requireOwnerWriteRequest } from "./owner-access.ts";

const CREDENTIAL_OBJECT_KEY = "content/private/publisher-credential.json";
const SHA256_PATTERN = /^[a-f0-9]{64}$/;
const MAX_CREDENTIAL_BYTES = 4096;

type PublisherCredentialRecord = {
  version: 1;
  tokenSha256: string;
  boundAt: string;
};

export type PublisherCredentialStatus = {
  configured: boolean;
  fingerprint?: string;
  boundAt?: string;
};

export async function requirePublisherWriteRequest(
  request: Request,
  getBucket: () => ContentBucket,
): Promise<"owner-session" | "publisher-token"> {
  const authorization = request.headers.get("authorization");
  if (!authorization) {
    requireOwnerWriteRequest(request);
    return "owner-session";
  }

  const token = parseBearerToken(authorization);
  const credential = await readCredential(getBucket());
  if (!credential) {
    throw new ApiError(503, "电脑发布器尚未由唯一管理员绑定。");
  }

  const actualSha256 = await sha256Hex(new TextEncoder().encode(token));
  if (!constantTimeEqual(actualSha256, credential.tokenSha256)) {
    throw new ApiError(401, "电脑发布器凭据无效或已被轮换。");
  }
  return "publisher-token";
}

export async function bindPublisherCredential(
  bucket: ContentBucket,
  tokenSha256: string,
  now = new Date(),
): Promise<PublisherCredentialStatus> {
  if (!SHA256_PATTERN.test(tokenSha256)) {
    throw new ApiError(400, "发布令牌 SHA-256 必须是 64 位小写十六进制字符串。");
  }

  const record: PublisherCredentialRecord = {
    version: 1,
    tokenSha256,
    boundAt: now.toISOString(),
  };
  await bucket.put(CREDENTIAL_OBJECT_KEY, `${JSON.stringify(record, null, 2)}\n`, {
    httpMetadata: { contentType: "application/json; charset=utf-8" },
    customMetadata: { version: "1", fingerprint: fingerprint(tokenSha256) },
  });
  return toStatus(record);
}

export async function revokePublisherCredential(bucket: ContentBucket): Promise<void> {
  await bucket.delete(CREDENTIAL_OBJECT_KEY);
}

export async function getPublisherCredentialStatus(
  bucket: ContentBucket,
): Promise<PublisherCredentialStatus> {
  const record = await readCredential(bucket);
  return record ? toStatus(record) : { configured: false };
}

function parseBearerToken(authorization: string): string {
  const match = /^Bearer ([A-Za-z0-9_-]{43,512})$/.exec(authorization);
  if (!match) {
    throw new ApiError(401, "电脑发布器必须提供有效的 Bearer 凭据。");
  }
  return match[1];
}

async function readCredential(bucket: ContentBucket): Promise<PublisherCredentialRecord | null> {
  const object = await bucket.get(CREDENTIAL_OBJECT_KEY);
  if (!object) return null;
  if (object.size <= 0 || object.size > MAX_CREDENTIAL_BYTES) {
    throw new ApiError(503, "电脑发布器凭据记录损坏，请由唯一管理员重新绑定。");
  }

  try {
    const source = new TextDecoder().decode(await object.arrayBuffer());
    const value = JSON.parse(source) as Partial<PublisherCredentialRecord>;
    if (
      value.version !== 1 ||
      typeof value.tokenSha256 !== "string" ||
      !SHA256_PATTERN.test(value.tokenSha256) ||
      typeof value.boundAt !== "string" ||
      !Number.isFinite(Date.parse(value.boundAt))
    ) {
      throw new Error("invalid credential record");
    }
    return value as PublisherCredentialRecord;
  } catch {
    throw new ApiError(503, "电脑发布器凭据记录损坏，请由唯一管理员重新绑定。");
  }
}

function toStatus(record: PublisherCredentialRecord): PublisherCredentialStatus {
  return {
    configured: true,
    fingerprint: fingerprint(record.tokenSha256),
    boundAt: record.boundAt,
  };
}

function fingerprint(tokenSha256: string): string {
  return `${tokenSha256.slice(0, 12)}…${tokenSha256.slice(-8)}`;
}

async function sha256Hex(bytes: Uint8Array): Promise<string> {
  const source = bytes.buffer.slice(
    bytes.byteOffset,
    bytes.byteOffset + bytes.byteLength,
  ) as ArrayBuffer;
  const digest = await crypto.subtle.digest("SHA-256", source);
  return Array.from(new Uint8Array(digest), (value) => value.toString(16).padStart(2, "0")).join("");
}

function constantTimeEqual(left: string, right: string): boolean {
  if (left.length !== right.length) return false;
  let difference = 0;
  for (let index = 0; index < left.length; index++) {
    difference |= left.charCodeAt(index) ^ right.charCodeAt(index);
  }
  return difference === 0;
}
