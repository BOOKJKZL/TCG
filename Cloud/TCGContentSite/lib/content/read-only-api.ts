const ALLOWED_METHODS = "GET, HEAD";

export function rejectPublicContentMutation(): Response {
  return Response.json(
    { error: "游戏内容接口仅允许只读访问。" },
    {
      status: 405,
      headers: {
        Allow: ALLOWED_METHODS,
        "Cache-Control": "no-store",
        "X-Content-Type-Options": "nosniff",
      },
    },
  );
}

export function rejectPublicReleaseMutation(): Response {
  return Response.json(
    { error: "公开安装包接口仅允许只读访问。" },
    {
      status: 405,
      headers: {
        Allow: ALLOWED_METHODS,
        "Cache-Control": "no-store",
        "X-Content-Type-Options": "nosniff",
      },
    },
  );
}
