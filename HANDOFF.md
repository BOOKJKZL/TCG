# Universal Gacha Simulator 项目交接

> 更新日期：2026-08-08（Asia/Kuala_Lumpur）
> 工作区：`D:\2_GamesCode\!Build_Project\TCG`
> Unity：`6000.0.73f1`
> 分支：`main`
> 本文建立时的源码基线：`7b5bf71230457dc294b765f538773caa65ce272d`

## 1. 这是不是 Handoff

是。本文是新对话的单一交接入口，目的不是取代全部历史文档，而是告诉下一位执行者：

- 游戏最终要变成什么。
- 哪些模块和资料已经存在。
- 当前手机实际暴露了什么问题。
- 哪些旧验收结论已经失效或需要重验。
- 下一步应该按什么顺序修改、验证和提交。
- 哪些工作必须暂停，不能擅自执行。

新对话开始时，应先完整读取本文，再按本文列出的相关文档和源码进行定向调查。不要只根据旧文档中的“96%”或“100%”字样直接继续发布。

## 2. 新对话启动指令

用户可以直接在新对话中发送：

> 请先完整读取项目根目录的 `HANDOFF.md`，再读取 `.agents/skills/vibe-coding/SKILL.md`。按照 Handoff 的“下一次执行顺序”继续开发；每个主题完成并自验证后单独 Git commit，不要混入现有未提交文件，不要同时打开多个 Unity 实例。

如果该次工作涉及 Unity UI Toolkit，可以再读取或引入第 14 节记录的 Unity 官方 `ui-uitk` Skill。

## 3. 事实优先级

当资料互相冲突时，按以下顺序判断：

1. 当前实体手机的干净安装、截图、日志和可重复行为。
2. 当前源码产生的新鲜 EditMode、PlayMode、Android 静态审计和真机证据。
3. 当前 Git 源码。
4. 本文的当前状态和决策。
5. `Docs/` 中的历史阶段记录。

因此，2026-08-08 的手机截图已经证明当前公开 APK 不能继续被视为合格正式包；过去在模拟器、带本机资料的 Editor 或预置内容环境中通过的验收不能覆盖这次干净手机安装失败。

## 4. 产品目标与已经确认的决策

### 4.1 产品定位

- 第一目标是品牌无关的“万能抽卡模拟器系统”。
- 宝可梦卡牌是第二阶段的内容适配器，不应污染通用抽卡核心。
- 这是用户私人兴趣项目，但下载、导入和发布仍应保留来源、版权和服务条款边界；客户端不得绕过第三方网站限制。
- 旧代码能安全升级就升级；若固定假设妨碍目标，可以直接用新系统替换，不要求为了兼容而长期保留错误架构。
- 这是游戏，不是纯资料工具。面向玩家的功能必须同时考虑动画、点击/状态音效、可选震动、减少动态效果、本地化、错误反馈和相应测试。

### 4.2 资料流程

已确认的长期数据流：

```text
TCGdex / Pokémon TCG API / PokéAPI 等公开来源
        ↓
电脑端私人导入器
        ↓
转换资料、压缩卡图、建立身份、生成 Hash 和内容包
        ↓
当前私人 Site 存储
        ↓
手机读取小型远端目录，并按系列/依赖下载所选资源
```

- 手机不直接抓取 TCGdex、PokéAPI 或 GitHub。
- 手机对游戏资料只有公开只读 `GET/HEAD` 权限。
- 发布凭据只留在电脑端 Git 忽略目录；不得进入 APK、公开配置、日志或卡包。
- 当前先继续使用 Site；Cloudflare R2 迁移明确暂停，除非用户以后重新恢复该范围。
- 新增卡包应通过远端目录交付，不应要求重新构建 APK。
- 手机不一次性下载所有 1+ GiB 资料；只读取目录，再下载用户选择的卡包及必要依赖。

### 4.3 语言边界

- 应用界面语言：`en`、`zh`、`ja`。
- 实体卡牌语言：`en`、`ja`、`zh-cn`。
- 应用语言和卡片语言必须完全隔离，不能互相修改、共享偏好或决定可用性。
- 同一跨语言卡牌只有一个已安装版本时，不显示语言切换器。
- 同一跨语言卡牌有两个或三个已安装版本时，详情页允许即时切换卡图和全部印刷资料。
- 不允许用错误语言的卡图伪装为缺失语言版本。

### 4.4 卡牌资料与图鉴规则

