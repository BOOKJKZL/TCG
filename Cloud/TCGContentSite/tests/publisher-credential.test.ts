import assert from "node:assert/strict";
import test from "node:test";
import { ApiError } from "../lib/content/api-error.ts";
import type {
  ContentBucket,
  StoredObjectBody,
  StoredObjectHead,
} from "../lib/content/content-store.ts";
import {
  bindPublisherCredential,
  getPublisherCredentialStatus,
  requirePublisherWriteRequest,
  revokePublisherCredential,
} from "../lib/content/publisher-credential.ts";

type Stored = { bytes: Uint8Array; customMetadata?: Record<string, string> };

class FakeBucket implements ContentBucket {
  readonly objects = new Map<string, Stored>();

  async head(key: string): Promise<StoredObjectHead | null> {
    const item = this.objects.get(key);
    return item ? { size: item.bytes.byteLength, customMetadata: item.customMetadata } : null;
  }

  async get(key: string): Promise<StoredObjectBody | null> {
    const item = this.objects.get(key);
    if (!item) return null;
    const bytes = item.bytes.slice();
    return {
      size: bytes.byteLength,
      customMetadata: item.customMetadata,
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

  async delete(key: string): Promise<void> {
    this.objects.delete(key);
  }
}

async function tokenHash(token: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(token));
  return Array.from(new Uint8Array(digest), (value) => value.toString(16).padStart(2, "0")).join("");
}

test("owner binds only a token hash and can revoke it", async () => {
  const bucket = new FakeBucket();
  const token = "A".repeat(43);
  const hash = await tokenHash(token);
  const status = await bindPublisherCredential(bucket, hash, new Date("2026-07-27T00:00:00Z"));

  assert.equal(status.configured, true);
  assert.equal(status.boundAt, "2026-07-27T00:00:00.000Z");
  assert.match(status.fingerprint ?? "", /^[a-f0-9]{12}…[a-f0-9]{8}$/);
  const storedText = new TextDecoder().decode([...bucket.objects.values()][0].bytes);
  assert.doesNotMatch(storedText, new RegExp(token));

  await revokePublisherCredential(bucket);
  assert.deepEqual(await getPublisherCredentialStatus(bucket), { configured: false });
});

test("bound computer token authorizes cross-origin API publishing", async () => {
  const bucket = new FakeBucket();
  const token = "valid_private_publisher_token_1234567890ABCDE";
  await bindPublisherCredential(bucket, await tokenHash(token));
  const request = new Request("https://cards.example/api/admin/content/catalog", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      Origin: "https://local-editor.invalid",
    },
  });

  assert.equal(
    await requirePublisherWriteRequest(request, () => bucket),
    "publisher-token",
  );
});

test("missing, malformed, wrong, and revoked tokens fail closed", async () => {
  const bucket = new FakeBucket();
  const token = "valid_private_publisher_token_1234567890ABCDE";
  await bindPublisherCredential(bucket, await tokenHash(token));

  for (const authorization of ["Bearer short", `Bearer ${"x".repeat(43)}`, "Basic abc"]) {
    const request = new Request("https://cards.example/api/admin/content/catalog", {
      method: "POST",
      headers: { Authorization: authorization },
    });
    await assert.rejects(
      requirePublisherWriteRequest(request, () => bucket),
      (error: unknown) => error instanceof ApiError && error.status === 401,
    );
  }

  await revokePublisherCredential(bucket);
  const revoked = new Request("https://cards.example/api/admin/content/catalog", {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` },
  });
  await assert.rejects(
    requirePublisherWriteRequest(revoked, () => bucket),
    (error: unknown) => error instanceof ApiError && error.status === 503,
  );
});

test("browser owner path still enforces identity and same origin", async () => {
  const bucket = new FakeBucket();
  const owner = new Request("https://cards.example/api/admin/content/catalog", {
    method: "POST",
    headers: {
      Origin: "https://cards.example",
      "oai-authenticated-user-email": "owner@example.test",
    },
  });
  assert.equal(await requirePublisherWriteRequest(owner, () => bucket), "owner-session");

  const crossOrigin = new Request("https://cards.example/api/admin/content/catalog", {
    method: "POST",
    headers: {
      Origin: "https://attacker.example",
      "oai-authenticated-user-email": "owner@example.test",
    },
  });
  await assert.rejects(
    requirePublisherWriteRequest(crossOrigin, () => bucket),
    (error: unknown) => error instanceof ApiError && error.status === 403,
  );
});
