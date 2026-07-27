import assert from "node:assert/strict";
import test from "node:test";
import { ApiError } from "../lib/content/api-error.ts";
import {
  catalogObjectKey,
  type ContentCatalog,
} from "../lib/content/content-contract.ts";
import {
  publishArchive,
  publishCatalog,
  serveArchive,
  serveCatalog,
  type ContentBucket,
  type StoredObjectBody,
  type StoredObjectHead,
} from "../lib/content/content-store.ts";

type Stored = {
  bytes: Uint8Array;
  customMetadata?: Record<string, string>;
};

class FakeBucket implements ContentBucket {
  readonly objects = new Map<string, Stored>();

  async head(key: string): Promise<StoredObjectHead | null> {
    const object = this.objects.get(key);
    return object ? { size: object.bytes.byteLength, customMetadata: object.customMetadata } : null;
  }

  async get(
    key: string,
    options?: { range?: { offset: number; length: number } },
  ): Promise<StoredObjectBody | null> {
    const object = this.objects.get(key);
    if (!object) return null;
    const bytes = options?.range
      ? object.bytes.slice(options.range.offset, options.range.offset + options.range.length)
      : object.bytes.slice();
    return {
      size: object.bytes.byteLength,
      customMetadata: object.customMetadata,
      body: new Response(bytes).body as ReadableStream<Uint8Array>,
      async arrayBuffer() {
        return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
      },
    };
  }

  async put(
    key: string,
    value: ArrayBuffer | Uint8Array | string,
    options?: { customMetadata?: Record<string, string> },
  ): Promise<void> {
    const bytes = typeof value === "string"
      ? new TextEncoder().encode(value)
      : value instanceof Uint8Array
        ? value.slice()
        : new Uint8Array(value.slice(0));
    this.objects.set(key, { bytes, customMetadata: options?.customMetadata });
  }
}

async function fixture(): Promise<{ bytes: ArrayBuffer; sha256: string; catalog: ContentCatalog }> {
  const source = new TextEncoder().encode("deterministic-zip-fixture");
  const bytes = source.buffer.slice(source.byteOffset, source.byteOffset + source.byteLength) as ArrayBuffer;
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  const sha256 = Array.from(new Uint8Array(digest), (value) => value.toString(16).padStart(2, "0")).join("");
  return {
    bytes,
    sha256,
    catalog: {
      schemaVersion: 1,
      revision: 1,
      packages: [{
        packageId: "en.base1",
        installRelativePath: "en/base1",
        revision: 1,
        version: "1.0.0",
        downloadBytes: bytes.byteLength,
        installedBytes: 100,
        sha256,
        archiveUrl: `packages/en.base1/${sha256}.zip`,
      }],
    },
  };
}

test("never stores an archive with a mismatched hash", async () => {
  const bucket = new FakeBucket();
  const data = await fixture();
  await assert.rejects(
    publishArchive(bucket, {
      packageId: "en.base1",
      sha256: "0".repeat(64),
      downloadBytes: data.bytes.byteLength,
      bytes: data.bytes,
    }),
    ApiError,
  );
  assert.equal(bucket.objects.size, 0);
});

test("publishes the catalog only after every verified archive exists", async () => {
  const bucket = new FakeBucket();
  const data = await fixture();
  await assert.rejects(publishCatalog(bucket, data.catalog), ApiError);
  assert.equal(bucket.objects.has(catalogObjectKey), false);

  const first = await publishArchive(bucket, {
    packageId: "en.base1",
    sha256: data.sha256,
    downloadBytes: data.bytes.byteLength,
    bytes: data.bytes,
  });
  assert.equal(first.reused, false);
  const second = await publishArchive(bucket, {
    packageId: "en.base1",
    sha256: data.sha256,
    downloadBytes: data.bytes.byteLength,
    bytes: data.bytes,
  });
  assert.equal(second.reused, true);

  const published = await publishCatalog(bucket, data.catalog);
  assert.equal(published.catalog.revision, 1);
  assert.equal(bucket.objects.has(catalogObjectKey), true);
});

test("serves exact 200, 206, HEAD, and 416 download semantics", async () => {
  const bucket = new FakeBucket();
  const data = await fixture();
  await publishArchive(bucket, {
    packageId: "en.base1",
    sha256: data.sha256,
    downloadBytes: data.bytes.byteLength,
    bytes: data.bytes,
  });
  await publishCatalog(bucket, data.catalog);

  const full = await serveArchive(bucket, {
    packageId: "en.base1", sha256: data.sha256, method: "GET", rangeHeader: null,
  });
  assert.equal(full.status, 200);
  assert.equal(full.headers.get("content-length"), String(data.bytes.byteLength));
  assert.equal(full.headers.get("content-encoding"), null);
  assert.equal(await full.text(), "deterministic-zip-fixture");

  const partial = await serveArchive(bucket, {
    packageId: "en.base1", sha256: data.sha256, method: "GET", rangeHeader: "bytes=14-",
  });
  assert.equal(partial.status, 206);
  assert.equal(partial.headers.get("content-range"), `bytes 14-${data.bytes.byteLength - 1}/${data.bytes.byteLength}`);
  assert.equal(await partial.text(), "zip-fixture");

  const head = await serveArchive(bucket, {
    packageId: "en.base1", sha256: data.sha256, method: "HEAD", rangeHeader: null,
  });
  assert.equal(head.status, 200);
  assert.equal(await head.text(), "");

  const invalid = await serveArchive(bucket, {
    packageId: "en.base1", sha256: data.sha256, method: "GET", rangeHeader: "bytes=1-4",
  });
  assert.equal(invalid.status, 416);
  assert.equal(invalid.headers.get("content-range"), `bytes */${data.bytes.byteLength}`);

  const catalog = await serveCatalog(bucket, "GET");
  assert.equal(catalog.status, 200);
  assert.match(await catalog.text(), /"schemaVersion": 1/);
});
