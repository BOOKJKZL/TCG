import { ApiError } from "./api-error.ts";
import {
  archiveObjectKey,
  catalogObjectKey,
  MAX_CATALOG_BYTES,
  MAX_PACKAGE_BYTES,
  parseContentCatalog,
  parsePackageIdentity,
  type ContentCatalog,
} from "./content-contract.ts";
import { parseOpenEndedRange } from "./range.ts";

export type StoredObjectHead = {
  size: number;
  customMetadata?: Record<string, string>;
};

export type StoredObjectBody = StoredObjectHead & {
  body: ReadableStream<Uint8Array>;
  arrayBuffer(): Promise<ArrayBuffer>;
};

export interface ContentBucket {
  head(key: string): Promise<StoredObjectHead | null>;
  get(
    key: string,
    options?: { range?: { offset: number; length: number } },
  ): Promise<StoredObjectBody | null>;
  put(
    key: string,
    value: ArrayBuffer | Uint8Array | string,
    options?: {
      httpMetadata?: { contentType?: string };
      customMetadata?: Record<string, string>;
    },
  ): Promise<unknown>;
}

export type PublishArchiveInput = {
  packageId: string;
  sha256: string;
  downloadBytes: number;
  bytes: ArrayBuffer;
};

export async function publishArchive(
  bucket: ContentBucket,
  input: PublishArchiveInput,
): Promise<{ key: string; reused: boolean }> {
  parsePackageIdentity(input.packageId, input.sha256);
  if (
    !Number.isSafeInteger(input.downloadBytes) ||
    input.downloadBytes <= 0 ||
    input.downloadBytes > MAX_PACKAGE_BYTES
  ) {
    throw new ApiError(400, "内容包声明大小不正确或超过 100 MiB 上限。");
  }
  if (input.bytes.byteLength !== input.downloadBytes) {
    throw new ApiError(
      400,
      `ZIP 实际大小 ${input.bytes.byteLength} 与 catalog 声明 ${input.downloadBytes} 不一致。`,
    );
  }

  const actualSha256 = await sha256Hex(input.bytes);
  if (actualSha256 !== input.sha256) {
    throw new ApiError(400, `ZIP SHA-256 不一致；实际值为 ${actualSha256}。`);
  }

  const key = archiveObjectKey(input.packageId, input.sha256);
  const existing = await bucket.head(key);
  if (existing) {
    const metadataMatches =
      existing.size === input.downloadBytes &&
      existing.customMetadata?.sha256 === input.sha256 &&
      existing.customMetadata?.packageId === input.packageId;
    if (!metadataMatches) {
      throw new ApiError(409, "同一内容寻址键已存在，但大小或验证元数据不同。");
    }
    return { key, reused: true };
  }

  await bucket.put(key, input.bytes, {
    httpMetadata: { contentType: "application/zip" },
    customMetadata: {
      sha256: input.sha256,
      packageId: input.packageId,
      downloadBytes: String(input.downloadBytes),
    },
  });
  return { key, reused: false };
}

export async function publishCatalog(
  bucket: ContentBucket,
  input: unknown,
): Promise<{ catalog: ContentCatalog; bytes: number }> {
  const catalog = parseContentCatalog(input);
  const serialized = `${JSON.stringify(catalog, null, 2)}\n`;
  const encoded = new TextEncoder().encode(serialized);
  if (encoded.byteLength > MAX_CATALOG_BYTES) {
    throw new ApiError(400, "Catalog 超过 1 MiB 上限。");
  }

  for (const item of catalog.packages) {
    const object = await bucket.head(archiveObjectKey(item.packageId, item.sha256));
    if (!object) {
      throw new ApiError(409, `内容包尚未上传：${item.packageId}`);
    }
    if (
      object.size !== item.downloadBytes ||
      object.customMetadata?.sha256 !== item.sha256 ||
      object.customMetadata?.packageId !== item.packageId
    ) {
      throw new ApiError(409, `内容包验证元数据不匹配：${item.packageId}`);
    }
  }

  await bucket.put(catalogObjectKey, encoded, {
    httpMetadata: { contentType: "application/json; charset=utf-8" },
    customMetadata: {
      schemaVersion: String(catalog.schemaVersion),
      revision: String(catalog.revision),
      packageCount: String(catalog.packages.length),
    },
  });
  return { catalog, bytes: encoded.byteLength };
}

export async function serveCatalog(
  bucket: ContentBucket,
  method: "GET" | "HEAD",
): Promise<Response> {
  const object = await bucket.get(catalogObjectKey);
  if (!object) {
    throw new ApiError(404, "Catalog 尚未发布。");
  }
  return new Response(method === "HEAD" ? null : object.body, {
    status: 200,
    headers: {
      "Accept-Ranges": "none",
      "Cache-Control": "public, max-age=60, must-revalidate",
      "Content-Length": String(object.size),
      "Content-Type": "application/json; charset=utf-8",
      "X-Content-Type-Options": "nosniff",
    },
  });
}

export async function serveArchive(
  bucket: ContentBucket,
  input: {
    packageId: string;
    sha256: string;
    method: "GET" | "HEAD";
    rangeHeader: string | null;
  },
): Promise<Response> {
  const key = archiveObjectKey(input.packageId, input.sha256);
  const head = await bucket.head(key);
  if (!head) {
    throw new ApiError(404, "找不到指定内容包。");
  }

  let range;
  try {
    range = parseOpenEndedRange(input.rangeHeader, head.size);
  } catch (error) {
    if (error instanceof ApiError && error.status === 416) {
      return Response.json(
        { error: error.message },
        {
          status: 416,
          headers: {
            "Accept-Ranges": "bytes",
            "Content-Range": `bytes */${head.size}`,
          },
        },
      );
    }
    throw error;
  }

  const headers: Record<string, string> = {
    "Accept-Ranges": "bytes",
    "Cache-Control": "public, max-age=31536000, immutable",
    "Content-Length": String(range?.length ?? head.size),
    "Content-Type": "application/zip",
    "ETag": `"sha256-${input.sha256}"`,
    "X-Content-Type-Options": "nosniff",
  };
  if (range) {
    headers["Content-Range"] = `bytes ${range.offset}-${range.end}/${head.size}`;
  }

  if (input.method === "HEAD") {
    return new Response(null, { status: range ? 206 : 200, headers });
  }

  const object = await bucket.get(
    key,
    range ? { range: { offset: range.offset, length: range.length } } : undefined,
  );
  if (!object) {
    throw new ApiError(404, "内容包在读取期间消失，请重新发布。");
  }
  return new Response(object.body, { status: range ? 206 : 200, headers });
}

export async function readPublishedStatus(bucket: ContentBucket): Promise<{
  published: boolean;
  revision?: number;
  packageCount?: number;
}> {
  const object = await bucket.get(catalogObjectKey);
  if (!object) return { published: false };

  const bytes = await object.arrayBuffer();
  const catalog = parseContentCatalog(JSON.parse(new TextDecoder().decode(bytes)));
  return {
    published: true,
    revision: catalog.revision,
    packageCount: catalog.packages.length,
  };
}

async function sha256Hex(bytes: ArrayBuffer): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(digest), (value) => value.toString(16).padStart(2, "0")).join("");
}
