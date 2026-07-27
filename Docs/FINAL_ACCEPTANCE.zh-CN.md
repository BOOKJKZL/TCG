# 100% 最终验收说明

最后更新：2026-07-27

## 当前判定

仓库实现、本机内容 fixture、自动测试、正式主题音效和 Android 干净构建已经达到本机可完成范围的 100%。完整发布计划当前为 92%，剩余部分必须由真实外部状态证明：

- 真实 Site 发布与已验证的公开 HTTPS catalog：4%。
- 实体 Android 安装、远程下载和十四项人工体验验收：4%。

`Tools/Validation/project_completion_audit.ps1` 是唯一的最终百分比判定入口。它不会因为缺少凭据或手机而伪造成功；`-RequireComplete` 在不足 100% 时返回退出码 2。

## 一、保留最终自动测试证据

测试结果必须写到 `TestResults/`，不能写到 Unity 会在下次启动时清理的 `Temp/`：

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\6000.0.73f1\Editor\Unity.exe"

& $unity -batchmode -nographics -projectPath . -runTests `
  -testPlatform EditMode `
  -testResults "$PWD\TestResults\final-editmode.xml" `
  -logFile "$PWD\TestResults\final-editmode.log"

& $unity -batchmode -projectPath . -runTests `
  -testPlatform PlayMode `
  -testResults "$PWD\TestResults\final-playmode.xml" `
  -logFile "$PWD\TestResults\final-playmode.log"
```

`TestResults/` 已被 Git 忽略，只保存本机证据。

## 二、完成真实 Site 发布

`Cloud/TCGContentSite` 已公开部署到 `https://universal-gacha-content.jiejingleek.chatgpt.site`，托管环境已经配置 `TCG_CONTENT_OWNER_EMAIL`。由于 Android 必须在没有 ChatGPT 浏览器会话的情况下读取内容，Site 允许公网读取；写入 API 仍由 owner 邮箱在服务端保护。

邮箱保护沿用小说云端的唯一管理员模型：ChatGPT 登录邮箱必须在服务器端精确匹配 `TCG_CONTENT_OWNER_EMAIL`，生产缺配置即关闭后台。管理员只负责绑定电脑发布器的 SHA-256；明文令牌留在 Git 忽略的本机文件。游戏端不登录、不携带邮箱或令牌，只能对公开 catalog/ZIP 执行 `GET` 与 `HEAD`；四种写方法统一返回 `405`，匿名管理写请求返回 `401`。

电脑直传版本已上线并完成 owner 配对。公网 catalog 当前包含 `en.base1` 与 `en.neo1`；独立客户端完整读回两包并核对 14,906,006 / 16,437,718 bytes 与预期 SHA-256，中点 Range 均为精确 `206`。两条游戏资源路由的 8 个写方法请求全部为 `405 Allow: GET, HEAD`；现在可以开始 Android 远程下载验收。

首次发布按以下最小范围执行：

1. Unity 打开 `Tools > Universal Gacha > Sites Content Publisher`，生成本机凭据并复制 Binding SHA-256。
2. Site `/admin` 只绑定该 SHA-256；网页不选择或读取任何卡包文件。
3. Unity 离线预检 `LocalContent/Releases/android` 后直接发布 `en.base1`、`en.neo1` 和 catalog。
4. 发布器先验证/上传 ZIP，从公开 HTTPS 完整读回复算 SHA-256，最后发布并读回 catalog，再原子生成 Git 忽略的 `LocalContent/remote-content.json`。

批处理首次使用 `PrivateSitesPublisherBatch.GenerateCredentialFromEnvironment` 生成 Git 忽略的本机凭据，之后 `PrivateSitesPublisherBatch.PublishFromEnvironment` 会自动读取它，令牌不会出现在命令行或日志。CI 仍可用 `GACHA_SITE_PUBLISH_TOKEN` 临时覆盖；Site URL 与凭据路径分别可由 `GACHA_SITE_BASE_URL`、`GACHA_SITE_CREDENTIAL_PATH` 指定。这些值都不能进入 APK。

以下独立 Cloudflare R2 流程保留为后续迁移方案，不阻塞当前 Site 版本：

