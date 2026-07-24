# 远程资源与托管方案

## 结论

不建议把 Google Drive 当作正式的游戏内容服务器。它适合个人备份和早期手动测试，但它是文件协作产品，不是稳定的静态资源 CDN；API 有配额，下载链接和共享权限也比对象存储复杂。

这个个人项目优先推荐：

1. **Cloudflare R2 + Addressables**：当前首选。R2 可公开提供 HTTPS 对象；截至 2026-07，其 Standard 免费额度包含每月 10 GB-month 存储、100 万 Class A、1000 万 Class B 请求，直接从 R2 出网不收流量费。正式使用应绑定自有域名；`r2.dev` 官方说明仅适合非生产流量并会限速。
2. **Unity CCD + Addressables**：Unity 集成最省事，适合不想维护上传脚本和 URL 的情况；费用和额度应在实际启用前再次确认。
3. **Firebase Storage**：鉴权和移动端生态完善，但公开大文件分发的费用模型通常不如 R2 简单；2026 年新 bucket 的免费额度与区域有关。
4. **Google Drive**：仅用于你自己的一两台设备测试，不作为发布架构。

这里的“没有服务器”不构成阻碍。对象存储本身就是静态 HTTPS 文件主机；本项目不需要运行后端程序，手机只需要读取远程 catalog、hash 和 asset bundle。

## 推荐资源划分

APK 内只保留：

- 启动、设置、下载管理和错误提示 UI
- 字体子集、占位图、必要 shader
- 抽卡核心代码
- 一个很小的离线演示内容包

远程内容按 Addressables label 拆分：

```text
core                    通用卡背、开包特效、共用声音
set/{game}/{set-id}     一个系列的卡牌数据与图片
product/{game}/{id}     卡包包装、概率与配列规则
language/{code}         可选语言内容
```

对象存储建议布局：

```text
content/android/catalog.json
content/android/catalog.hash
content/android/core/*.bundle
content/android/sets/{set-id}/*.bundle
content/android/products/{product-id}/*.bundle
```

Addressables bundle 与平台相关，因此 Android、iOS 和 Windows 必须分别构建，不能共用同一批 bundle。

## 手机首次下载流程

项目已经提供 `IContentDeliveryService` 和 `AddressablesContentDeliveryService`，可供下载页面调用：

1. 启动 Addressables 并检查远程 catalog 更新。
2. 使用 `GetDownloadSizeAsync(label)` 获取准确下载量。
3. 检查 Wi-Fi、剩余空间并让用户确认。
4. 使用 `DownloadAsync(label, progress)` 显示下载进度。
5. Addressables 校验并缓存 bundle；下次直接读取缓存。
6. 允许用户在内容管理页删除某个系列的缓存。

发布时启用 Build Remote Catalog，把远程组设为 `RemoteBuildPath/RemoteLoadPath`，构建后上传 bundle、catalog JSON 和 hash。Unity 官方说明远程 catalog 可以让应用不重新安装就发现更新，只下载变化的 bundle。

## 当前 Android 私测路径

- 正式 APK 不嵌入 `LocalContent`；2026-07-24 的 Android/IL2CPP 冒烟包约 51.6 MiB，包内私人内容条目为 0。
- 非 Editor 运行时从 `Application.persistentDataPath/Content` 读取已安装 manifest 和图片。
- 在阶段 6 下载 UI 完成前，可连接一台已授权 Android 设备并运行 `Tools/Android/install_smoke_content.ps1`：脚本安装开发 APK，把本机 `LocalContent/Imports` 推入应用私有外部文件目录，然后启动游戏。
- 这条 ADB 路径只用于个人真机验收，不是最终玩家下载方案，也不会把卡图加入 Git 或 APK。

参考资料：

- [Unity Addressables 远程内容说明](https://docs.unity3d.com/Packages/com.unity.addressables@1.21/manual/remote-content-intro.html)
- [Cloudflare R2 定价](https://developers.cloudflare.com/r2/pricing/)
- [Cloudflare R2 公开 bucket](https://developers.cloudflare.com/r2/buckets/public-buckets/)
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
