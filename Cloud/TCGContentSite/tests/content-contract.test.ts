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

test("accepts the exact catalog v1 contract", () => {
  assert.deepEqual(parseContentCatalog(validCatalog()), validCatalog());
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
