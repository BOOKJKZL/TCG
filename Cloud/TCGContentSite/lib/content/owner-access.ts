import { ApiError } from "./api-error.ts";
import { getConfiguredOwnerEmail } from "./bindings.ts";

const USER_EMAIL_HEADER = "oai-authenticated-user-email";

export function requireOwnerRequest(request: Request): void {
  let ownerEmail: string;
  try {
    ownerEmail = getConfiguredOwnerEmail();
  } catch {
    throw new ApiError(503, "发布者邮箱尚未在 Site 环境中配置。");
  }

  const userEmail = request.headers.get(USER_EMAIL_HEADER)?.trim().toLowerCase();
  if (!userEmail) {
    throw new ApiError(401, "请先通过 ChatGPT 登录发布后台。");
  }
  if (userEmail !== ownerEmail) {
    throw new ApiError(403, "当前账号没有内容发布权限。");
  }
}

export function requireOwnerWriteRequest(request: Request): void {
  requireOwnerRequest(request);
  const origin = request.headers.get("origin");
  if (origin && origin !== new URL(request.url).origin) {
    throw new ApiError(403, "拒绝跨来源的内容发布请求。");
  }
}
