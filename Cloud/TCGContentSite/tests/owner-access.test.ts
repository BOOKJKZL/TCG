import assert from "node:assert/strict";
import test from "node:test";
import { resolveOwnerAccess } from "../lib/content/owner-access.ts";

test("owner email policy normalizes the configured ChatGPT identity", () => {
  assert.deepEqual(
    resolveOwnerAccess(" Owner@Example.Test ", "owner@example.test", true),
    { allowed: true, email: "owner@example.test" },
  );
});

test("owner email policy fails closed for a different account", () => {
  assert.deepEqual(
    resolveOwnerAccess("other@example.test", "owner@example.test", true),
    {
      allowed: false,
      status: 403,
      message: "当前 ChatGPT 账号没有内容发布权限。",
    },
  );
});

test("owner email policy requires explicit production configuration", () => {
  const result = resolveOwnerAccess("owner@example.test", undefined, true);
  assert.equal(result.allowed, false);
  if (result.allowed) assert.fail("production owner access must fail closed");
  assert.equal(result.status, 503);
});

test("owner email policy keeps local development usable without weakening production", () => {
  assert.deepEqual(
    resolveOwnerAccess(" Local@Example.Test ", undefined, false),
    { allowed: true, email: "local@example.test" },
  );
});