- 卡包列表必须可按卡包名称、卡包编号、发行时间、世代排序和筛选。
- 图鉴先按宝可梦世代，再按全国图鉴编号排序。
- 同一物种跨世代出现仍归于同一只宝可梦。
- 阿罗拉等地区形态作为独立图鉴形态显示，但必须与基础形态双向连接并能跳转。
- 图鉴详情包含宝可梦图片、名称/编号/介绍、相关形态，以及所有已收录同名或已关联实体卡牌。
- 卡牌关联必须来自明确资料身份或人工复核，不可只凭名称模糊匹配。

## 5. 当前已经存在的能力

以下是当前源码和历史验收中已经建立的能力。它们需要在本轮手机修复后重新做关键路径回归，但不应无理由重写：

- 通用数据驱动抽卡模型、可替换产品规则、历史规则/来源模拟可信度标记。
- 单包与十连、原子库存事务、历史和统计、存档 v4。
- 收藏页、卡片图片缓存、NEW 状态、跨语言 Printing 切换。
- 远端 Catalog、内容寻址 ZIP、SHA-256、Range、断点续传、安装收据、暂停/恢复/取消、卸载/重装。
- Catalog schema v2、虚拟化内容列表、筛选/排序/批选、空间与网络预检、可恢复下载队列。
- 全世代宝可梦 taxonomy、物种/形态、卡牌关联、图鉴图片和相关卡牌入口。
- 中英日应用 UI、本地化表、应用语言与卡片语言隔离。
- 设置、音效、动画速度、减少动态效果、震动、存档导出/导入、云冲突选择和回滚基础设施。
- 运行时程序集已拆为 Core、Foundation、Utility、Controllers 和独立 Domain/Application/Infrastructure/Presentation 模块。
- Site 已具有只读 Catalog/ZIP、owner-only 电脑发布、最新版 APK 清单和公开 APK 下载。

历史资料规模记录：

- 公网 Catalog：schema v2、revision 6、538 个内容包。
- Catalog 大小：504,816 bytes。
- 内容包总下载量：1,302,001,240 bytes。
- 正式来源卡牌：44,076 条。
- 正式规则 Printing：53,480 条。
- 已接受跨语言组：147 组。
- 图鉴：9 世代、1,025 个物种、1,579 个形态、约 1,571 张图鉴图。
- 仍有来源本身未提供图片的记录；不能把“资料记录已收入”等同于“每张卡图都已取得”。详见第四阶段缺图审计。

## 6. 当前公开 Site 与 APK 状态

### 6.1 Site

- 公网地址：<https://universal-gacha-content.jiejingleek.chatgpt.site>
- Catalog：`/api/content/catalog.json`
- 最新 Android 清单：`/api/releases/android/latest.json`
- Site 当前是资源和 APK 中继，不是完整卡牌浏览站。
- 目前 Site 还不能以网页形式浏览所有卡牌、卡包详情和完整图鉴；这属于后续明确计划，不得误报已完成。

### 6.2 当前公开 APK

- 版本：`0.1.0+1`
- 大小：53,043,913 bytes。
- SHA-256：`782c9c8767f0cb2b48779231ba1549d52a729deeb0782169e673dfd62fa9b3d9`
- 公开下载：`/api/releases/android/782c9c8767f0cb2b48779231ba1549d52a729deeb0782169e673dfd62fa9b3d9.apk`

该 APK **不是合格正式包**：

- `Assets/Editor/AndroidSmokeBuilder.cs` 的 `SmokeBuildOptions` 包含 `BuildOptions.Development`。
- APK 审计曾显示 `application-debuggable`。
- 手机画面右下角可见 `Development Build`。
- Site 发布脚本验证签名、Hash、大小等，但目前没有拒绝 Development/debuggable APK。
- 产物名称仍是 `UniversalGachaSimulator-smoke.apk`，却被发布成公开最新版。

结论：Site 的下载功能本身存在，但公开 Latest 指向测试/开发产物。修复完成前不要再次把该 APK 描述为正式包。

## 7. 2026-08-08 实体手机暴露的问题

### 7.1 P0：干净安装没有内容目录

手机错误：

```text
Collection unavailable: Content directory was not found:
/storage/emulated/0/Android/data/com.personal.universalgacha/files/Content
```

以及：

```text
Pack opening unavailable: Content directory was not found: .../Content
```

根因：

