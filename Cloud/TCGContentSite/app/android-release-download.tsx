"use client";

import { useEffect, useState } from "react";

type AndroidRelease = {
  schemaVersion: number;
  releaseChannel?: string;
  versionName: string;
  versionCode: number;
  fileName: string;
  sha256: string;
  downloadBytes: number;
  publishedAt: string;
  downloadUrl: string;
  releaseNotes?: string;
};

type ReleaseState =
  | { state: "loading" }
  | { state: "ready"; release: AndroidRelease }
  | { state: "empty" }
  | { state: "error" };

export default function AndroidReleaseDownload() {
  const [releaseState, setReleaseState] = useState<ReleaseState>({ state: "loading" });

  useEffect(() => {
    const controller = new AbortController();
    void fetch("/api/releases/android/latest.json", {
      cache: "no-store",
      signal: controller.signal,
    })
      .then(async (response) => {
        if (response.status === 404) {
          setReleaseState({ state: "empty" });
          return;
        }
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const release = await response.json() as AndroidRelease;
        if (release.schemaVersion !== 2 || release.releaseChannel !== "stable") {
          setReleaseState({ state: "empty" });
          return;
        }
        setReleaseState({ state: "ready", release });
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === "AbortError") return;
        setReleaseState({ state: "error" });
      });
    return () => controller.abort();
  }, []);

  return (
    <section className="android-release" id="android-download" aria-labelledby="android-release-title">
      <div className="release-copy">
        <p className="release-kicker">ANDROID RELEASE</p>
        <h2 id="android-release-title">下载最新游戏</h2>
        <p>
          正式版 APK 经签名、ABI、调试标记与 SHA-256 审计后由私人电脑发布器上传。这里只有公开下载权限，
          不包含卡牌资源、管理员账号或发布凭据。
        </p>
      </div>

      {releaseState.state === "loading" && (
        <div className="release-status" aria-live="polite">
          <span className="release-pulse" aria-hidden="true" />正在读取最新版本…
        </div>
      )}
      {releaseState.state === "empty" && (
        <div className="release-status">正式版暂未发布；旧的开发验证包不会在这里提供下载。</div>
      )}
      {releaseState.state === "error" && (
        <div className="release-status" role="status">暂时无法读取版本，请稍后刷新。</div>
      )}
      {releaseState.state === "ready" && (
        <div className="release-card">
          <div className="release-version">
            <span>最新正式版</span>
            <strong>{releaseState.release.versionName}</strong>
            <small>versionCode {releaseState.release.versionCode}</small>
          </div>
          <dl className="release-facts">
            <div><dt>大小</dt><dd>{formatBytes(releaseState.release.downloadBytes)}</dd></div>
            <div><dt>发布</dt><dd>{formatDate(releaseState.release.publishedAt)}</dd></div>
            <div><dt>SHA-256</dt><dd><code>{releaseState.release.sha256}</code></dd></div>
          </dl>
          <p className="release-notes"><strong>更新说明：</strong>{releaseState.release.releaseNotes}</p>
          <a className="apk-download-button" href={releaseState.release.downloadUrl} download>
            下载 Android APK
          </a>
        </div>
      )}
    </section>
  );
}

function formatBytes(bytes: number): string {
  return `${(bytes / (1024 * 1024)).toFixed(2)} MiB`;
}

function formatDate(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "未知";
  return new Intl.DateTimeFormat("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(date);
}
