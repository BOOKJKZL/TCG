# 远程资源与托管方案

## 结论

不建议把 Google Drive 当作正式的游戏内容服务器。它适合个人备份和早期手动测试，但它是文件协作产品，不是稳定的静态资源 CDN；API 有配额，下载链接和共享权限也比对象存储复杂。

这个个人项目优先推荐：

1. **Cloudflare R2 + 版本化内容包**：当前首选。R2 可公开提供 HTTPS 对象；截至 2026-07，其 Standard 免费额度包含每月 10 GB-month 存储、100 万 Class A、1000 万 Class B 请求，直接从 R2 出网不收流量费。正式使用应绑定自有域名；`r2.dev` 官方说明仅适合非生产流量并会限速。卡牌数据/图片使用本项目已有的 ZIP、SHA-256、schema catalog 和原子安装链路。
2. **Unity CCD + Addressables**：Unity 集成最省事，适合不想维护上传脚本和 URL 的情况；费用和额度应在实际启用前再次确认。
3. **Firebase Storage**：鉴权和移动端生态完善，但公开大文件分发的费用模型通常不如 R2 简单；2026 年新 bucket 的免费额度与区域有关。
4. **Google Drive**：仅用于你自己的一两台设备测试，不作为发布架构。

这里的“没有服务器”不构成阻碍。对象存储本身就是静态 HTTPS 文件主机；本项目不需要运行后端程序，手机只需要读取远程 catalog、hash 和不可变归档。

## 推荐资源划分

APK 内只保留：

- 启动、设置、下载管理和错误提示 UI
- 字体子集、占位图、必要 shader
- 抽卡核心代码
- 一个很小的离线演示内容包

远程卡牌内容按可独立安装的 Package ID 拆分：

```text
core                    可选离线演示与共用数据
set/{game}/{set-id}     一个系列的卡牌数据与图片
product/{game}/{id}     卡包包装、概率与配列规则
language/{code}         可选语言内容
```

对象存储建议布局：

```text
content/releases/android/catalog.json
content/packages/{package-id}/{sha256}.zip
```

卡牌 JSON/图片 ZIP 在格式一致时可跨平台复用；只有通用卡背、特效、声音或其他 Unity 序列化资产需要 Addressables，并按 Android、iOS、Windows 分别构建。这样不会让卡牌内容同时维护 ZIP 与 AssetBundle 两套版本/缓存状态。

## 手机首次下载流程

项目已经提供 `IContentPackageCatalogProvider`、`ContentPackageInstallCoordinator` 和内容管理页面：

安装前置检查已经由 `ContentPackagePlanner` 统一处理：包 ID、Revision、版本、整包 SHA-256、下载/安装大小、现有收据和可用空间都会先产生可本地化的状态；UI 不应直接操作文件或自行比较版本。

下载完成后的本地 ZIP 由 `FileSystemContentPackageInstaller` 在实时 Catalog 之外验证和解压，再以同卷目录重命名提交；Hash、解压大小或路径检查失败时不会接触旧系列。收据发布失败会回滚旧目录，极端的二次回滚失败会保留事务工作区供恢复。

阶段 6C1 已加入协议无关的下载状态机和文件断点层：未完成数据保存为 `.part`，Retry 使用实际文件长度作为 offset，只有声明字节数完整时才发布 `.zip`。阶段 6C2 已加入 `HttpContentPackageByteSource`：公开地址只允许 HTTPS，新下载要求 `200`，续传要求精确匹配的 `206 Content-Range`，请求使用 identity encoding；服务器忽略 Range、返回错误总大小、压缩响应或截断连接都不会制造假完成。

正式对象应使用不可变、带版本的 URL，例如把 Revision 或 SHA-256 放进对象名。当前下载收据还没有持久化 ETag/`If-Range`，因此不能在某台手机续传期间用新文件覆盖同一远程路径；最终 ZIP 仍会由安装器用 catalog 声明的 SHA-256 再校验一次。