- Android 的内容根目录是 `Application.persistentDataPath/Content`。
- 干净安装时该目录自然不存在。
- `PrivateContentManifestReader` 把目录不存在当成异常。
- 开包和收藏控制器在 Catalog 成功后才完成主要界面、按钮、标签和语言订阅初始化；失败时只剩空页面与错误条。
- 历史 PlayMode 多数使用 Editor 的 `LocalContent/Imports`，没有真实模拟“全新安装、目录不存在、零内容”的路径。

正确行为：

- 目录不存在是正常的首次启动状态，不是内部异常。
- 启动时只建立应用需要的空目录，不自动下载全部资源。
- 首次启动显示语言选择和内容准备引导。
- 玩家可以选择推荐入门包、进入内容管理自行选择，或在离线状态稍后重试。
- 收藏/开包入口在没有可用内容时应显示友好的空状态并跳转内容下载，而不是展示文件路径。

### 7.2 P0：固定 1:2 画面比例造成上下空白

根因：

- `ResolutionManager` 固定 `1000 × 2000`，并修改每个 Camera 的 `rect` 加 letterbox/pillarbox。
- 截图为 `576 × 1280`；固定 1:2 内容区域会产生约 64 px 的上下边条，与照片完全一致。
- `DynamicCanvasScaler` 和相关测试也把 `1000 × 2000` 当成硬契约。

正确行为：

- 不再裁切 Camera 来强制固定比例。
- 游戏使用完整手机屏幕，并通过 UI Toolkit 弹性布局适配常见竖屏比例。
- 所有关键内容进入系统 Safe Area，背景可以延伸到屏幕边缘。
- 需要覆盖窄屏、标准屏、长屏、刘海/挖孔、导航条和字体放大。

### 7.3 P0：错误状态破坏整体 UI

- Catalog 失败发生在按钮配置、产品列表、语言订阅和正常页面状态之前。
- 错误信息直接暴露 Android 私人绝对路径。
- 错误文本很长，没有换行、操作按钮、重试、离线解释或下载入口。
- 第一次启动默认偏向英文，玩家还没选择应用语言就看到英文内部错误。

正确行为：

- 先建立页面骨架、本地化、导航和错误订阅，再加载资料。
- 错误分为“尚未下载”“离线”“目录损坏”“验证失败”“空间不足”等玩家可理解状态。
- UI 不显示内部路径、异常堆栈、URL 凭据或开发术语。
- 每个恢复性错误提供明确下一步：下载、重试、管理内容、查看离线资料或返回首页。

### 7.4 P0：历史验收没有挡住上述回归

- 构建测试只确认 ARM64、CleanBuildCache、权限等，没有禁止 Development/AllowDebugging。
- APK 静态审计没有把 `application-debuggable` 作为正式发布失败条件。
- UI 测试反而固定要求 `1000 × 2000`。
- PlayMode 没有隔离 Editor 本机 `LocalContent/Imports`，导致测试环境天然有资料。
- Site 发布器没有区分 smoke、emulator、candidate 和 stable release。

## 8. UI/UX 重做方向

### 8.1 设计原则

- 这是纵向手机游戏，不应把桌面工具面板缩进手机。
- 可以参考 Pokémon TCG Pocket 的信息层级、单手操作、卡包聚焦、卡片画廊和过渡节奏，但必须使用本项目原创视觉、图标、包装和动效，不能复制其美术、商标或具体页面。
- 采用统一设计 Token：颜色、字号、间距、圆角、描边、阴影替代、动画时间、层级和状态颜色。
- 主要点击目标 Android 至少约 48 dp；关键按钮必须有按压视觉、音效，并按设置提供震动。
- 动画服务于状态转换，不同时让所有元素运动；减少动态效果开启时使用淡入或立即切换。
- 所有页面支持中英日文本长度、换行、字体回退和安全区域。

### 8.2 建议玩家信息架构

```text
首次启动 / Onboarding
  ├─ 应用语言
  ├─ 资料说明与容量
  ├─ 推荐最小内容或进入内容管理
  └─ 完成后进入首页

首页（底部主导航）
  ├─ 开包
  ├─ 收藏
  ├─ 图鉴
  ├─ 内容
  └─ 设置
```

页面职责：

