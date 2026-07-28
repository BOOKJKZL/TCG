# 100% 最终验收说明

最后更新：2026-07-29

## 当前判定

仓库实现、本机内容 fixture、自动测试、正式主题音效、Site 发布与 Android 干净构建已经完成。公开 HTTPS catalog 与两个最小卡包已经过完整读取、Range 和 SHA-256 验证；最近一次正式项目审计为 96%。剩余 4% 是 Android 十四项有证据验收，不能因为模拟器能够启动或个别下载成功而提前计为 100%。

`Tools/Validation/project_completion_audit.ps1` 是唯一的最终百分比判定入口。它不会因为缺少凭据或手机而伪造成功；`-RequireComplete` 在不足 100% 时返回退出码 2。

## 2026-07-29 续作检查点

验证顺序改为 Unity Play Mode 优先：逻辑、UI 状态、动画、语言与下载状态机必须先通过 PlayMode；只有 Android 权限、存储路径、图形驱动、触觉和音频焦点等平台边界才使用现有 APK 验收。源码未变化时不得重复构建，已安装同版 APK 可通过安装脚本的 `-SkipInstall` 复用。

已经取得的 Android 模拟器证据：

- 验收目标为 Android 14 / API 34 的 `Pixel_3a_API_34_extension_level_7_x86_64`，ADB 序列号为 `emulator-5554`；使用独立的 `TestResults/Emulator` 数据盘，不污染默认 AVD。
- 第一份内容按钮候选 x86_64 干净构建成功，APK 大小 53,379,005 bytes，SHA-256 为 `790c8e7f2cb6cd37171509561efc523bab1a27cbcf6c0d7f379dd507eb418377`；Unity 日志明确记录 `Build Finished, Result: Success`、6 个场景与批处理返回码 0，构建日志在 `TestResults/android-emulator-candidate-20260729015231.log`。该候选用于发现点击缩放问题，不能作为最终修复包。
- 第二份“移除按压 scale”候选大小为 53,379,185 bytes，SHA-256 为 `ab9e60a89da8f3f8e7c376f548469ef6025ea47561d6f936b16eaf1acd8b7d4c`；构建日志为 `TestResults/android-emulator-no-scale-candidate-20260729025127.log`。`TestResults/android14-no-scale-loaded.png` 与 `TestResults/android14-no-scale-invalid-pause.png` 证明：即使没有 transform，无效 Pause 仍会让原生 Button 背景消失。因此 scale 只是放大因素，真正不稳定边界是 Android 上的 UI Toolkit 原生 Button 按压渲染路径；该包同样不能作为最终修复包。
- 模拟器包会先构建 Addressables Player Content，并只在 x86_64 软件渲染验收包中临时使用 Built-in Render Pipeline；生产 ARM64 构建仍保留 URP。正常启动日志没有 `Unable to load runtime data`、`No Locales`、`UniversalRenderPipeline..ctor` 或致命异常；AVD 极慢启动造成的系统/应用 ANR 限制另见下文。
- APK 已通过 `adb install -r` 覆盖安装并进入启动页、主菜单和内容管理页；主菜单和纵向内容页的 1080×2220 布局可完整显示。
- 同一个 APK 在 Android Emulator Vulkan/SwiftShader 路径上曾出现 UI Toolkit 完全不绘制但按钮仍可点击、改变窗口尺寸后恢复；不改代码、不重建，仅用 `--es unity -force-gles30` 启动后，首次进入内容页即完整显示，证据为 `TestResults/gles-content-first-attach-2-202607290117.png`。因此该黑屏判定为模拟器图形路径限制，不再通过修改游戏 UI 逻辑规避。Unity 官方也记录过 Vulkan 驱动下 UI Toolkit 黑屏类问题：<https://issuetracker.unity3d.com/issues/ui-toolkit-draws-black-screen-on-adreno-740>。
- Site catalog 在应用中列出 `en.base1` 与 `en.neo1`。`en.base1` 的已安装状态跨覆盖安装保留；`en.neo1.part` 以 10,402,432 bytes 的旧 offset 进入 64% 续传，之后下载至 100% 并显示 `Installed`。这证明远程读取和断点续传主链路可工作，但尚未代替其余十四项收据。
- 模拟器冷启动偶尔出现 `System UI isn't responding`、`Process system isn't responding`，极慢启动时也可能触发应用 ANR；选择 `Wait` 并在系统稳定后重新启动游戏可正常运行。该现象属于当前 AVD 系统层限制，不能表述为游戏通过了实体设备稳定性验证。
- `persistentDataPath` 位于 `/sdcard/Android/data/com.personal.universalgacha/files`。本轮通过 root ADB 放入 `remote-content.json` 后，必须把 owner/group 与 SELinux context 修正为同目录已有文件一致，应用才能读取；正式安装脚本仍需在最终验收时单独复核非 root 流程。

当前阻塞问题（尚未通过）：

- Android 诊断已经排除下载状态机、disabled 与 scale 是单一根因：第一份候选的无效 Pause 没有改变状态却使文字/背景错位；第二份无 scale 候选仍能让背景消失。最新源码因此不再使用任何 UI Toolkit 原生 `Button` 或 `:active`，页头与内容行全部改为永久的 `VisualElement + Label`，由手动 pointer/keyboard 事件、控制器守卫和 `.is-pressed` 颜色/边框反馈处理交互。静态契约会拒绝内容页重新出现原生 Button；合成玩家点击的内容 PlayMode 1/1、定向 EditMode 18/18、完整 PlayMode 7/7、完整 EditMode 235/235 全部通过，证据分别为 `content-stable-actions-pointer-playmode-20260729040500.xml`、`content-stable-actions-editmode-20260729035500.xml`、`full-playmode-stable-actions-20260729041000.xml` 与 `full-editmode-stable-actions-20260729041500.xml`。Android 最终结论仍须留给下一份候选 APK，不能用 PlayMode 冒充设备通过。
- 十四项收据尚未生成；离线重启、网络断开/恢复、存储失败保护、后台/音频焦点、减少动态、内容卸载重装保留收藏和云冲突仍需逐项取得证据。震动、实体扬声器音质与蜂窝切换只能记录模拟器软件证据和硬件限制，不能冒充实体体验。
- 最新源码的完整 EditMode 为 235/235、PlayMode 为 7/7；生产 ARM64 APK 与 `project_completion_audit.ps1 -RequireComplete` 尚未完成，因此当前结论仍是 96%，不是 100%。

工作区与下次执行顺序：

1. 内容页稳定 action control、手指输入、按压反馈、下载状态流与完整回归已经通过；按本主题提交控制器、UXML、USS、测试和本记录，不夹带字体、主题 meta、URP/ProjectSettings 或 Addressables 生成噪声。
2. 只制作一份包含稳定 action control 的新 x86_64 候选 APK；通过 `install_smoke_content.ps1 -ContentMode Remote` 让模拟器用 OpenGLES3 启动，实际点击无效操作、下载、暂停、继续、取消与删除确认，并截图证明文字、背景、热区和状态始终位于正确内容行。
3. 候选 APK 通过后复用同一 APK 和 `-SkipInstall` 补齐可由模拟器证明的 Android 收据；不因重复测试重新构建。
4. 继续实体设备限定收据、生产 ARM64 干净构建、APK 隐私检查和最终 100% 审计；最后再把结果写回本文件和主计划。

## 一、保留最终自动测试证据

测试结果必须写到 `TestResults/`，不能写到 Unity 会在下次启动时清理的 `Temp/`。功能开发先跑定向 PlayMode，再跑完整 PlayMode；不得用一次 APK 构建代替 PlayMode 回归：

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
