# Universal Gacha Content & APK Relay

万能抽卡模拟器的临时资源与 Android 安装包中继 Site。第一阶段用 Sites 托管代码，并把不可变 ZIP 与最新版 APK 存入 Site 自带 R2；未来迁移到独立 Cloudflare R2 时，保留同一 catalog、SHA-256 与 HTTP Range 契约。

## 边界

- `GET /api/content/catalog.json`：公开只读 catalog，短缓存，不启用内容压缩。
- `GET|HEAD /api/content/packages/{packageId}/{sha256}.zip`：公开只读 ZIP，支持 `bytes=<offset>-` 断点续传。
- `GET|HEAD /api/releases/android/latest.json`：公开只读的最新版 APK 资料。
- `GET|HEAD /api/releases/android/{sha256}.apk`：公开只读、内容寻址的 APK 下载，支持断点续传。
- `/admin`：需要 Sign in with ChatGPT，且登录邮箱必须等于 `TCG_CONTENT_OWNER_EMAIL`；只负责绑定或撤销电脑发布器，不读取本机文件。
- `POST|DELETE /api/admin/publisher/credential`：唯一管理员绑定/轮换或撤销电脑令牌的 SHA-256；服务器不保存令牌明文。
- `HEAD|POST /api/admin/content/packages`：已绑定电脑检查对象，服务端重新核对真实大小与 SHA-256 后写入 R2。
- `HEAD|POST /api/admin/content/catalog`：确认所有引用 ZIP 已存在并带有正确验证元数据后，最后切换公开 catalog。
- `HEAD|POST /api/admin/releases/android`：已绑定电脑上传 APK；服务端复算大小与 SHA-256，先写新 APK、最后切换最新版清单，并在成功后清理旧版。

Site 不保存 R2 管理密钥或电脑令牌明文，不使用 D1，也不把卡图、ZIP 或 APK 写进 Site 源码。服务器只在私有 R2 对象中保存发布令牌的 SHA-256；发布输入和明文令牌分别位于 Git 忽略的 `LocalContent`、`Builds` 与 `LocalContent/site-publisher-credential.json`。

## 权限模型

邮箱授权沿用小说云端的唯一管理员模式：ChatGPT 登录只负责确认当前身份，服务器再把 `oai-authenticated-user-email` 与环境变量 `TCG_CONTENT_OWNER_EMAIL` 做规范化后的精确比较。生产环境没有配置唯一邮箱时返回 `503`，其他账号返回 `403`；这些判断全部在服务器执行，不能由前端按钮绕过。

| 身份 | 客户端持有的资料 | 允许 | 明确拒绝 |
|---|---|---|---|
| 手机游戏 | 公开 `catalogUrl` | `GET` / `HEAD` 读取 catalog 和 ZIP | `POST` / `PUT` / `PATCH` / `DELETE` 均返回 `405` |
| 未登录浏览器 | 无 | 读取 catalog、ZIP 与最新版 APK | 所有 `/api/admin/**` 写操作返回 `401` |
| 错误 ChatGPT 账号 | 登录身份 | 与手机相同的公开读取 | 管理接口返回 `403` |
| 唯一发布者账号 | ChatGPT 登录会话 | 进入 `/admin`，绑定/轮换/撤销电脑发布器 | 跨来源写请求仍返回 `403` |
| 已绑定电脑发布器 | 仅存于本机的随机令牌 | 调用管理 API 发布验证后的 ZIP、catalog 与 APK | 不能更改 owner 邮箱，也不能调用公开接口的写方法 |

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
TCG_ANDROID_RELEASE_CERT_SHA256=<64 位正式版签名证书 SHA-256>
```

本机 Site 地址为 `http://localhost:3000`。生产构建由 `npm run build` 生成；`npm test` 同时覆盖页面渲染、严格 schema、ZIP/APK-first、最新版清单-last、200/206/416、只读权限和 Hash 失败路径。

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

## 发布最新版 Android APK

先按 `Docs/ANDROID_RELEASE.zh-CN.md` 构建并审计 ARM64 正式版。将与 Site 环境变量
`TCG_ANDROID_RELEASE_CERT_SHA256` 完全相同的证书指纹写入 Git 忽略的
`LocalContent/ReleaseSigning/certificate.sha256`，然后在项目根目录显式发布正式产物：

```powershell
./Tools/Android/publish_apk_to_site.ps1 `
  -ApkPath "Builds/Android/Release/UniversalGachaSimulator-release-0.2.0+2.apk" `
  -VersionName "0.2.0" `
  -VersionCode 2 `
  -ReleaseNotes "首次公开正式版"
```

脚本不再推断 smoke APK 或 `ProjectSettings.asset` 中的版本。每次上传前，它会在独立 PowerShell
进程中重新运行 `audit_release_apk.ps1`，核对包名、版本、ARM64 ABI、target SDK、调试标记、
唯一签名证书、zipalign 与发布载荷边界，并把短时有效且与 APK SHA-256 绑定的报告随请求发送。
服务器还会独立核对报告、固定的证书指纹和线上 versionCode，只有严格递增的 schema 2
`stable` 清单才能成为最新版。当前 schema 1 开发验证包仍可被服务器读取以便平滑迁移，但公开页不会把它标成正式版或提供下载。

APK 正文上传使用 R2 的 SHA-256 checksum 约束。候选对象写入后，Worker 会先通过公开下载处理器把整包
流式读完并核对字节数，再以 ETag 条件写切换 `latest`；并发或回读失败不会覆盖当前正式版。
正式 APK 的 POST 只接受已绑定电脑发布器令牌，管理员网页会话只负责绑定、撤销和查看。

当前 Site 发布 API 最多接受 60 MiB，并要求有效的 `Content-Length`；这是为避免在 Worker
内存中缓冲超大请求。若正式 APK 以后接近该上限，应先改为流式暂存与校验，不能只上调数字。
Site 只把小型 `latest.json` 作为可变指针；APK 本体使用 SHA-256 内容寻址。
新版本成功切换后清理旧 APK，发布脚本再从公开 URL 完整读回并复算 Hash。管理 API 仍只接受
已绑定的电脑令牌；浏览器发布台不提供文件上传入口。

信任边界：审计 JSON 由已绑定且受信任的私人电脑发布器提交，Site 会严格绑定其 Hash、版本、
证书指纹、检查集合和短时线上基线，但该 JSON 目前没有独立的数字签名。因此这套门禁用于阻止
误传、陈旧报告和传输/存储不一致，不宣称能抵御“发布令牌和受信任发布电脑同时失陷”。发现
令牌泄露时应立即在 `/admin` 撤销；若以后需要抵御该威胁，应增加独立审计私钥签名或服务端 APK 解析。

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