- 首页：最近内容、推荐卡包、下载状态、收藏进度和明确主操作。
- 开包：卡包选择 → 数量选择 → 撕包 → 逐张揭晓 → 批次总结；保持主题动画、音效、稀有揭晓和减少动态效果。
- 收藏：虚拟化卡牌网格、搜索/筛选/排序、拥有数量与 NEW、详情全屏查看、可用时切换实体卡语言。
- 图鉴：世代入口、编号网格、形态连接、详情介绍、相关实体卡牌横向/网格画廊。
- 内容：以世代/语言/卡包组织，不把 538 个包平铺成桌面管理表；支持容量预估、批量选择、队列、暂停/继续、错误恢复和删除确认。
- 设置：可滚动分组，应用语言、声音、动画、震动、下载政策、卡片偏好、存档恢复和账号状态互不挤压。

### 8.3 Android UI Toolkit 已知边界

- 现有内容页曾在 Android 上出现原生 `Button`、`:active` 和 background/border transition 后背景消失的问题。
- 已有稳定方案是 `VisualElement + Label`，根背景保持不变，只对 Label 或独立覆盖层做反馈。
- 新设计不能恢复已知不稳定写法；应先用 PlayMode 和目标 Android 图形路径验证共用按钮组件。

## 9. 优化后的执行路线

每个编号是一个可独立验证和提交的主题。不要把全部改动塞进一个 commit。

### P0-A：引入 Unity UI Toolkit 专项指导

- 安全审查 Unity 官方 `Unity-Technologies/skills` 的 `ui-uitk`。
- 优先作为项目本地、可版本控制的精简 Skill 或参考，不盲目引入整个大型 Skill 集。
- 保留本项目 `vibe-coding` 对工作区保护、自验证和 Git 提交的最高执行约束。
- 补充本项目特有规则：手机 Safe Area、正式 APK、首次零内容、Android 稳定按钮、动画/音效、多语言和真机验证。

建议提交：`chore(skill): add unity ui toolkit workflow`

### P0-B：正式 Android 发布通道

- 把 smoke/emulator 与 release 构建器、文件名和输出路径彻底分开。
- Release 不得包含 `Development`、`AllowDebugging`、`ConnectWithProfiler` 等开发选项。
- 正式包只包含 ARM64，继续验证 target SDK、权限、zipalign、私人内容 0 命中。
- 建立用户私有 Release keystore，凭据和 keystore 不进 Git；记录证书指纹并安全备份。丢失密钥会阻断覆盖升级，必须在首次正式发布前固定。
- 正式构建必须递增 `bundleVersion` 与 `versionCode`。
- APK 审计新增 `debuggable=false/缺失`、无 Development 标记、发行证书指纹、正确 ABI 和禁止测试依赖。

建议提交：`fix(release): separate stable android builds`

### P0-C：Site 拒绝测试包

- Site 的公开 Latest 只接受通过正式审计的 release artifact。
- 不以文件名作为唯一判断；发布前读取 APK manifest/签名/ABI/版本和 debuggable 状态。
- 拒绝 smoke、emulator、Development、debuggable、错误签名、旧 versionCode 或审计报告不匹配的 APK。
- 若以后保留测试通道，必须是独立、非首页、不会覆盖 stable latest 的内部通道。
- 首页清楚显示“正式版”、版本、versionCode、大小、SHA-256、发布日期和更新说明。

建议提交：`fix(site): reject non-release android packages`

### P0-D：移除固定 Camera 比例并加入 Safe Area

- 退役 `ResolutionManager` 对 Camera rect 的固定 1:2 裁切。
- 将 `1000 × 2000` 从硬画面契约降为设计参考，UI 通过 flex、最小/最大约束和可滚动区域适配实际屏幕。
- 建立共享 Safe Area 容器和背景延伸策略。
- 更新错误的固定比例测试，加入多个纵向 aspect 与 inset 测试。

建议提交：`fix(mobile): support full-screen safe-area layouts`

### P0-E：首次启动与零内容状态

- 干净安装自动建立受控目录。
- Catalog reader 对空目录返回结构化“未安装内容”，不抛内部目录异常。
- 新增首次启动语言选择、资源说明、容量和下载路径。
- 首页、收藏、开包和图鉴在零内容时都有正常空状态与内容页跳转。
- Catalog 元数据可以按需要刷新，但不会自动下载全部 ZIP。
- 覆盖首次启动在线、离线、取消、重试、杀进程重启和空间不足。

建议提交：`feat(onboarding): handle clean installs and content setup`

### P0-F：UI 初始化与玩家错误模型

