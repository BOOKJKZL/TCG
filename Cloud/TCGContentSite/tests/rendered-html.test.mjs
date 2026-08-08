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
    env: {
      ...process.env,
      TCG_CONTENT_OWNER_EMAIL: "owner@example.test",
      TCG_ANDROID_RELEASE_CERT_SHA256: "a".repeat(64),
    },
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
  assert.match(html, /下载最新 APK/);
  assert.match(html, /下载最新游戏/);
  assert.match(html, /管理电脑发布器/);
  assert.doesNotMatch(html, /react-loading-skeleton|Your site is taking shape/);
});

test("removes disposable starter assets and declares the Sites project and R2 binding", async () => {
  const [packageJson, hostingJson] = await Promise.all([
    readFile(new URL("../package.json", import.meta.url), "utf8"),
    readFile(new URL("../.openai/hosting.json", import.meta.url), "utf8"),
  ]);
  assert.doesNotMatch(packageJson, /react-loading-skeleton/);
  const hosting = JSON.parse(hostingJson);
  assert.deepEqual(Object.keys(hosting).sort(), ["d1", "project_id", "r2"]);
  assert.match(hosting.project_id, /^appgprj_[a-f0-9]+$/);
  assert.equal(hosting.d1, null);
  assert.equal(hosting.r2, "FILES");
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

  const anonymousWrite = await fetch(`${origin}/api/admin/content/catalog`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: "{}",
  });
  assert.equal(anonymousWrite.status, 401);

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

  const anonymousCredential = await fetch(`${origin}/api/admin/publisher/credential`);
  assert.equal(anonymousCredential.status, 401);

  const anonymousCredentialWrite = await fetch(`${origin}/api/admin/publisher/credential`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ tokenSha256: "0".repeat(64) }),
  });
  assert.equal(anonymousCredentialWrite.status, 401);

  const crossOriginCredentialWrite = await fetch(`${origin}/api/admin/publisher/credential`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Origin: "https://attacker.example",
      "oai-authenticated-user-email": "owner@example.test",
    },
    body: JSON.stringify({ tokenSha256: "0".repeat(64) }),
  });
  assert.equal(crossOriginCredentialWrite.status, 403);

  const anonymousApkWrite = await fetch(`${origin}/api/admin/releases/android`, {
    method: "POST",
    headers: { "Content-Type": "application/vnd.android.package-archive" },
    body: "PK-not-an-apk",
  });
  assert.equal(anonymousApkWrite.status, 401);

  const wrongApkMediaType = await fetch(`${origin}/api/admin/releases/android`, {
    method: "POST",
    headers: {
      "Content-Type": "text/plain",
      Origin: origin,
      "oai-authenticated-user-email": "owner@example.test",
    },
    body: "not-an-apk",
  });
  assert.equal(wrongApkMediaType.status, 403);

  const missingStableEvidence = await fetch(`${origin}/api/admin/releases/android`, {
    method: "POST",
    headers: {
      "Content-Type": "application/vnd.android.package-archive",
      Origin: origin,
      "oai-authenticated-user-email": "owner@example.test",
    },
    body: "PK-not-an-audited-release",
  });
  assert.equal(missingStableEvidence.status, 403);
});

test("removes browser file upload controls from the owner console", async () => {
  const [publisherSource, releaseDownloadSource] = await Promise.all([
    readFile(new URL("../app/admin/release-publisher.tsx", import.meta.url), "utf8"),
    readFile(new URL("../app/android-release-download.tsx", import.meta.url), "utf8"),
  ]);
  assert.doesNotMatch(publisherSource, /type="file"|archiveFiles|catalogFile/);
  assert.match(publisherSource, /Binding SHA-256|绑定电脑发布器/);
  assert.match(publisherSource, /Android APK/);
  assert.match(releaseDownloadSource, /api\/releases\/android\/latest\.json/);
  assert.match(releaseDownloadSource, /下载 Android APK/);
  assert.match(releaseDownloadSource, /schemaVersion !== 2|releaseChannel !== "stable"/);
  assert.match(releaseDownloadSource, /旧的开发验证包不会在这里提供下载/);
  assert.doesNotMatch(releaseDownloadSource, /type="file"/);
});

test("keeps every public game content route strictly read-only", async () => {
  const routes = [
    { pathname: "/api/content/catalog.json", error: "游戏内容接口仅允许只读访问。" },
    { pathname: `/api/content/packages/en.base1/${"0".repeat(64)}.zip`, error: "游戏内容接口仅允许只读访问。" },
    { pathname: "/api/releases/android/latest.json", error: "公开安装包接口仅允许只读访问。" },
    { pathname: `/api/releases/android/${"0".repeat(64)}.apk`, error: "公开安装包接口仅允许只读访问。" },
  ];

  for (const { pathname, error } of routes) {
    for (const method of ["POST", "PUT", "PATCH", "DELETE"]) {
      const response = await fetch(`${origin}${pathname}`, { method });
      assert.equal(response.status, 405, `${method} ${pathname}`);
      assert.equal(response.headers.get("allow"), "GET, HEAD");
      assert.deepEqual(await response.json(), {
        error,
      });
    }
  }
});

test("includes private APK publication and public download routes in the worker build", async () => {
  const worker = await readFile(new URL("../dist/server/index.js", import.meta.url), "utf8");
  for (const route of [
    "/api/admin/releases/android",
    "/api/releases/android/latest.json",
    "/api/releases/android/:sha256",
  ]) {
    assert.match(worker, new RegExp(route.replaceAll("/", "\\/")));
  }
});
