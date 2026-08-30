import assert from "node:assert/strict";
import test from "node:test";
import { ApiError } from "../lib/content/api-error.ts";
import { parseContentCatalog } from "../lib/content/content-contract.ts";
import { parseOpenEndedRange } from "../lib/content/range.ts";

function validCatalog() {
  const sha256 = "a".repeat(64);
  return {
    schemaVersion: 1,
    revision: 3,
    packages: [{
      packageId: "en.base1",
      installRelativePath: "en/base1",
      revision: 1,
      version: "1.0.0",
      downloadBytes: 123,
      installedBytes: 456,
      sha256,
      archiveUrl: `packages/en.base1/${sha256}.zip`,
    }],
  };
}

function validCatalogV2() {
  const catalog = validCatalog();
  return {
    ...catalog,
    schemaVersion: 2,
    packages: catalog.packages.map((item) => ({
      ...item,
      metadata: {
        kind: "card-set",
        gameId: "pokemon-tcg",
        contentLanguageId: "en",
        localizedNames: { en: "Base Set" },
        setId: "base1",
        setCode: "BS",
        releaseDate: "1999-01-09",
        generationOrder: 1,
        sortOrdinal: 1,
        tags: ["generation:generation-1", "pokemon"],
        dependencies: [] as string[],
      },
    })),
  };
}

function validCatalogV3() {
  return {
    ...validCatalogV2(),
    schemaVersion: 3,
    minAppVersion: "1.2.0",
    contentSchemaVersion: 1,
    ruleSchemaVersion: 1,
    signature: {
      algorithm: "RS256",
      keyId: "production-2026-01",
      value: Buffer.alloc(256, 7).toString("base64"),
    },
  };
}

test("accepts the exact catalog v1 contract", () => {
  assert.deepEqual(parseContentCatalog(validCatalog()), validCatalog());
});

test("accepts and preserves the exact catalog v2 metadata contract", () => {
  assert.deepEqual(parseContentCatalog(validCatalogV2()), validCatalogV2());
});

test("accepts and preserves the exact protected catalog v3 contract", () => {
  assert.deepEqual(parseContentCatalog(validCatalogV3()), validCatalogV3());
});

test("rejects malformed or incomplete protected catalog v3 fields", () => {
  const invalidVersion = validCatalogV3();
  invalidVersion.minAppVersion = "1.2";
  assert.throws(() => parseContentCatalog(invalidVersion), ApiError);

  const invalidAlgorithm = validCatalogV3();
  invalidAlgorithm.signature.algorithm = "none";
  assert.throws(() => parseContentCatalog(invalidAlgorithm), ApiError);

  const missingSignature = validCatalogV3();
  delete (missingSignature as { signature?: unknown }).signature;
  assert.throws(() => parseContentCatalog(missingSignature), ApiError);

  const legacyWithProtectedField = { ...validCatalogV2(), minAppVersion: "1.0.0" };
  assert.throws(() => parseContentCatalog(legacyWithProtectedField), ApiError);
});

test("rejects incomplete or malformed catalog v2 metadata", () => {
  const missingMetadata = validCatalogV2();
  delete (missingMetadata.packages[0] as { metadata?: unknown }).metadata;
  assert.throws(() => parseContentCatalog(missingMetadata), ApiError);

  const invalidDate = validCatalogV2();
  invalidDate.packages[0].metadata.releaseDate = "2025-02-29";
  assert.throws(() => parseContentCatalog(invalidDate), ApiError);

  const unknownMetadata = validCatalogV2();
  Object.assign(unknownMetadata.packages[0].metadata, { secret: "must-not-pass" });
  assert.throws(() => parseContentCatalog(unknownMetadata), ApiError);
});

test("rejects missing, self, and cyclic catalog v2 dependencies", () => {
  const missing = validCatalogV2();
  missing.packages[0].metadata.dependencies = ["en.missing"];
  assert.throws(() => parseContentCatalog(missing), ApiError);

  const self = validCatalogV2();
  self.packages[0].metadata.dependencies = ["en.base1"];
  assert.throws(() => parseContentCatalog(self), ApiError);

  const cyclic = validCatalogV2();
  cyclic.packages.push({
    ...structuredClone(cyclic.packages[0]),
    packageId: "en.base2",
    installRelativePath: "en/base2",
    sha256: "b".repeat(64),
    archiveUrl: `packages/en.base2/${"b".repeat(64)}.zip`,
  });
  cyclic.packages[0].metadata.dependencies = ["en.base2"];
  cyclic.packages[1].metadata.dependencies = ["en.base1"];
  assert.throws(() => parseContentCatalog(cyclic), ApiError);
});

test("rejects mutable URLs, traversal paths, unknown fields, and duplicates", () => {
  const mutableUrl = validCatalog();
  mutableUrl.packages[0].archiveUrl = "packages/en.base1/latest.zip";
  assert.throws(() => parseContentCatalog(mutableUrl), ApiError);

  const traversal = validCatalog();
  traversal.packages[0].installRelativePath = "../outside";
  assert.throws(() => parseContentCatalog(traversal), ApiError);

  const unknown = { ...validCatalog(), channel: "android" };
  assert.throws(() => parseContentCatalog(unknown), ApiError);

  const duplicate = validCatalog();
  duplicate.packages.push({ ...duplicate.packages[0] });
  assert.throws(() => parseContentCatalog(duplicate), ApiError);
});

test("only accepts the open-ended resume range used by the Unity client", () => {
  assert.equal(parseOpenEndedRange(null, 100), null);
  assert.deepEqual(parseOpenEndedRange("bytes=40-", 100), { offset: 40, length: 60, end: 99 });
  assert.throws(() => parseOpenEndedRange("bytes=40-80", 100), ApiError);
  assert.throws(() => parseOpenEndedRange("bytes=-20", 100), ApiError);
  assert.throws(() => parseOpenEndedRange("bytes=100-", 100), ApiError);
});