- 页面骨架和本地化先于 Catalog 加载。
- 建立结构化玩家错误码与中英日文本；不把 Exception message 或绝对路径直接呈现给玩家。
- 所有失败页保留导航、重试和内容管理入口。
- 错误出现/关闭有轻量动画和音效，并尊重减少动态效果与静音。

建议提交：`fix(ui): keep navigation available during content failures`

### P1-A：共享手机 UI 系统

- 建立设计 Token、Safe Area、顶部栏、底部导航、按钮、卡片容器、空状态、错误状态、Toast、进度、Sheet 和确认弹窗。
- 优先替换 Presentation；保留已模块化的数据、规则、下载、存档和图片基础设施。
- 对 Android 已知渲染路径建立契约测试。

建议提交：`feat(ui): add mobile game design system`

### P1-B 至 P1-F：逐页重做

按以下顺序，每页单独验证和提交：

1. 首页与底部导航。
2. 内容下载与首次推荐包。
3. 开包选择、动画和结果。
4. 收藏网格、详情与实体卡语言切换。
5. 世代图鉴、形态跳转与相关卡牌。
6. 可滚动设置、存档和账号状态。

建议提交分别使用 `feat(home)`、`feat(content-ui)`、`feat(gacha-ui)`、`feat(collection-ui)`、`feat(pokedex-ui)`、`feat(settings-ui)`。

### P2：流畅度、内存和可访问性

- 目标普通交互 60 FPS；低端设备允许有依据地降级粒子或动画，不降低资料正确性。
- 保持列表虚拟化、图片租约和 48 MiB 解码预算，并在真实手机长时滚动/开包时重新测量。
- 减少布局抖动、重复分配、同步磁盘 IO 和场景切换空帧。
- 覆盖触摸目标、对比度、字体缩放、减少动态、静音、震动关闭和中英日长文本。

建议提交：`perf(mobile): stabilize runtime presentation`

### P3：Site 页面升级

第一部分先完成正式 APK 下载页：

- 手机优先页面、清晰版本信息、正式包标记、Hash/大小、更新说明和下载按钮。
- 不恢复网页上传；电脑发布器继续像小说云端一样直接上传。

第二部分再增加公开只读资料站：

- 卡包目录按语言、名称、编号、时间、世代浏览。
- 卡包详情显示卡牌数量、语言、发行资料和卡牌网格。
- 图鉴按世代/全国编号浏览，地区形态独立且互相连接。
- 宝可梦详情展示图、介绍、形态与相关卡牌。
- 不在每次网页请求中解压 538 个 ZIP；由私人发布流程额外生成确定性的轻量 Web Index 和缩略图/媒体对象。
- Web Index 与游戏 Catalog 共享稳定 ID 和 Hash，但网页展示模型不反向污染游戏领域模型。
- 所有公开 API 只读；owner-only 发布权限保持隔离。

建议拆分提交：

- `feat(site): redesign stable android download page`
- `feat(site): browse published card archive`
- `feat(site): add generation pokedex browser`

### P4：真实资料持续补齐

- 继续通过电脑端私人导入器增量拉取和审计各语言资料。
- 对缺图区分来源未提供、404、区域不对应、人工复核和可合法替代来源。
- 每次新增内容只发布新的不可变对象并最后切换 Catalog；不重建 APK。
- 保持卡包排序、图鉴身份、Printing 身份和跨语言组确定性。

### P5：延后范围

- Cloudflare R2 迁移：暂停。只有 Site 容量、成本、性能或用户明确恢复时才执行。
- Unity Player Accounts 真实邮箱与换机演练：等待外部 Client ID；只允许 `openid/email/offline_access`，不得申请 Gmail/Drive。
- 实体手机硬件补测：震动手感、扬声器音质、蜂窝切换、长时温度与低内存。

## 10. 正式版本完成标准

只有以下条件全部满足，才能把 Site Latest 标为正式包：

