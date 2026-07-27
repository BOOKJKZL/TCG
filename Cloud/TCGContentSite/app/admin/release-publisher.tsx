"use client";

import { useMemo, useState } from "react";

type PublishedStatus = {
  published: boolean;
  revision?: number;
  packageCount?: number;
};

type ClientPackage = {
  packageId: string;
  sha256: string;
  downloadBytes: number;
};

type ClientCatalog = {
  revision: number;
  packages: ClientPackage[];
};

type LogEntry = {
  id: number;
  state: "active" | "success" | "error";
  message: string;
};

export default function ReleasePublisher({ initialStatus }: { initialStatus: PublishedStatus }) {
  const [status, setStatus] = useState(initialStatus);
  const [catalogFile, setCatalogFile] = useState<File | null>(null);
  const [archiveFiles, setArchiveFiles] = useState<File[]>([]);
  const [publishing, setPublishing] = useState(false);
  const [logs, setLogs] = useState<LogEntry[]>([]);

  const archiveSummary = useMemo(() => {
    if (archiveFiles.length === 0) return "尚未选择 ZIP";
    const bytes = archiveFiles.reduce((total, file) => total + file.size, 0);
    return `${archiveFiles.length} 个 ZIP · ${formatBytes(bytes)}`;
  }, [archiveFiles]);

  async function publish() {
    if (!catalogFile || publishing) return;
    setPublishing(true);
    setLogs([]);
    let nextLogId = 0;
    const append = (message: string, state: LogEntry["state"] = "active") => {
      const id = ++nextLogId;
      setLogs((current) => [...current, { id, state, message }]);
      return id;
    };
    const finish = (id: number, message: string, state: "success" | "error") => {
      setLogs((current) => current.map((entry) => entry.id === id ? { ...entry, message, state } : entry));
    };

    try {
      const catalogSource = await catalogFile.text();
      const catalog = parseClientCatalog(JSON.parse(catalogSource));
      const parseLog = append(`Catalog revision ${catalog.revision} · ${catalog.packages.length} 个内容包`);
      finish(parseLog, "Catalog 结构已读取，开始核对不可变 ZIP。", "success");

      for (const item of catalog.packages) {
        const packageLog = append(`核对 ${item.packageId}…`);
        const publicUrl = `/api/content/packages/${encodeURIComponent(item.packageId)}/${item.sha256}.zip`;
        const existing = await fetch(publicUrl, { method: "HEAD", cache: "no-store" });
        if (
          existing.ok &&
          Number(existing.headers.get("content-length")) === item.downloadBytes
        ) {
          finish(packageLog, `${item.packageId} 已在 R2，跳过重复上传。`, "success");
          continue;
        }

        const archive = findArchive(archiveFiles, item);
        if (!archive) {
          throw new Error(`缺少 ${item.packageId} 的 ZIP；请选择 ${item.sha256}.zip 或 ${item.packageId}.zip。`);
        }
        if (archive.size !== item.downloadBytes) {
          throw new Error(`${archive.name} 实际 ${archive.size} bytes，与 catalog 的 ${item.downloadBytes} 不同。`);
        }
        const query = new URLSearchParams({
          packageId: item.packageId,
          sha256: item.sha256,
          downloadBytes: String(item.downloadBytes),
        });
        const response = await fetch(`/api/admin/content/packages?${query}`, {
          method: "POST",
          headers: { "Content-Type": "application/zip" },
          body: archive,
        });
        if (!response.ok) throw new Error(await responseError(response));
        const result = await response.json() as { reused: boolean };
        finish(
          packageLog,
          result.reused ? `${item.packageId} 已验证并复用。` : `${item.packageId} 已验证并上传。`,
          "success",
        );
      }

      const catalogLog = append("所有 ZIP 就绪，正在原子切换公开 catalog…");
      const response = await fetch("/api/admin/content/catalog", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: catalogSource,
      });
      if (!response.ok) throw new Error(await responseError(response));
      const result = await response.json() as { revision: number; packageCount: number };
      setStatus({ published: true, revision: result.revision, packageCount: result.packageCount });
      finish(catalogLog, `Revision ${result.revision} 已公开，可供手机下载。`, "success");
    } catch (error) {
      const message = error instanceof Error ? error.message : "发布失败。";
      setLogs((current) => [
        ...current.map((entry) => entry.state === "active" ? { ...entry, state: "error" as const } : entry),
        { id: ++nextLogId, state: "error", message },
      ]);
    } finally {
      setPublishing(false);
    }
  }

  return (
    <>
      <section className="status-strip" aria-label="当前发布状态">
        <div><span>存储</span><strong>Sites R2</strong></div>
        <div><span>公开版本</span><strong>{status.published ? `r${status.revision}` : "未发布"}</strong></div>
        <div><span>内容包</span><strong>{status.packageCount ?? 0}</strong></div>
      </section>

      <section className="upload-panel">
        <h2>发布一组完整内容</h2>
        <p>ZIP 可以重复选择；内容寻址对象已存在时会验证后复用。Catalog 永远最后发布。</p>
        <div className="field-grid">
          <label className="file-field">
            <span>1. Catalog JSON</span>
            <small>{catalogFile ? `${catalogFile.name} · ${formatBytes(catalogFile.size)}` : "选择 publisher 生成的 catalog.json"}</small>
            <input
              type="file"
              accept="application/json,.json"
              disabled={publishing}
              onChange={(event) => setCatalogFile(event.target.files?.[0] ?? null)}
            />
          </label>
          <label className="file-field">
            <span>2. 内容 ZIP</span>
            <small>{archiveSummary}</small>
            <input
              type="file"
              accept="application/zip,.zip"
              multiple
              disabled={publishing}
              onChange={(event) => setArchiveFiles(Array.from(event.target.files ?? []))}
            />
          </label>
        </div>
        <button className="publish-button" type="button" disabled={!catalogFile || publishing} onClick={publish}>
          {publishing ? "正在验证与发布…" : "验证全部内容并发布"}
        </button>
        {logs.length > 0 && (
          <ul className="publish-log" aria-live="polite">
            {logs.map((entry) => <li key={entry.id} data-state={entry.state}>{entry.message}</li>)}
          </ul>
        )}
      </section>
    </>
  );
}

