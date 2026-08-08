"use client";

import { useState } from "react";

type PublishedStatus = {
  published: boolean;
  revision?: number;
  packageCount?: number;
};

type PublisherCredentialStatus = {
  configured: boolean;
  fingerprint?: string;
  boundAt?: string;
};

export default function ReleasePublisher({
  initialStatus,
  initialCredential,
  initialAndroidRelease,
}: {
  initialStatus: PublishedStatus;
  initialCredential: PublisherCredentialStatus;
  initialAndroidRelease: { schemaVersion: number; versionName: string; versionCode: number } | null;
}) {
  const [credential, setCredential] = useState(initialCredential);
  const [tokenSha256, setTokenSha256] = useState("");
  const [pendingAction, setPendingAction] = useState<"bind" | "revoke" | null>(null);
  const [feedback, setFeedback] = useState<{ state: "success" | "error"; message: string } | null>(null);

  async function bindCredential() {
    if (!/^[a-f0-9]{64}$/.test(tokenSha256) || pendingAction) return;
    setPendingAction("bind");
    setFeedback(null);
    try {
      const response = await fetch("/api/admin/publisher/credential", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ tokenSha256 }),
      });
      if (!response.ok) throw new Error(await responseError(response));
      setCredential(await response.json() as PublisherCredentialStatus);
      setTokenSha256("");
      setFeedback({ state: "success", message: "电脑发布器已绑定；以后可从 Unity 直接发布。" });
    } catch (error) {
      setFeedback({
        state: "error",
        message: error instanceof Error ? error.message : "绑定电脑发布器失败。",
      });
    } finally {
      setPendingAction(null);
    }
  }

  async function revokeCredential() {
    if (!credential.configured || pendingAction) return;
    setPendingAction("revoke");
    setFeedback(null);
    try {
      const response = await fetch("/api/admin/publisher/credential", { method: "DELETE" });
      if (!response.ok) throw new Error(await responseError(response));
      setCredential({ configured: false });
      setFeedback({ state: "success", message: "电脑发布器已撤销，旧令牌立即失效。" });
    } catch (error) {
      setFeedback({
        state: "error",
        message: error instanceof Error ? error.message : "撤销电脑发布器失败。",
      });
    } finally {
      setPendingAction(null);
    }
  }

  return (
    <>
      <section className="status-strip" aria-label="当前发布状态">
        <div><span>存储</span><strong>Sites R2</strong></div>
        <div><span>公开版本</span><strong>{initialStatus.published ? `r${initialStatus.revision}` : "未发布"}</strong></div>
        <div><span>内容包</span><strong>{initialStatus.packageCount ?? 0}</strong></div>
        <div>
          <span>Android APK</span>
          <strong>{initialAndroidRelease
            ? `${initialAndroidRelease.schemaVersion === 2 ? "正式版" : "旧开发验证包"} ${initialAndroidRelease.versionName}+${initialAndroidRelease.versionCode}`
            : "未发布"}</strong>
        </div>
        <div><span>电脑发布器</span><strong>{credential.configured ? "已绑定" : "未绑定"}</strong></div>
      </section>

      <section className="publisher-panel">
        <div className="publisher-heading">
          <div>
            <p className="panel-kicker">ONE-TIME PAIRING</p>
            <h2>绑定电脑端私人发布器</h2>
          </div>
          <span className="credential-state" data-state={credential.configured ? "ready" : "idle"}>
            {credential.configured ? "DIRECT API READY" : "PAIRING REQUIRED"}
          </span>
        </div>
        <p>
          Unity 在本机生成随机令牌，只把 SHA-256 粘贴到这里。服务器不保存明文；卡包与 APK 都由电脑直接上传，网页不读取文件。
        </p>

        {credential.configured && (
          <div className="credential-summary">
            <span>当前指纹</span>
            <strong>{credential.fingerprint}</strong>
            <small>{credential.boundAt ? `绑定于 ${formatDate(credential.boundAt)}` : "已绑定"}</small>
          </div>
        )}

        <label className="credential-field">
          <span>Unity 显示的绑定 SHA-256</span>
          <input
            type="text"
            inputMode="text"
            autoComplete="off"
            spellCheck={false}
            maxLength={64}
            value={tokenSha256}
            placeholder="64 位小写十六进制字符串"
            disabled={pendingAction !== null}
            onChange={(event) => setTokenSha256(event.target.value.trim().toLowerCase())}
          />
        </label>

        <ol className="pairing-steps">
          <li>Unity：Tools → Universal Gacha → Sites Content Publisher</li>
          <li>生成本机凭据并复制 Binding SHA-256</li>
          <li>在这里绑定一次；以后直接从电脑发布内容与 APK</li>
        </ol>

        <div className="publisher-actions">
          <button
            className="publish-button"
            type="button"
            disabled={!/^[a-f0-9]{64}$/.test(tokenSha256) || pendingAction !== null}
            onClick={bindCredential}
          >
            {pendingAction === "bind" ? "正在绑定…" : credential.configured ? "轮换为这台发布器" : "绑定电脑发布器"}
          </button>
          <button
            className="revoke-button"
            type="button"
            disabled={!credential.configured || pendingAction !== null}
            onClick={revokeCredential}
          >
            {pendingAction === "revoke" ? "正在撤销…" : "撤销现有发布器"}
          </button>
        </div>

        {feedback && (
          <p className="publisher-feedback" data-state={feedback.state} aria-live="polite">{feedback.message}</p>
        )}
      </section>
    </>
  );
}

async function responseError(response: Response): Promise<string> {
  try {
    const body = await response.json() as { error?: string };
    return body.error ?? `服务器返回 HTTP ${response.status}。`;
  } catch {
    return `服务器返回 HTTP ${response.status}。`;
  }
}

function formatDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString("zh-CN", { hour12: false });
}