- Release APK 非 Development、非 debuggable，使用固定且已备份的私人发行签名。
- ARM64、target SDK、权限、zipalign、签名、私人内容扫描全部通过。
- 版本号和 versionCode 高于当前公开版本。
- 干净手机安装后不需要 ADB 推送本机资料或配置即可进入首次启动流程。
- 没有 Content 目录时显示正常 onboarding/空状态。
- 手机全屏无固定比例上下/左右黑边，关键 UI 尊重 Safe Area。
- 应用语言选择可用；中英日错误和首次启动文本完整。
- Catalog 读取、按需下载、暂停/恢复、离线缓存、安装、开包、收藏、图鉴、删除/重装均有新鲜证据。
- 应用语言与卡片语言仍隔离；多语言卡牌切换规则没有回归。
- 动画、音效、震动开关和减少动态效果有效。
- Site 在切换 latest 前完成整包公开回读与 SHA-256；公开写方法继续全部拒绝。
- Site 下载页显示的版本、Hash、大小与实际 APK 完全一致。

## 11. 自验证顺序

不要每改一点就重建 APK。普通修改遵守：

```text
定向源码/契约检查
→ 定向 EditMode 或 PlayMode
→ 完整 PlayMode
→ 必要的完整 EditMode
→ Unity Console / Missing Script / GUID / meta 审计
→ git diff --check 与差异复核
```

只有触及以下边界才制作候选 APK：

- Android manifest、权限、签名、ABI、IL2CPP/裁剪。
- Safe Area 或真机 UI Toolkit 渲染。
- 持久目录、Storage Access Framework、网络/后台行为。
- 准备正式发布的最终候选。

最终 APK 验证顺序：

1. Clean Release ARM64 build。
2. 静态审计：Development/debuggable、ABI、SDK、权限、签名、zipalign、私人内容、版本。
3. 实体手机先卸载旧版，完成真正干净安装。
4. 首次启动在线/离线/零内容/语言/Safe Area。
5. 按需目录、下载、暂停/恢复、开包、收藏、图鉴、删除/重装和存档保持。
6. 覆盖安装验证固定发行签名和版本升级。
7. 最后才上传 Site；上传后从公开 URL 完整回读并复算 Hash。

每个验收证据必须来自当前源码和当前产物；旧截图、旧 XML、旧日志只能作为历史背景。

## 12. Git 与工作区规则

- 每完成并验证一个主题，立即创建独立 Git commit。
- 只用精确路径暂存本主题文件。
- 不得混入用户已有修改、Unity 自动改动、日志、构建物或附件。
- 不执行 `git reset --hard`、`git checkout --`、amend、rebase、push 或发布，除非用户明确要求对应动作。
- 用户说“继续”时，完成当前切片、验证、提交，再开始下一个切片。
- 不要同时启动多个 Unity 实例。
- 优先使用 Unity PlayMode 验证普通游戏行为；不要为了相同源码重复构建 APK。

本文建立时必须保留、不得顺手提交的已有工作区内容：

```text
 M Assets/Fonts/Universal UI Chinese Fallback SDF.asset
 M Assets/Resources/Gacha/Themes/forest-pack.png.meta
 M Assets/Resources/Gacha/Themes/gallery-pack.png.meta
 M Assets/Resources/Gacha/Themes/ruby-pack.png.meta
 M Assets/Resources/Gacha/Themes/vintage-pack.png.meta
 M Assets/Settings/Mobile_RPAsset.asset
 M Assets/Settings/UniversalRenderPipelineGlobalSettings.asset
 M ProjectSettings/GraphicsSettings.asset
 M ProjectSettings/ProjectSettings.asset
 M ProjectSettings/QualitySettings.asset
?? .codex-remote-attachments/
?? Assets/AddressableAssetsData/Android.meta
?? Assets/AddressableAssetsData/ProfileDataSourceSettings.asset
?? Assets/AddressableAssetsData/ProfileDataSourceSettings.asset.meta
```

新对话必须重新运行 `git status --short`，因为该列表只是 2026-08-08 的交接快照。

## 13. 关键文件入口

### 当前问题

- Android 构建：`Assets/Editor/AndroidSmokeBuilder.cs`
- APK 发布：`Tools/Android/publish_apk_to_site.ps1`
- Site APK 存储：`Cloud/TCGContentSite/lib/releases/`
- Site 下载 UI：`Cloud/TCGContentSite/app/android-release-download.tsx`
- 固定比例：`Assets/Scripts/001_Baisc/004_Screen/ResolutionManager.cs`
- Canvas 缩放：`Assets/Scripts/001_Baisc/004_Screen/DynamicCanvasScaler.cs`
- 应用组合与内容路径：`Assets/Scripts/002_Core/001_Service/GameApplicationBootstrap.cs`
- 内容 reader：`Assets/Scripts/Modules/Gacha.Infrastructure/PrivateContentManifestReader.cs`
- 开包控制器：`Assets/Scripts/004_Controller/GachaViewController.cs`
- 收藏控制器：`Assets/Scripts/004_Controller/CollectionViewController.cs`
- 内容管理 UI：`Assets/UI/ContentManagementView.uxml`、`Assets/UI/Styles.uss`
- 图鉴 UI：`Assets/Resources/UI/PokedexView.uxml`