阶段 6C3 已把这项约束写入 schema v1 包清单：每个 archive URL 的路径必须包含完整 SHA-256，重复 Package ID、非法 descriptor、公开 HTTP、旧 revision 描述符和 `latest.zip` 类可变地址都会在下载前失败。`ContentPackageInstallCoordinator` 已串起规划、断点下载、原子安装与归档清理；真实 ZIP/HTTP fixture 已验证损坏更新保留旧内容，正确重下后才发布新收据。当前仍缺少正式 R2 catalog 的远程读取与鉴权配置。

阶段 6C4 已加入第 6 个内容管理场景：页面可显示包版本、大小、状态和进度，执行安装、更新、修复、暂停、取消、失败重试与 catalog 刷新，并把后台协调器事件切回 Unity 主线程。进入/状态/失败动画、下载开始/完成/失败音效、完成震动、双语文案、减少动态和错误 Attempt 去重已由 PlayMode fixture 验证。

1. `HttpContentPackageCatalogProvider` 读取受限 schema catalog。
2. 页面显示 catalog 声明的准确下载量，Planner 检查剩余空间和现有收据。
3. 协调器通过 Range 下载 `.part`，完成后发布 `.zip`。
4. 安装器校验 SHA-256、路径和实际解压大小，再原子替换内容目录与收据。
5. Catalog 与卡图从文件缓存离线读取；下次启动无需重新下载。
6. 后续允许用户卸载某个包，但收藏存档必须独立保留。

阶段 7B 的发布器已经生成不可变 ZIP 与 schema catalog；上传 R2 后只需替换小 catalog，应用无需重新安装即可发现新 Revision。Addressables 仍可独立用于通用特效/声音，但不再是卡牌数据和卡图发布的前置条件。

## 确定性内容发布器

打开 `Tools > Universal Gacha > Private Content Publisher` 可以扫描 `LocalContent/Imports`，选择要发布的语言/系列并设置 Catalog Revision、Package Revision 与版本。批处理入口 `Tools > Universal Gacha > Publish Base + Neo Fixtures` 只发布当前最小验证集。

发布器执行以下验证：

1. 拒绝 source/output 嵌套、链接、空包、重复 ID/路径和不可移植文件名。
2. 按 Ordinal 路径排序，忽略源文件时间，并以固定 ZIP 时间戳/属性生成归档。
3. 从实际文件计算 InstalledBytes，从最终 ZIP 计算 DownloadBytes 与 SHA-256。
4. 写入 `packages/{package-id}/{sha256}.zip`，全部包完成后才原子发布排序后的 `catalog.json`。
5. 用正式 Planner 与 `FileSystemContentPackageInstaller` 安装到隔离目录，再由 `PrivateContentCatalogProvider` 读回预期系列；验证目录无论成功失败都会清理。

当前本机 `LocalContent/Releases/android` 已有：

| Package | ZIP bytes | Installed bytes | SHA-256（缩写） |
|---|---:|---:|---|
| `en.base1` | 14,906,006 | 15,189,695 | `2522292c…beceac` |
| `en.neo1` | 16,437,718 | 16,754,096 | `f353fe80…7a861b` |

连续构建时两个 ZIP 与 catalog 共 3 个文件全部保持相同 Hash。它们只存在于 Git 忽略的本机目录，尚未上传 R2。

## 私人 R2 上传器

阶段 7C 已加入 `Tools > Universal Gacha > Private R2 Publisher`。`Offline preflight` 不访问网络，先用正式 schema reader 读取本机 catalog，逐个核对 ZIP 路径、大小与 SHA-256，并显示最终对象键。正式发布需要以下值：

