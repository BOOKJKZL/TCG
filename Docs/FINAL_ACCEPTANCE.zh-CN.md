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

邮箱保护沿用小说云端的唯一管理员模型：ChatGPT 登录邮箱必须在服务器端精确匹配 `TCG_CONTENT_OWNER_EMAIL`，生产缺配置即关闭后台。游戏端不登录、不携带邮箱或令牌，只能对公开 catalog/ZIP 执行 `GET` 与 `HEAD`；四种写方法统一返回 `405`，匿名管理写请求返回 `401`。

公网已验证两条游戏资源路由的 8 个写方法请求全部为 `405 Allow: GET, HEAD`；匿名管理写请求和外部伪造身份头都为 `401`。当前 catalog 仍为 `404`，表示真实 ZIP/catalog 尚未发布，不应开始 Android 远程下载验收。

在 Site `/admin` 只选择以下最小范围：

- `LocalContent/Releases/android/catalog.json`
- `en.base1` 内容寻址 ZIP
- `en.neo1` 内容寻址 ZIP

后台会先验证/上传 ZIP，最后发布 catalog。完成后用公开 HTTPS 地址建立 Git 忽略的 `LocalContent/remote-content.json`，并重新读回 catalog、完整 ZIP、Range 片段和 SHA-256。

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

## 三、安装到一台实体 Android

连接且只连接一台已授权设备：

```powershell
& "Tools/Android/install_smoke_content.ps1" `
  -ContentMode Remote `
  -ResetDownloadedContent
```

脚本会安装最新 APK、验证手机配置不含凭据、推送 `remote-content.json` 并启动应用。

## 四、完成十四项人工验收

复制 `Docs/ANDROID_ACCEPTANCE_RECEIPT.example.json` 到：

```text
LocalContent/FinalAcceptance/android-device.json
```

只有亲自在实体设备观察通过后，才把对应值改为 `true`：

| 收据字段 | 必须观察的结果 |
|---|---|
| `installAndLaunch` | APK 安装并冷启动成功 |
| `touchNavigation` | 主要页面、按钮和滚动触摸正常 |
| `localContentLoad` | 推送到本机的私人内容可以读取 |
| `remoteFirstDownload` | 从 R2 首次下载、安装和显示成功 |
| `interruptedDownloadResume` | 下载中断后按实际 offset 继续 |
| `offlineRestart` | 已缓存内容在断网重启后可用 |
| `wifiMobileSwitch` | 网络切换不损坏下载或收藏 |
| `storageFailureSafety` | 空间不足时失败友好且不覆盖旧内容 |
| `speakerAudio` | 按钮、撕包、翻卡和五套主题音效可听且音量适中 |
| `audioFocusAndBackgroundResume` | 来电/后台切换后音频和页面恢复合理 |
| `haptics` | 开包、稀有卡等震动可用且可关闭 |
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
- ADB 只有一台授权设备、APK 与远程配置已安装。
- 十四项实体设备收据全部为 `true`。