### 计划与历史证据

- 总计划：`Docs/MASTER_PLAN.zh-CN.md`
- 架构：`Docs/ARCHITECTURE.zh-CN.md`
- 第二阶段卡牌与图鉴：`Docs/PHASE_2_CARD_ARCHIVE_AND_POKEDEX.zh-CN.md`
- 第三阶段 UI/性能/多语言：`Docs/PHASE_3_UI_PERFORMANCE_MULTILINGUAL_CARDS.zh-CN.md`
- 第四阶段资料/内容/运维：`Docs/PHASE_4_DATA_QUALITY_CONTENT_UX_AND_OPERATIONS.zh-CN.md`
- 远端内容：`Docs/REMOTE_CONTENT.zh-CN.md`
- 私人导入器：`Docs/PRIVATE_IMPORTER.zh-CN.md`
- 历史最终验收：`Docs/FINAL_ACCEPTANCE.zh-CN.md`
- Site 边界：`Cloud/TCGContentSite/README.md`

历史文档不应删除；当新验证推翻旧结论时，在历史文档补充勘误或指向本文，不要篡改旧证据使其看起来从未发生。

## 14. Skill 决策

当前项目 Skill：

- `.agents/skills/vibe-coding/SKILL.md`
- 负责规划、实现、风险分级验证、工作区保护、Subagent 使用边界和逐主题 Git commit。

已经调查到的外部候选：

- Unity 官方仓库：<https://github.com/Unity-Technologies/skills>
- 最匹配 Skill：`skills/ui-uitk/SKILL.md`
- 它与 Unity 6、UXML、USS、flex、runtime binding 和自定义元素匹配。

采用原则：

- 推荐使用“现有 `vibe-coding` + Unity 官方 `ui-uitk`”。
- 不让外部 Skill 取代本项目计划。
- 不引入要求大量并行 Agent 的完整“游戏工作室”框架；用户明确要求不要同时开好几个。
- 官方 Skill 现有验证建议偏向让用户查看 Editor，不能取代本项目的自动 PlayMode、Android 静态审计和真机自验证。
- 引入前检查许可证、脚本、外部命令和允许工具；最好将需要的部分项目化并加入本项目特有验证规则。

## 15. 下一次执行顺序

新对话不要重新做大范围架构研究，按以下顺序开始：

1. 重新检查 `git status`、分支、Unity 进程和 Site 当前 latest。
2. 完整读取 `vibe-coding`；审查并引入/项目化 Unity 官方 `ui-uitk`，单独验证和提交。
3. 实现 P0-B：正式 Release 构建与静态审计，先不上传。
4. 实现 P0-C：Site stable gate，确保测试包不能覆盖公开 Latest。
5. 实现 P0-D：移除固定 Camera 比例并建立 Safe Area。
6. 实现 P0-E/P0-F：首次启动、空内容和可靠错误 UI。
7. 使用 PlayMode 和一份 Android 候选完成干净安装验证。
8. 通过后再逐页进行 P1 手机 UI 重做。
9. 最终正式 APK 完成全部验收后，才上传 Site 并替换当前开发包。
10. 手机核心体验稳定后，再做 Site 卡牌档案与图鉴浏览；Cloudflare R2 仍保持暂停。

如果 Release keystore 尚未准备，其他本机 P0 工作可以继续，但不得发布或承诺可覆盖升级的正式 APK；到首次 stable 上传前必须由用户确认 keystore 的保存和备份位置。

## 16. 当前最重要的结论

当前系统的数据、下载、图鉴、多语言、存档和模块边界基础值得保留；问题集中在“发行通道把开发包当正式包”“首次零内容路径没有被设计成玩家流程”“固定比例和现有 Presentation 不适合真实手机”。

下一阶段应重做发行门槛和手机 Presentation，不应重新下载全部资料，也不应先迁移 Cloudflare R2。完成 P0 后，现有 538 包资料才能成为可靠的 UI/玩法验收样本；在此之前继续堆资料或发布 APK 只会放大首次安装问题。