| 环境变量 | 来源 | 是否秘密 |
|---|---|---|
| `GACHA_R2_S3_ENDPOINT` | R2 API Token 建立完成页提供的完整 S3 endpoint；兼容默认与 jurisdiction endpoint | 否 |
| `GACHA_R2_BUCKET` | 只存放私人游戏内容的 bucket 名 | 否 |
| `GACHA_R2_PUBLIC_BASE_URL` | 已连到该 bucket 的公开 HTTPS 自定义域名；临时测试也可用 `r2.dev` | 否 |
| `GACHA_R2_OBJECT_PREFIX` | 可选，默认 `releases/android` | 否 |
| `GACHA_R2_ACCESS_KEY_ID` | 限制到上述 bucket 的 R2 S3 Access Key ID | 是 |
| `GACHA_R2_SECRET_ACCESS_KEY` | 对应 Secret Access Key | 是 |

建议在 Cloudflare 建立只允许指定 bucket 的 `Object Read & Write` Token。上传器直接使用 S3 Signature V4，region 固定为 R2 要求的 `auto`，因此不依赖本机全局安装的 AWS CLI、rclone 或 Wrangler。endpoint 必须是无路径、无用户密码的 `https://*.r2.cloudflarestorage.com`；这样即使 UI 输入错误，也不会把长期凭据发到其他主机。Access Key 与 Secret 只留在当前 Editor 进程/环境变量，不会写入 Assets、catalog、运行配置、日志或 APK。

发布顺序和失败边界如下：

1. HEAD 检查每个内容寻址 ZIP；同名对象的大小或元数据 Hash 不一致时拒绝覆盖。
2. 上传缺少的 ZIP；已存在且匹配的 ZIP 可以复用，但两者都会从 S3 origin 完整下载并重新计算大小与 SHA-256。
3. 再从公开只读 URL 完整读取 ZIP，确认手机将使用的访问路径也返回相同字节。
4. 重新确认本机 catalog 自预检后没有变化，最后以 `no-cache, no-store` 单对象 PUT 更新 `catalog.json`。
5. S3 origin 与公开 URL 都读回相同 catalog 后，才原子生成 Git 忽略的 `LocalContent/remote-content.json`。

中途取消或失败可能留下已经验证过的内容寻址 ZIP，但不会提前移动 catalog 指针，也不会生成看似可用的本机运行配置。批处理离线入口为 `PrivateR2PublisherBatch.PreflightFromEnvironment`；未设置公开 Base URL 时只使用不可联网的 `example.invalid` 计算对象映射。真实入口为 `PrivateR2PublisherBatch.PublishFromEnvironment`，读取完整环境变量。真实执行前仍应先查看离线预检结果。

当前工具链和 8 个定向测试已完成，但因为尚未提供 bucket、S3 endpoint、公开读取 URL 与凭据，本项目没有执行真实外部上传。这是刻意的权限边界，不是静默跳过。

## 远程 catalog 私人配置

阶段 7A 已提供 `HttpContentPackageCatalogProvider`。它只接受 HTTPS；HTTP 仅允许 `127.0.0.1`/loopback 自动测试。请求固定声明 JSON 与 identity encoding，必须收到 `200 OK`，默认 15 秒超时、最大 1 MiB，并会对无 `Content-Length` 的流式响应再次计数。外部取消会继续向上传播，超时、超限、非 JSON、公开 HTTP、带用户密码或 fragment 的 URL 会返回结构化失败。

配置优先级如下：

1. Unity Editor 进程环境变量 `GACHA_CONTENT_CATALOG_URL`，适合临时测试。
2. 项目根目录 `LocalContent/remote-content.json`；目录已被 Git 忽略，适合本机私人配置。
3. 可选的 `Assets/Resources/Data/RemoteContent.json`；只适合嵌入公开读取 URL，不能放 API Token、R2 Access Key 或其他秘密。
4. Android/桌面正式包读取 `Application.persistentDataPath/remote-content.json`，以后可由私人安装脚本或设置页写入。

本机配置可以从 `Tools/Content/remote-content.example.json` 复制，格式为：

```json
{
  "catalogUrl": "https://你的公开读取域名/releases/android/catalog.json",
  "timeoutSeconds": 15,
  "maxCatalogBytes": 1048576
}
```

PowerShell 临时测试示例：

