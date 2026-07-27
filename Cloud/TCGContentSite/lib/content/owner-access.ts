import { ApiError } from "./api-error.ts";

const USER_EMAIL_HEADER = "oai-authenticated-user-email";
const OWNER_EMAIL_ENV = "TCG_CONTENT_OWNER_EMAIL";

export type OwnerAccessResult =
  | { allowed: true; email: string }
  | { allowed: false; status: 403 | 503; message: string };

export function resolveOwnerAccess(
  email: string,
  configuredOwnerEmail: string | undefined,
  production: boolean,
): OwnerAccessResult {
  const normalizedEmail = normalizeEmail(email);
  const normalizedOwnerEmail = normalizeEmail(configuredOwnerEmail ?? "");

  if (!normalizedOwnerEmail) {
    if (!production) return { allowed: true, email: normalizedEmail };
    return {
      allowed: false,
      status: 503,
      message: "内容站尚未配置唯一发布者账号。",
    };
  }

  if (normalizedEmail !== normalizedOwnerEmail) {
    return {
      allowed: false,
      status: 403,
      message: "当前 ChatGPT 账号没有内容发布权限。",
    };
  }

  return { allowed: true, email: normalizedEmail };
}

export function getOwnerAccess(email: string): OwnerAccessResult {
  return resolveOwnerAccess(
    email,
    process.env[OWNER_EMAIL_ENV],
    process.env.NODE_ENV === "production",
  );
}

export function requireOwnerRequest(request: Request): void {
  const userEmail = request.headers.get(USER_EMAIL_HEADER);
  if (!userEmail) {
    throw new ApiError(401, "请先通过 ChatGPT 登录发布后台。");
  }

  const access = getOwnerAccess(userEmail);
  if (!access.allowed) {
    throw new ApiError(access.status, access.message);
  }
}

export function requireOwnerWriteRequest(request: Request): void {
  requireOwnerRequest(request);
  const origin = request.headers.get("origin");
  if (origin && origin !== new URL(request.url).origin) {
    throw new ApiError(403, "拒绝跨来源的内容发布请求。");
  }
}

function normalizeEmail(value: string): string {
  return value.trim().toLowerCase();
}