在 Cloudflare 建立仅限目标 bucket 的 Object Read & Write Token，并准备：

- `GACHA_R2_S3_ENDPOINT`
- `GACHA_R2_BUCKET`
- `GACHA_R2_PUBLIC_BASE_URL`
- `GACHA_R2_ACCESS_KEY_ID`
- `GACHA_R2_SECRET_ACCESS_KEY`
- 可选 `GACHA_R2_OBJECT_PREFIX`

推荐在 Unity 中打开：

```text
Tools/Universal Gacha/Private R2 Publisher
```

先运行离线预检，再明确点击真实发布。发布器只会先写入不可变 ZIP，验证 origin 与公开 URL 后最后发布 catalog；成功后才生成 Git 忽略的 `LocalContent/remote-content.json`。密钥不能写入 Assets、文档、命令历史、APK 或手机配置。

最小发布范围保持为 `en.base1`、`en.neo1` 和 catalog，不先上传全部历史系列。

## 三、安装到一个 Android 验收目标

连接且只连接一个已授权目标；可以是实体 Android，也可以是 `ro.kernel.qemu=1` 的 Android 模拟器：

```powershell
& "Tools/Android/install_smoke_content.ps1" `
  -ContentMode Remote `
  -ResetDownloadedContent
```

脚本会安装最新 APK、验证手机配置不含凭据、推送 `remote-content.json` 并启动应用。

## 四、完成十四项有证据验收

复制 `Docs/ANDROID_ACCEPTANCE_RECEIPT.example.json` 到：

```text
LocalContent/FinalAcceptance/android-device.json
```

收据使用 schema v2，必须声明 `environmentType`、设备/模拟器身份，并为每个 `true` 检查填写不少于 8 个字符的实际证据。模拟器可以完成软件验收，但必须保留 `physicalHaptics`、`physicalSpeakerQuality`、`cellularHandover` 三项硬件限制，不能把宿主机或模拟行为描述为实体体验：

| 收据字段 | 必须观察的结果 |
|---|---|
| `installAndLaunch` | APK 安装并冷启动成功 |
| `touchNavigation` | 主要页面、按钮和滚动输入正常；模拟器记录为指针/模拟触控 |
| `localContentLoad` | 推送到本机的私人内容可以读取 |
| `remoteFirstDownload` | 从 R2 首次下载、安装和显示成功 |
| `interruptedDownloadResume` | 下载中断后按实际 offset 继续 |
| `offlineRestart` | 已缓存内容在断网重启后可用 |
| `wifiMobileSwitch` | 网络断开/恢复不损坏下载或收藏；实体蜂窝切换另行补测 |
| `storageFailureSafety` | 空间不足时失败友好且不覆盖旧内容 |
| `speakerAudio` | 音频事件、映射和 Android 音频服务正常；实体扬声器音质另行补测 |
| `audioFocusAndBackgroundResume` | 来电/后台切换后音频和页面恢复合理 |
| `haptics` | 震动 API/开关逻辑正常；实体触感另行补测 |
| `reduceMotion` | 开启后跳过动态演出并保留静态信息 |
| `collectionPreservedAfterReinstall` | 内容卸载/重装不删除收藏存档 |
| `cloudConflictResolution` | 本地/云端冲突按预期合并或选择 |

## 五、取得 100% 结论

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File "Tools/Validation/project_completion_audit.ps1" `
  -RequireComplete
```

只有以下证据同时存在时才输出 `PROJECT COMPLETION VERIFIED: 100%`：

- EditMode 与 PlayMode 最终 XML 全部通过。
- APK 比运行时代码/资产新，且包内没有私人内容名称。
- 十个年代 WAV 和配置映射完整。
- 两个本机发布包的大小与 SHA-256 匹配 catalog。
- Site 或后续 R2 发布流程生成了已验证的 HTTPS 运行配置。
- ADB 只有一个授权 Android 目标、APK 与远程配置已安装。
- 十四项验收全部为 `true`、逐项证据完整，且收据环境与当前连接目标一致。

模拟器满足这里的“软件完成度 100%”；三项已声明硬件限制仍属于未来实体设备的体验补测，不会被表述为已经验证。
