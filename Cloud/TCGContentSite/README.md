# Universal Gacha Content Relay

万能抽卡模拟器的临时资源中继 Site。第一阶段用 Sites 托管代码并把不可变 ZIP 存入 Site 自带 R2；未来迁移到独立 Cloudflare R2 时，保留同一 catalog、SHA-256 与 HTTP Range 契约。

## 边界

- `GET /api/content/catalog.json`：公开只读 catalog，短缓存，不启用内容压缩。
- `GET|HEAD /api/content/packages/{packageId}/{sha256}.zip`：公开只读 ZIP，支持 `bytes=<offset>-` 断点续传。
- `/admin`：需要 Sign in with ChatGPT，且登录邮箱必须等于 `TCG_CONTENT_OWNER_EMAIL`。
- `POST /api/admin/content/packages`：服务端重新核对真实大小与 SHA-256 后写入 R2。
- `POST /api/admin/content/catalog`：确认所有引用 ZIP 已存在并带有正确验证元数据后，最后切换公开 catalog。

Site 不保存 R2 管理密钥，不使用 D1，也不包含卡图或发布 ZIP。发布输入仍位于仓库根目录下 Git 忽略的 `LocalContent/Releases/android`。

## 本机运行与验证

要求 Node.js `>=22.13.0`。

```bash
npm install
npm run dev
npm run lint
npm test
```

复制 `.env.example` 为 Git 忽略的 `.env.local`，填入自己的 ChatGPT 登录邮箱：

```dotenv
TCG_CONTENT_OWNER_EMAIL=you@example.com
```

本机 Site 地址为 `http://localhost:3000`。生产构建由 `npm run build` 生成；`npm test` 同时覆盖页面渲染、严格 schema、ZIP-first/catalog-last、200/206/416 和 Hash 失败路径。

## 发布两个样例系列

1. 先在 Unity 执行 `Tools > Universal Gacha > Publish Base + Neo Fixtures`。
2. 打开 Site 的 `/admin`。
3. 选择 `LocalContent/Releases/android/catalog.json`。
4. 同时选择该目录 `packages` 下的两个内容寻址 ZIP。
5. 点击“验证全部内容并发布”。
6. 把最终 Site 的 HTTPS 地址写入游戏运行配置：

```json
{
  "catalogUrl": "https://<site-host>/api/content/catalog.json",
  "timeoutSeconds": 15,
  "maxCatalogBytes": 1048576
}
```

不要把管理身份、邮箱配置或任何未来的 Cloudflare Token 放进 APK。手机只需要公开 catalog URL。

## 迁移到独立 Cloudflare R2

对象键已经固定：

```text
content/releases/catalog.json
content/releases/packages/{packageId}/{sha256}.zip
```

迁移时复制这些对象，并让新公开域名继续提供：

- catalog 的相对 `archiveUrl` 解析规则；
- ZIP 的 `Content-Length`、`Accept-Ranges: bytes` 与精确 `206 Content-Range`；
- identity 编码与不可变缓存；
- 相同 package ID、版本、字节数和 SHA-256。

完成远端读回验证后，只替换游戏配置的 `catalogUrl`。已安装内容与收藏身份不会因存储商变化而改变。
