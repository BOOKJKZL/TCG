# Universal Gacha Content Relay

万能抽卡模拟器的临时资源中继 Site。第一阶段用 Sites 托管代码并把不可变 ZIP 存入 Site 自带 R2；未来迁移到独立 Cloudflare R2 时，保留同一 catalog、SHA-256 与 HTTP Range 契约。

## 边界

- `GET /api/content/catalog.json`：公开只读 catalog，短缓存，不启用内容压缩。
- `GET|HEAD /api/content/packages/{packageId}/{sha256}.zip`：公开只读 ZIP，支持 `bytes=<offset>-` 断点续传。
- `/admin`：需要 Sign in with ChatGPT，且登录邮箱必须等于 `TCG_CONTENT_OWNER_EMAIL`；只负责绑定或撤销电脑发布器，不读取本机文件。
- `POST|DELETE /api/admin/publisher/credential`：唯一管理员绑定/轮换或撤销电脑令牌的 SHA-256；服务器不保存令牌明文。
- `HEAD|POST /api/admin/content/packages`：已绑定电脑检查对象，服务端重新核对真实大小与 SHA-256 后写入 R2。
- `HEAD|POST /api/admin/content/catalog`：确认所有引用 ZIP 已存在并带有正确验证元数据后，最后切换公开 catalog。

Site 不保存 R2 管理密钥或电脑令牌明文，不使用 D1，也不包含卡图或发布 ZIP。服务器只在私有 R2 对象中保存发布令牌的 SHA-256；发布输入和明文令牌分别位于 Git 忽略的 `LocalContent/Releases/android` 与 `LocalContent/site-publisher-credential.json`。

## 权限模型

邮箱授权沿用小说云端的唯一管理员模式：ChatGPT 登录只负责确认当前身份，服务器再把 `oai-authenticated-user-email` 与环境变量 `TCG_CONTENT_OWNER_EMAIL` 做规范化后的精确比较。生产环境没有配置唯一邮箱时返回 `503`，其他账号返回 `403`；这些判断全部在服务器执行，不能由前端按钮绕过。

| 身份 | 客户端持有的资料 | 允许 | 明确拒绝 |
|---|---|---|---|
| 手机游戏 | 公开 `catalogUrl` | `GET` / `HEAD` 读取 catalog 和 ZIP | `POST` / `PUT` / `PATCH` / `DELETE` 均返回 `405` |
| 未登录浏览器 | 无 | 与手机相同的公开读取 | 所有 `/api/admin/**` 写操作返回 `401` |
| 错误 ChatGPT 账号 | 登录身份 | 与手机相同的公开读取 | 管理接口返回 `403` |
| 唯一发布者账号 | ChatGPT 登录会话 | 进入 `/admin`，绑定/轮换/撤销电脑发布器 | 跨来源写请求仍返回 `403` |
| 已绑定电脑发布器 | 仅存于本机的随机令牌 | 调用管理 API 发布验证后的 ZIP 与 catalog | 不能更改 owner 邮箱，也不能调用公开游戏 API 的写方法 |

因此 APK 中只有公开 URL、超时和 catalog 大小上限；没有邮箱、ChatGPT 会话、电脑发布令牌、R2 Token、Access Key、Secret 或管理 API 地址。公开读取不等于获得 R2 权限，R2 binding 只存在于 Site 的服务器进程。

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

## 绑定电脑并发布两个样例系列

1. 先在 Unity 执行 `Tools > Universal Gacha > Publish Base + Neo Fixtures`。
2. 打开 `Tools > Universal Gacha > Sites Content Publisher`，生成本机凭据；明文只写入 Git 忽略的 `LocalContent`。
3. 复制窗口显示的 Binding SHA-256，登录 Site `/admin` 并绑定一次。后台只接收 Hash，不接收卡包或令牌明文。
4. 回到 Unity 执行离线预检，然后点击 `Publish verified release to Site`。发布器自动复用已验证 ZIP、上传缺少对象、从公网完整读回并计算 Hash，最后才切换 catalog。
5. 发布成功后，工具会原子生成 `LocalContent/remote-content.json`；无需手动把文件放进网页：

```json
{
  "catalogUrl": "https://<site-host>/api/content/catalog.json",
  "timeoutSeconds": 15,
  "maxCatalogBytes": 1048576
}
```

批处理入口为 `PrivateSitesPublisherBatch.GenerateCredentialFromEnvironment`、`PreflightFromEnvironment` 与 `PublishFromEnvironment`。首次生成会写入 Git 忽略的本机凭据文件并且只在日志显示 Binding SHA-256；后续发布默认直接读取该文件，不需要把令牌放进命令行。自动化环境仍可用 `GACHA_SITE_PUBLISH_TOKEN` 覆盖令牌，用 `GACHA_SITE_BASE_URL` 覆盖 Site URL，或用 `GACHA_SITE_CREDENTIAL_PATH` 指定另一份 Git 忽略凭据。不要把管理身份、邮箱配置、电脑发布令牌或任何未来的 Cloudflare Token 放进 APK。手机只需要公开 catalog URL。

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