```powershell
$env:GACHA_CONTENT_CATALOG_URL = 'https://你的公开读取域名/releases/android/catalog.json'
```

不要把 R2 管理 API 凭证放进游戏。推荐让游戏对象使用公开只读的自定义域名，并依靠不可猜测不是安全边界这一事实来决定是否接受公开读取；若未来必须鉴权，应另加短期令牌服务，而不是把长期密钥打进 APK。catalog 内的归档路径仍必须包含完整 SHA-256，避免可变 URL 破坏断点续传。

## 当前 Android 私测路径

- 正式 APK 不嵌入 `LocalContent`；2026-07-24 阶段 7A 的 Android/IL2CPP 冒烟包为 74.84 MiB，包含 6 个场景，413 个 APK 条目中私人内容和 `remote-content.json` 匹配均为 0。它比阶段 5C 的 51.6 MiB 增长约 23.2 MiB，后续必须结合 IL2CPP stripping 与构建生成设置复核，而不能用删除必要字体或把卡图放回 APK 的方式掩盖。
- 非 Editor 运行时从 `Application.persistentDataPath/Content` 读取已安装 manifest 和图片。
- 在真实 R2 上传完成前，仍可连接一台已授权 Android 设备并运行 `Tools/Android/install_smoke_content.ps1`：脚本安装开发 APK，把本机 `LocalContent/Imports` 推入应用私有外部文件目录，然后启动游戏。
- 这条 ADB 路径只用于个人真机验收，不是最终玩家下载方案，也不会把卡图加入 Git 或 APK。

参考资料：

- [Unity Addressables 远程内容说明](https://docs.unity3d.com/Packages/com.unity.addressables@1.21/manual/remote-content-intro.html)
- [Cloudflare R2 定价](https://developers.cloudflare.com/r2/pricing/)
- [Cloudflare R2 公开 bucket](https://developers.cloudflare.com/r2/buckets/public-buckets/)
- [Cloudflare R2 S3 入门与 endpoint](https://developers.cloudflare.com/r2/get-started/s3/)
- [Cloudflare R2 S3 兼容性与 `auto` region](https://developers.cloudflare.com/r2/api/s3/api/)
- [Cloudflare R2 API Token](https://developers.cloudflare.com/r2/api/tokens/)
- [Google Drive API 配额](https://developers.google.com/workspace/drive/api/guides/limits)
- [Firebase 定价](https://firebase.google.com/pricing)

## 宝可梦资源注意事项

技术上可以编写批量导入器：分页读取系列和卡牌元数据、下载图片、断点续传、计算 hash、生成内容清单并构建 Addressables。可选数据源包括：

- Pokémon TCG API：主要是英文资料；有现成 JSON 仓库和图片 URL。无 API Key 时限制较低，API Key 不应放在客户端应用中。
- TCGdex：覆盖多语言和多个卡牌格式，数据库与 SDK 开源；其“超过 13 万张”包含不同语言/格式记录，不等于 13 万张独立实体卡。

但是“API/数据库代码开源”不代表卡图版权也被授权。Pokémon 官方支持页面明确要求不要把其角色、名称和设计用于项目；官方条款也对批量下载内容建立数据库有限制。因此：

- 可以为你制作通用导入工具和技术流程。
- 不应直接从 Pokémon 官方网站爬取并在公开服务器重新分发全部卡图。
- 即使只供个人使用，仍应检查数据源条款和当地法律；个人用途不是自动取得再发布权。
- 风险较低的做法是：核心仓库不包含宝可梦素材，由你在本机运行导入器，把内容放进你自己的私有存储，并且不公开分发 APK+素材包。

参考资料：

- [Pokémon 官方关于图片和素材使用的说明](https://support.pokemon.com/hc/en-us/articles/360000634094-Can-I-use-Pok%C3%A9mon-images-or-materials)
- [Pokémon TCG API 文档](https://docs.pokemontcg.io/)
- [Pokémon TCG API 使用限制](https://dev.pokemontcg.io/terms)
- [TCGdex 项目](https://github.com/tcgdex)
