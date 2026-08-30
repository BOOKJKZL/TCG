import { ApiError } from "./api-error.ts";

export const CONTENT_CATALOG_SCHEMA_VERSION = 3;
export const MINIMUM_CONTENT_CATALOG_SCHEMA_VERSION = 1;
export const MAX_CATALOG_BYTES = 1024 * 1024;
export const MAX_PACKAGE_BYTES = 100 * 1024 * 1024;

const PACKAGE_ID_PATTERN = /^[a-z0-9][a-z0-9._-]{0,79}$/;
const SHA256_PATTERN = /^[a-f0-9]{64}$/;
const LANGUAGE_ID_PATTERN = /^[a-z]{2,3}(?:-[a-z0-9]{2,8})*$/;
const RELEASE_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;
const SEMANTIC_VERSION_PATTERN = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$/;
const KEY_ID_PATTERN = /^[A-Za-z0-9._-]{1,64}$/;
const BASE64_PATTERN = /^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/;

export type ContentPackageMetadata = {
  kind: string;
  gameId: string | null;
  contentLanguageId: string | null;
  localizedNames: Record<string, string>;
  setId: string | null;
  setCode: string | null;
  releaseDate: string | null;
  generationOrder: number | null;
  sortOrdinal: number | null;
  tags: string[];
  dependencies: string[];
};

export type ContentPackage = {
  packageId: string;
  installRelativePath: string;
  revision: number;
  version: string;
  downloadBytes: number;
  installedBytes: number;
  sha256: string;
  archiveUrl: string;
  metadata?: ContentPackageMetadata;
};

export type ContentCatalog = {
  schemaVersion: 1 | 2 | 3;
  revision: number;
  packages: ContentPackage[];
  minAppVersion?: string;
  contentSchemaVersion?: number;
  ruleSchemaVersion?: number;
  signature?: ContentCatalogSignature;
};

export type ContentCatalogSignature = {
  algorithm: "RS256";
  keyId: string;
  value: string;
};

