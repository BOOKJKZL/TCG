import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { access, readFile } from "node:fs/promises";
import test, { after, before } from "node:test";
import { fileURLToPath } from "node:url";

const projectRoot = new URL("../", import.meta.url);
const port = 32000 + (process.pid % 1000);
const origin = `http://localhost:${port}`;
let server;
let serverOutput = "";

before(async () => {
  const cli = new URL("../node_modules/vinext/dist/cli.js", import.meta.url);
  server = spawn(process.execPath, [fileURLToPath(cli), "dev", "--port", String(port)], {
    cwd: fileURLToPath(projectRoot),
    env: { ...process.env, TCG_CONTENT_OWNER_EMAIL: "owner@example.test" },
    stdio: ["ignore", "pipe", "pipe"],
  });
  server.stdout.on("data", (chunk) => { serverOutput += chunk.toString(); });
  server.stderr.on("data", (chunk) => { serverOutput += chunk.toString(); });
  await waitForServer();
});

after(() => {
  server?.kill();
});

async function render(pathname = "/") {
  return fetch(`${origin}${pathname}`, { headers: { accept: "text/html" } });
}

async function waitForServer() {
  const deadline = Date.now() + 20_000;
  while (Date.now() < deadline) {
    if (server.exitCode !== null) throw new Error(`vinext dev exited early.\n${serverOutput}`);
    try {
      const response = await fetch(origin);
      if (response.ok) return;
    } catch {
      // Development server is still starting.
    }
    await new Promise((resolve) => setTimeout(resolve, 150));
  }
  throw new Error(`Timed out waiting for vinext dev.\n${serverOutput}`);
}

test("server-renders the content relay landing page", async () => {
  const response = await render();
  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-type") ?? "", /^text\/html\b/i);

  const html = await response.text();
  assert.match(html, /Universal Gacha Content Relay/);
  assert.match(html, /卡包离开 APK/);
  assert.match(html, /\/api\/content\/catalog\.json/);
  assert.match(html, /进入私人发布台/);
  assert.doesNotMatch(html, /react-loading-skeleton|Your site is taking shape/);
});

test("removes disposable starter assets and declares R2 binding", async () => {
  const [packageJson, hostingJson] = await Promise.all([
    readFile(new URL("../package.json", import.meta.url), "utf8"),
    readFile(new URL("../.openai/hosting.json", import.meta.url), "utf8"),
  ]);
  assert.doesNotMatch(packageJson, /react-loading-skeleton/);
  assert.deepEqual(JSON.parse(hostingJson), { d1: null, r2: "FILES" });
  await assert.rejects(access(new URL("../public/favicon.svg", import.meta.url)));
  await access(new URL("../dist/server/index.js", import.meta.url));
  await access(projectRoot);
});

test("protects publisher APIs by identity, origin, and media type", async () => {
  const anonymous = await fetch(`${origin}/api/admin/content/status`);
  assert.equal(anonymous.status, 401);

  const wrongAccount = await fetch(`${origin}/api/admin/content/status`, {
    headers: { "oai-authenticated-user-email": "other@example.test" },
  });
  assert.equal(wrongAccount.status, 403);

  const crossOrigin = await fetch(`${origin}/api/admin/content/catalog`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Origin: "https://attacker.example",
      "oai-authenticated-user-email": "owner@example.test",
    },
    body: "{}",
  });
  assert.equal(crossOrigin.status, 403);

  const wrongMediaType = await fetch(`${origin}/api/admin/content/catalog`, {
    method: "POST",
    headers: {
      "Content-Type": "text/plain",
      Origin: origin,
      "oai-authenticated-user-email": "owner@example.test",
    },
    body: "{}",
  });
  assert.equal(wrongMediaType.status, 415);
});
