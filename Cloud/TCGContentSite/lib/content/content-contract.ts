import { ApiError } from "./api-error.ts";

export const CONTENT_CATALOG_SCHEMA_VERSION = 1;
export const MAX_CATALOG_BYTES = 1024 * 1024;
export const MAX_PACKAGE_BYTES = 100 * 1024 * 1024;

const PACKAGE_ID_PATTERN = /^[a-z0-9][a-z0-9._-]{0,79}$/;
const SHA256_PATTERN = /^[a-f0-9]{64}$/;

export type ContentPackage = {
  packageId: string;
  installRelativePath: string;
  revision: number;
  version: string;
  downloadBytes: number;
  installedBytes: number;
  sha256: string;
  archiveUrl: string;
};

export type ContentCatalog = {
  schemaVersion: 1;
  revision: number;
  packages: ContentPackage[];
};

export function parseContentCatalog(value: unknown): ContentCatalog {
  if (!isRecord(value)) {
    throw new ApiError(400, "Catalog 必须是 JSON 对象。");
  }
  requireExactKeys(value, ["schemaVersion", "revision", "packages"], "Catalog");
  if (value.schemaVersion !== CONTENT_CATALOG_SCHEMA_VERSION) {
    throw new ApiError(400, `只支持 catalog schemaVersion ${CONTENT_CATALOG_SCHEMA_VERSION}。`);
  }
  const revision = requirePositiveInteger(value.revision, "Catalog revision");
  if (!Array.isArray(value.packages) || value.packages.length === 0) {
    throw new ApiError(400, "Catalog 至少需要一个内容包。");
  }
  if (value.packages.length > 5000) {
    throw new ApiError(400, "Catalog 内容包数量超过 5000 个上限。");
  }

  const packages = value.packages.map(parsePackage);
  const packageIds = new Set<string>();
  const installPaths = new Set<string>();
  for (const item of packages) {
    if (packageIds.has(item.packageId)) {
      throw new ApiError(400, `Catalog 出现重复 packageId：${item.packageId}`);
    }
    if (installPaths.has(item.installRelativePath)) {
      throw new ApiError(400, `Catalog 出现重复安装路径：${item.installRelativePath}`);
    }
    packageIds.add(item.packageId);
    installPaths.add(item.installRelativePath);
  }

  return { schemaVersion: 1, revision, packages };
}

export function parsePackageIdentity(packageId: string, sha256: string): void {
  if (!PACKAGE_ID_PATTERN.test(packageId)) {
    throw new ApiError(400, "packageId 格式不正确。");
  }
  if (!SHA256_PATTERN.test(sha256)) {
    throw new ApiError(400, "SHA-256 必须是 64 位小写十六进制字符串。");
  }
}

export function archiveObjectKey(packageId: string, sha256: string): string {
  parsePackageIdentity(packageId, sha256);
  return `content/releases/packages/${packageId}/${sha256}.zip`;
}

export const catalogObjectKey = "content/releases/catalog.json";

function parsePackage(value: unknown, index: number): ContentPackage {
  if (!isRecord(value)) {
    throw new ApiError(400, `packages[${index}] 必须是 JSON 对象。`);
  }
  requireExactKeys(
    value,
    [
      "packageId",
      "installRelativePath",
      "revision",
      "version",
      "downloadBytes",
      "installedBytes",
      "sha256",
      "archiveUrl",
    ],
    `packages[${index}]`,
  );

  const packageId = requireString(value.packageId, `packages[${index}].packageId`, 80);
  const sha256 = requireString(value.sha256, `packages[${index}].sha256`, 64);
  parsePackageIdentity(packageId, sha256);

  const installRelativePath = requireSafeRelativePath(
    value.installRelativePath,
    `packages[${index}].installRelativePath`,
  );
  const archiveUrl = requireString(value.archiveUrl, `packages[${index}].archiveUrl`, 256);
  const expectedArchiveUrl = `packages/${packageId}/${sha256}.zip`;
  if (archiveUrl !== expectedArchiveUrl) {
    throw new ApiError(
      400,
      `packages[${index}].archiveUrl 必须是 ${expectedArchiveUrl}。`,
    );
  }

  return {
    packageId,
    installRelativePath,
    revision: requirePositiveInteger(value.revision, `packages[${index}].revision`),
    version: requireString(value.version, `packages[${index}].version`, 80),
    downloadBytes: requireBoundedBytes(
      value.downloadBytes,
      `packages[${index}].downloadBytes`,
      MAX_PACKAGE_BYTES,
    ),
    installedBytes: requireBoundedBytes(
      value.installedBytes,
      `packages[${index}].installedBytes`,
      Number.MAX_SAFE_INTEGER,
    ),
    sha256,
    archiveUrl,
  };
}

function requireSafeRelativePath(value: unknown, label: string): string {
  const path = requireString(value, label, 240);
  if (
    path.startsWith("/") ||
    path.endsWith("/") ||
    path.includes("\\") ||
    path.split("/").some((segment) => !segment || segment === "." || segment === "..")
  ) {
    throw new ApiError(400, `${label} 必须是安全的相对路径。`);
  }
  return path;
}

function requireBoundedBytes(value: unknown, label: string, maximum: number): number {
  const bytes = requirePositiveInteger(value, label);
  if (bytes > maximum) {
    throw new ApiError(400, `${label} 超过允许的大小上限。`);
  }
  return bytes;
}

function requirePositiveInteger(value: unknown, label: string): number {
  if (!Number.isSafeInteger(value) || (value as number) <= 0) {
    throw new ApiError(400, `${label} 必须是正整数。`);
  }
  return value as number;
}

function requireString(value: unknown, label: string, maximumLength: number): string {
  if (typeof value !== "string" || value.length === 0 || value.length > maximumLength) {
    throw new ApiError(400, `${label} 必须是 1–${maximumLength} 个字符的字符串。`);
  }
  return value;
}

function requireExactKeys(value: Record<string, unknown>, expected: string[], label: string): void {
  const expectedSet = new Set(expected);
  const unknown = Object.keys(value).filter((key) => !expectedSet.has(key));
  const missing = expected.filter((key) => !(key in value));
  if (unknown.length > 0 || missing.length > 0) {
    const details = [
      unknown.length > 0 ? `未知字段：${unknown.join(", ")}` : "",
      missing.length > 0 ? `缺少字段：${missing.join(", ")}` : "",
    ].filter(Boolean).join("；");
    throw new ApiError(400, `${label} 字段不符合 schema（${details}）。`);
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