export function parseContentCatalog(value: unknown): ContentCatalog {
  if (!isRecord(value)) {
    throw new ApiError(400, "Catalog 必须是 JSON 对象。");
  }
  if (
    value.schemaVersion !== MINIMUM_CONTENT_CATALOG_SCHEMA_VERSION &&
    value.schemaVersion !== 2 &&
    value.schemaVersion !== CONTENT_CATALOG_SCHEMA_VERSION
  ) {
    throw new ApiError(
      400,
      `只支持 catalog schemaVersion ${MINIMUM_CONTENT_CATALOG_SCHEMA_VERSION}–${CONTENT_CATALOG_SCHEMA_VERSION}。`,
    );
  }
  const schemaVersion = value.schemaVersion;
  requireExactKeys(
    value,
    schemaVersion === 3
      ? [
        "schemaVersion",
        "revision",
        "minAppVersion",
        "contentSchemaVersion",
        "ruleSchemaVersion",
        "packages",
        "signature",
      ]
      : ["schemaVersion", "revision", "packages"],
    "Catalog",
  );
  const revision = requirePositiveInteger(value.revision, "Catalog revision");
  if (!Array.isArray(value.packages) || value.packages.length === 0) {
    throw new ApiError(400, "Catalog 至少需要一个内容包。");
  }
  if (value.packages.length > 5000) {
    throw new ApiError(400, "Catalog 内容包数量超过 5000 个上限。");
  }

  const packages = value.packages.map((item, index) => parsePackage(item, index, schemaVersion));
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
  if (schemaVersion >= 2) {
    validateDependencies(packages, packageIds);
  }

  if (schemaVersion === 3) {
    return {
      schemaVersion,
      revision,
      minAppVersion: requireSemanticVersion(value.minAppVersion, "Catalog minAppVersion"),
      contentSchemaVersion: requirePositiveInteger(
        value.contentSchemaVersion,
        "Catalog contentSchemaVersion",
      ),
      ruleSchemaVersion: requirePositiveInteger(
        value.ruleSchemaVersion,
        "Catalog ruleSchemaVersion",
      ),
      packages,
      signature: parseSignature(value.signature),
    };
  }
  return { schemaVersion, revision, packages };
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

function parsePackage(value: unknown, index: number, schemaVersion: 1 | 2 | 3): ContentPackage {
  if (!isRecord(value)) {
    throw new ApiError(400, `packages[${index}] 必须是 JSON 对象。`);
  }
  const keys = [
    "packageId",
    "installRelativePath",
    "revision",
    "version",
    "downloadBytes",
    "installedBytes",
    "sha256",
    "archiveUrl",
  ];
  if (schemaVersion >= 2) keys.push("metadata");
  requireExactKeys(value, keys, `packages[${index}]`);

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

  const parsed: ContentPackage = {
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
  if (schemaVersion >= 2) {
    parsed.metadata = parseMetadata(value.metadata, index);
  }
  return parsed;
}

function parseSignature(value: unknown): ContentCatalogSignature {
  if (!isRecord(value)) {
    throw new ApiError(400, "Catalog signature 必须是 JSON 对象。");
  }
  requireExactKeys(value, ["algorithm", "keyId", "value"], "Catalog signature");
  if (value.algorithm !== "RS256") {
    throw new ApiError(400, "Catalog signature algorithm 必须是 RS256。");
  }
  const keyId = requireString(value.keyId, "Catalog signature keyId", 64);
  if (!KEY_ID_PATTERN.test(keyId)) {
    throw new ApiError(400, "Catalog signature keyId 格式不正确。");
  }
  const signature = requireString(value.value, "Catalog signature value", 2048);
  if (!BASE64_PATTERN.test(signature)) {
    throw new ApiError(400, "Catalog signature value 必须是 Base64。 ");
  }
  let bytes: string;
  try {
    bytes = atob(signature);
  } catch {
    throw new ApiError(400, "Catalog signature value 必须是有效 Base64。");
  }
  if (bytes.length < 128 || bytes.length > 1024) {
    throw new ApiError(400, "Catalog signature 长度不符合 RS256 签名范围。");
  }
  return { algorithm: "RS256", keyId, value: signature };
}

function parseMetadata(value: unknown, packageIndex: number): ContentPackageMetadata {
  const label = `packages[${packageIndex}].metadata`;
  if (!isRecord(value)) {
    throw new ApiError(400, `${label} 必须是 JSON 对象。`);
  }
  requireExactKeys(
    value,
    [
      "kind",
      "gameId",
      "contentLanguageId",
      "localizedNames",
      "setId",
      "setCode",
      "releaseDate",
      "generationOrder",
      "sortOrdinal",
      "tags",
      "dependencies",
    ],
    label,
  );

  const localizedNames = parseLocalizedNames(value.localizedNames, `${label}.localizedNames`);
  const contentLanguageId = requireNullableString(value.contentLanguageId, `${label}.contentLanguageId`, 32);
  if (contentLanguageId !== null && !LANGUAGE_ID_PATTERN.test(contentLanguageId)) {
    throw new ApiError(400, `${label}.contentLanguageId 格式不正确。`);
  }

  return {
    kind: requireString(value.kind, `${label}.kind`, 80),
    gameId: requireNullableString(value.gameId, `${label}.gameId`, 80),
    contentLanguageId,
    localizedNames,
    setId: requireNullableString(value.setId, `${label}.setId`, 80),
    setCode: requireNullableString(value.setCode, `${label}.setCode`, 80),
    releaseDate: requireReleaseDate(value.releaseDate, `${label}.releaseDate`),
    generationOrder: requireNullableNonNegativeInteger(
      value.generationOrder,
      `${label}.generationOrder`,
    ),
    sortOrdinal: requireNullableNonNegativeInteger(value.sortOrdinal, `${label}.sortOrdinal`),
    tags: requireStringArray(value.tags, `${label}.tags`, 32, 80, false),
    dependencies: requireStringArray(
      value.dependencies,
      `${label}.dependencies`,
      64,
      80,
      true,
    ),
  };
}

function parseLocalizedNames(value: unknown, label: string): Record<string, string> {
  if (!isRecord(value)) {
    throw new ApiError(400, `${label} 必须是 JSON 对象。`);
  }
  const entries = Object.entries(value);
  if (entries.length === 0 || entries.length > 16) {
    throw new ApiError(400, `${label} 必须包含 1–16 个名称。`);
  }

  const result: Record<string, string> = {};
  for (const [languageId, name] of entries) {
    if (!LANGUAGE_ID_PATTERN.test(languageId)) {
      throw new ApiError(400, `${label} 的语言编号格式不正确：${languageId}`);
    }
    result[languageId] = requireString(name, `${label}.${languageId}`, 160);
  }
  return result;
}

function validateDependencies(packages: ContentPackage[], packageIds: Set<string>): void {
  for (const item of packages) {
    for (const dependency of item.metadata?.dependencies ?? []) {
      if (dependency === item.packageId) {
        throw new ApiError(400, `内容包不能依赖自身：${item.packageId}`);
      }
      if (!packageIds.has(dependency)) {
        throw new ApiError(400, `内容包 ${item.packageId} 依赖不存在的 ${dependency}。`);
      }
    }
  }

  const byId = new Map(packages.map((item) => [item.packageId, item]));
  const visiting = new Set<string>();
  const visited = new Set<string>();
  const visit = (packageId: string): void => {
    if (visited.has(packageId)) return;
    if (visiting.has(packageId)) {
      throw new ApiError(400, `Catalog 依赖出现循环：${packageId}`);
    }
    visiting.add(packageId);
    for (const dependency of byId.get(packageId)?.metadata?.dependencies ?? []) visit(dependency);
    visiting.delete(packageId);
    visited.add(packageId);
  };
  for (const item of packages) visit(item.packageId);
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

function requireNullableNonNegativeInteger(value: unknown, label: string): number | null {
  if (value === null) return null;
  if (!Number.isSafeInteger(value) || (value as number) < 0) {
    throw new ApiError(400, `${label} 必须是 null 或非负整数。`);
  }
  return value as number;
}

function requireNullableString(value: unknown, label: string, maximumLength: number): string | null {
  return value === null ? null : requireString(value, label, maximumLength);
}

function requireReleaseDate(value: unknown, label: string): string | null {
  if (value === null) return null;
  const date = requireString(value, label, 10);
  const parsed = new Date(`${date}T00:00:00.000Z`);
  if (
    !RELEASE_DATE_PATTERN.test(date) ||
    Number.isNaN(parsed.valueOf()) ||
    parsed.toISOString().slice(0, 10) !== date
  ) {
    throw new ApiError(400, `${label} 必须使用有效的 yyyy-MM-dd 日期。`);
  }
  return date;
}

function requireStringArray(
  value: unknown,
  label: string,
  maximumItems: number,
  maximumLength: number,
  packageIdsOnly: boolean,
): string[] {
  if (!Array.isArray(value) || value.length > maximumItems) {
    throw new ApiError(400, `${label} 必须是最多 ${maximumItems} 项的数组。`);
  }
  const result = value.map((item, index) => requireString(item, `${label}[${index}]`, maximumLength));
  const normalized = new Set(result.map((item) => item.toLowerCase()));
  if (normalized.size !== result.length) {
    throw new ApiError(400, `${label} 不能包含重复值。`);
  }
  if (packageIdsOnly) {
    for (const packageId of result) {
      if (!PACKAGE_ID_PATTERN.test(packageId)) {
        throw new ApiError(400, `${label} 包含无效 packageId：${packageId}`);
      }
    }
  }
  return result;
}

function requireString(value: unknown, label: string, maximumLength: number): string {
  if (typeof value !== "string" || value.length === 0 || value.length > maximumLength) {
    throw new ApiError(400, `${label} 必须是 1–${maximumLength} 个字符的字符串。`);
  }
  return value;
}

function requireSemanticVersion(value: unknown, label: string): string {
  const version = requireString(value, label, 120);
  if (!SEMANTIC_VERSION_PATTERN.test(version)) {
    throw new ApiError(400, `${label} 必须是 semantic version。`);
  }
  const prerelease = version.split("+", 1)[0].split("-", 2)[1];
  if (prerelease?.split(".").some((item) => /^\d+$/.test(item) && item.length > 1 && item.startsWith("0"))) {
    throw new ApiError(400, `${label} 的预发布数字不能有前导零。`);
  }
  return version;
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