function parseClientCatalog(value: unknown): ClientCatalog {
  if (!value || typeof value !== "object") throw new Error("Catalog 必须是 JSON 对象。");
  const record = value as Record<string, unknown>;
  if (!Number.isSafeInteger(record.revision) || (record.revision as number) <= 0) {
    throw new Error("Catalog revision 不正确。");
  }
  if (!Array.isArray(record.packages) || record.packages.length === 0) {
    throw new Error("Catalog 没有内容包。");
  }
  const packages = record.packages.map((item, index) => {
    if (!item || typeof item !== "object") throw new Error(`packages[${index}] 不正确。`);
    const candidate = item as Record<string, unknown>;
    if (
      typeof candidate.packageId !== "string" ||
      typeof candidate.sha256 !== "string" ||
      !Number.isSafeInteger(candidate.downloadBytes)
    ) {
      throw new Error(`packages[${index}] 缺少 packageId、sha256 或 downloadBytes。`);
    }
    return candidate as ClientPackage;
  });
  return { revision: record.revision as number, packages };
}

function findArchive(files: File[], item: ClientPackage): File | undefined {
  const expectedNames = new Set([`${item.sha256}.zip`, `${item.packageId}.zip`]);
  return files.find((file) => expectedNames.has(file.name));
}

async function responseError(response: Response): Promise<string> {
  try {
    const body = await response.json() as { error?: string };
    return body.error ?? `服务器返回 HTTP ${response.status}。`;
  } catch {
    return `服务器返回 HTTP ${response.status}。`;
  }
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KiB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MiB`;
}
