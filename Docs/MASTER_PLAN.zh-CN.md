# Universal Gacha Simulator 项目主计划

最后更新：2026-07-30

本次修改原因：第一至第三阶段的软件范围均已完成 100% 验收。英文、日文、简中共 524 个 Set 包，连同 taxonomy、三语言卡牌关联与 9 个世代图鉴图片组成 537 个按需资源包并发布到只读 Site；完整 EditMode 344/344、PlayMode 10/10、远端 HEAD/Range 537/537、匿名写入拒绝 8/8、最终 ARM64 APK 和 Android 14 安装收据全部通过。应用语言与卡片语言是完全隔离的状态，缺少卡牌目录时不会阻止玩家切换应用语言。实体震动手感、扬声器音质和真实蜂窝切换继续作为实体机体验补测限制，不影响软件范围结论。验证节奏固定为 Play Mode 优先，只有权限、ABI、存储、图形、触觉等平台边界变化才构建 APK。

本文档是项目实施、验收和后续修改的主要依据。架构细节参考 `ARCHITECTURE.zh-CN.md`，远程资源细节参考 `REMOTE_CONTENT.zh-CN.md`。全量卡牌资料库与宝可梦图鉴请参阅[第二大阶段实施计划](PHASE_2_CARD_ARCHIVE_AND_POKEDEX.zh-CN.md)；正式资料语义、内容体验、存档恢复、R2 与长期运维请参阅[第四阶段实施计划](PHASE_4_DATA_QUALITY_CONTENT_UX_AND_OPERATIONS.zh-CN.md)。

## 一、最终目标

项目分为三个里程碑：

1. 完成可离线游玩的通用抽卡模拟器。
2. 完成按系列和语言下载资源的手机内容系统。
3. 在通用系统上接入私人使用的宝可梦卡牌资料和卡包规则。

核心系统不得写死宝可梦、固定稀有度或固定卡包结构。宝可梦只作为可插拔的数据源和规则适配层。

## 二、游戏完成标准

这是一个游戏，不是只有数据和按钮的管理工具。任何面向玩家的功能必须同时满足以下条件才算完成：

- 功能逻辑可以正常使用。
- 进入、退出、成功、失败和等待状态都有视觉反馈。
- 可点击元素有按下动画与点击音效。
- 重要操作有对应音效、动画或震动反馈。
- 文本进入 Localization，不把显示文案写死在代码中。
- 音量、静音、语言和减少动态效果设置生效。
- 异常、断网和缺失资源有友好的游戏内提示。
- 数据能够正确保存与恢复。
- 有与风险相称的自动化测试或真机验证。

### 必须使用的交互反馈

| 交互 | 动画 | 音效/反馈 |
|---|---|---|
| 普通按钮 | 按压颜色/边框或安全缩放、回弹、禁用状态 | 通用点击音效 |
| 页面切换 | 淡入淡出或滑动 | 页面切换音效 |
| 下载开始/完成 | 进度变化、完成高亮 | 开始、完成、失败音效 |
| 选择系列/卡包 | 卡包抬起、聚焦或发光 | 选择音效 |
| 开启卡包 | 撕包、展开、卡片出场 | 撕包和开包音效，可选震动 |
| 翻开卡牌 | 翻转、缩放、光效 | 翻卡音效 |
| 高稀有卡 | 稀有度专属光效和停顿 | 稀有度专属音效，可选震动 |
| 新卡加入收藏 | New 标记和收藏飞入 | 获得新卡音效 |
| 重复卡 | 数量递增动画 | 较轻的重复卡音效 |
| 错误/断网 | 面板抖动或警示颜色 | 失败音效，不连续轰炸玩家 |

动画和音效应通过统一服务触发，业务控制器不能各自硬编码：

```text
UIFeedbackService
AnimationService
AudioManager
HapticService
AccessibilitySettings
```

## 三、当前状态

已经完成：

- 修复 Dictionary 本地存档。
- 完整云存档快照和冲突处理。
- 修复整包 SR 保底问题。
- 建立目录、库存、随机源、存档冲突和内容下载接口。
- 建立 Addressables 内容下载适配器。
- 建立 TCGdex 私人导入器。
- 下载并校验五个英文历史系列。
- 当前英文私人资料为 218 个 Set、23,444 份卡牌 metadata、21,828 张低清 WebP（345,786,690 bytes）、0 个 Hash 错误；1,616 份来源卡牌记录明确没有图片 URL。
- 当前完整 EditMode 299/299、PlayMode 8/8 全部通过。
- 已建立第一个独立程序集 `Gacha.Presentation`，作为后续模块化迁移基线。
- 已建立统一 `UIFeedbackService`、稳定音效键、震动接口与无障碍偏好。
- 现有 UGUI 按钮会在运行时自动获得按下、悬停和回弹动画，不需要修改场景引用。
- 通用按钮、下载和缺失资源路径仍有低音量程序化后备音；五套年代主题已经由 `AudioClipConfig` 优先加载十个正式原创 WAV，分别覆盖撕包与稀有揭晓，缺失时才回退到原程序化声音。
- 反馈系统、通用领域模型、Application 状态、内容适配器、图片源、WebP 解码、纹理缓存、新抽卡引擎、产品开启、收藏进度、体验设置、版本化资源包 catalog、协调安装/卸载、HTTP 断点下载、远程/离线 catalog、确定性发布器、内容管理 Presentation、产品开包主题和 Pokémon 图鉴均有自动化测试；当前项目 EditMode 测试为 299/299、PlayMode 为 8/8 通过。
- 第二大阶段 Phase 2A 已完成：通用 Set 稳定排序、私人 Manifest v2 与 v1 内存迁移、`PrintingIdentity` 回归保护，以及版本控制的 Set 世代/形态分类 override 均已验证。
- 第二大阶段 Phase 2B 已完成：17 语言只读清单发现、218 个英文 Set 详情、图片 URL 覆盖率与 12 张容量抽样均已记录；没有下载全量卡图或写入远端。
- 第二大阶段 Phase 2C 已完成：218 个英文 Set 全部映射；checkpoint、限速、重试、失败隔离和完成 Set 快速跳过均已验证；23,444 份 metadata 与 21,828 张 WebP 完整性审计失败为 0，运行时可实际解码卡图。
- 第二大阶段 Phase 2D 已完成：218 个最小运行时包连续两次构建 Hash 相同并全部经正式安装器回读；Gen 1 的 11 包 Site pilot 已真实发布，11/11 全包 Hash、11/11 Range 与 8/8 写方法拒绝均通过。
- 第二大阶段 Phase 2E 已完成：电脑端可恢复 PokéAPI 导入器固定 9 世代、1,025 物种、1,351 个具体变体与 1,579 个形态；确定性快照、双向形态链接、Gen 1 #001–#151 与全量资料审计失败 0。
- 第二大阶段 Phase 2F 已完成：23,444/23,444 卡均具有明确物种/形态关联质量状态，关联包可独立安装并由人工 override 重现。
- 第二大阶段 Phase 2G–2I 已完成：中英双语图鉴覆盖 9 世代、地区形态双向跳转、1,571 张按世代下载图片，以及虚拟化的同形态/同物种相关卡片画廊；动画、音效、减少动态效果和有界缓存均已验证。
- 第二大阶段 Phase 2J 已完成：229 包全量发行实际上传，548,304,599 bytes 公网资料通过完整 Hash、HEAD、Range 与只读权限审计；最终 ARM64 APK 与 Android 14 安装完成，统一审计输出 100%。
- 第三阶段已完成：英文 218 Set / 23,444 来源卡、日文 177 Set / 8,159 来源卡、简中 129 Set / 12,473 来源卡已发布；运行时安装结果为 499 个逻辑 Set、43,705 张逻辑卡和 53,480 个实体印刷版本。
- 应用语言与卡片语言使用独立存档、事件与可用性政策；Android 首装无卡牌目录时可把 UI 切换为中文，同时卡片语言保持英文。单语言卡不显示切换器，多语言卡只显示实际存在的实体版本。
- 第三阶段 revision 4 共 537 包，下载 1,301,893,754 bytes、安装 1,356,266,175 bytes；最终 ARM64 APK 为 52,643,378 bytes，完整 EditMode 344/344、PlayMode 10/10 与完成度审计 100% 通过。
- 私人 `manifest.json` 已能在运行时转换为 `UniversalCatalog`，不再只属于编辑器导入流程。
- 本机五个历史系列已验证为 5 个系列、796 个收藏项目和 12 种稀有度；原始 manifest 可展开 1278 个印刷版本，Scarlet & Violet 的运行时 foil 形态补正后 Catalog 共为 1462 个可分别计数的 Printing，原始私人资料保持不变。
- 已建立无 Unity 依赖的 `Gacha.Application`，Controller 通过 `CatalogSession` 使用内容，不再直接构造私人导入读取器。
- UI 与卡牌内容语言已经分离，设置场景提供两个独立选择器、回退提示、持久化、淡入动画和确认音效。
- Android 桌面应用名已接入同一套 Unity Localization，英文为 “Universal Gacha Simulator”、简体中文为“万能抽卡模拟器”；Seeder 会幂等恢复 App Info 元数据和稳定 GUID/ID 引用。
- 私人卡图已经支持异步读取、重复请求合并、32 张 LRU 纹理缓存、加载占位、失败重试和失效请求取消。
- 收藏场景已经可以按系列浏览本机 796 张卡，仅为可见列表项加载图片，并提供双语界面、详情入场动画、翻卡/返回/错误反馈和减少动态效果支持；收藏与共享卡图状态的 28 个玩家文本键已迁入 String Table，运行时 `zh ↔ en` 切换由 PlayMode 验证。
- 收藏场景已接入真实库存数量、持久化 NEW 状态、名称/卡号搜索、稀有度筛选、仅拥有/仅新卡切换和空结果反馈；查看新卡详情会立即保存已查看状态。
- 库存快照已升级为 v3，云端优先读取 `inventory_v3` 并保留 `inventory_v2` 回读；旧 v2 收藏不会在迁移后被误标为新卡。
- 已建立可替换的 `IProductRuleProvider`、规则可信度标记、卡位平均概率摘要和原子库存提交；落盘失败会回滚本次开包。
- `ProductRuleProfile` 已升级为通用证据模型：区分 `Simulated`、`SourceInformedSimulation` 与 `HistoricallyVerified`，记录地区、中英地区名、可信度、HTTPS 来源和核验日期；来源重复时采用最近核验记录，缺少证据的规则不能声明为已验证。
- 抽卡场景已经支持五个系列/产品选择、模拟概率说明、准备卡包、撕包动画、逐张翻卡、新卡/持有数量和结果总结，并立即保存本地库存；开包流程的 29 个玩家文本键已迁入 String Table，选择、待翻卡、翻卡中和结果页均可在运行时切换中英文。
- 英文 Base Set Unlimited 已接入第一份 `HistoricallyVerified` 配列：5 Common、2 Basic Energy、3 Uncommon、1 Rare，Holo 平均约三包一张；Machamp 和 First Edition 已按来源排除。
- 英文 Neo Genesis First Edition 已接入第二份 `HistoricallyVerified` 配列：7 Common、3 Uncommon、1 Rare，Holo 平均约三包一张；只会抽出第一版 Printing。
- 英文 EX Ruby & Sapphire 已接入第三份 `HistoricallyVerified + Corroborated` 配列：5 Common、2 Uncommon、1 Reverse Holo、1 Rare；Rare 槽使用可追溯的整盒经验比例，并明确不声称官方工厂序列。
- 英文 Sword & Shield Base 已接入 `SourceInformedSimulation + Corroborated`：10 张可收藏系列卡、保证 Reverse 槽和六类 Rare 权重均有来源边界；实体 Basic Energy/code card 明确作为非收藏插入物省略。
- 英文 Scarlet & Violet Base 已接入 `SourceInformedSimulation + Corroborated`：4 Common、3 Uncommon、两个独立 foil 槽与一个 Rare-or-higher 槽；官方包结构和超过 8,000 包样本分别限定槽位与权重，Basic Energy/code card 作为非收藏插入物省略。
- 已建立通用 `ProductOpeningTheme` 契约和独立 `Gacha.Pokemon.Presentation` 模块；Base Set、Neo Genesis、EX Ruby & Sapphire、Sword & Shield Base、Scarlet & Violet Base 分别使用 vintage、forest、ruby、electric、gallery 主题，控制配色、撕包节奏、稀有揭晓光环与音效键，并遵守减少动态效果设置。
- 五个年代主题已接入 1024 × 1536 的原创抽象包装图；运行时导入最长边限制为 512px、关闭 mipmap，Android 使用 ASTC 6×6。准备卡包时播放淡入/缩放动画，减少动态效果时直接显示静态包装；缺图则回退到原系列卡图。
- 通用 `ProductOpeningParticleTheme` 和复用式 `ThemeParticleField` 已接入开包环境粒子与稀有卡爆发光效；五套主题分别控制粒子数量、漂移、周期、爆发半径与脉冲，单层最多复用 12 个 UI 元素并以 30 FPS 更新，不进行逐帧对象分配。
- 五个年代主题已接入十个原创烘焙 WAV；`ThemeAudioAssetGenerator` 使用固定种子重建，连续两次生成的 10/10 SHA-256 不变。所有文件为单声道 44.1 kHz，Unity 以 `DecompressOnLoad + ADPCM + preload` 导入，配置资产优先于运行时后备音。
- 设置页已提供静音、减少动态、震动和 0.5x–2.0x 动画速度控件，修改会原子保存并立即预览；保存失败不会发布错误状态。
- 开包页已提供 `Reveal All / 查看全部`，可以取消正在播放的逐张揭晓动画并直接进入完整总结。
- 62.2 KB 的 Noto Sans SC 修改子集已作为全局 TMP 中文回退字体；自动化会同时检查 String Table 与代码内中文，不再出现缺字方框。
- 连续开 500 包/5100 张卡约 0.075 秒，本轮托管内存净增长约 0.063 MiB，低于 32 MiB 验收阈值；三轮核心场景切换净增长约 0.137 MiB；1 万卡存档 JSON 约 464 KB。
- Android/IL2CPP 开发 APK 已成功构建，包名 `com.personal.universalgacha`；smoke builder 现在强制清理旧构建缓存，最新 APK 为 55,027,355 bytes（约 52.48 MiB），ZIP 容器开销为 77,562 bytes。十个正式主题音效相对上一版本增加 174,392 bytes（约 0.17 MiB）；6 个场景、414 个 ZIP 条目中私人内容、`remote-content.json` 和 catalog 缓存匹配均为 0。Gradle 已生成 `values-b+en` / `values-b+zh` 应用名资源，Manifest 使用 `@string/app_name`，`Android App Info` 缺失警告为 0。
- 抽卡、收藏、设置、旧 UGUI 本地化、内容管理、远程 catalog 和场景切换 PlayMode 测试为 7/7 通过。
- 通用 `ContentPackagePlanner` 已能判断新装、更新、Hash 修复、无需操作、空间不足和存储不可用；不会用旧 catalog 降级有效的新版本。
- 本地 `.packages/<package-id>.json` 安装收据读取器已阻止路径逃逸、串包和损坏收据；Android 使用 `StatFs`、编辑器/桌面使用卷信息检查剩余空间。
- ZIP 安装器会先核对下载字节数与 SHA-256，再在实时 Catalog 目录外解压；只有实际解压字节数也匹配后才原子替换系列目录并发布收据。
- 归档路径逃逸、重复路径、损坏归档、取消、未登记目录冲突或收据发布失败都不会覆盖旧内容；回滚也失败时会保留恢复工作区而不是清理唯一副本。
- 下载任务已支持暂停、继续、取消、失败重试、单包并发去重和绝对已落盘字节进度；一次失败尝试只发布一次错误事件。
- `.part` 文件可以跨任务和重启按真实长度继续，达到声明大小后才发布为 `.zip`；本机文件源已经用与未来 HTTP Range 相同的 offset 合同完成验证。
- HTTP 字节源只允许公开 HTTPS 和 loopback HTTP；新下载必须返回精确 `200`，续传必须返回匹配 offset、末字节与总大小的 `206 Content-Range`，忽略 Range、错误长度、压缩编码和截断响应不会制造假完成。
- schema v1 包清单要求每个 archive URL 包含对应 SHA-256；单包协调器已经统一规划、下载、暂停/取消/重试、原子安装和归档清理，玩家界面不需要自行排列基础设施调用。
- 主菜单已加入 `CONTENT` 入口；内容管理页按包显示版本、下载大小、状态与进度，并提供安装、更新、修复、继续、重试、暂停、取消、双击确认卸载和 catalog 刷新操作。
- 内容管理页使用 Unity 主线程桥接协调器事件，支持进入/状态切换/失败抖动动画、按钮按压、下载与卸载反馈、完成震动、减少动态和 42 组中英文本；一次失败尝试只提示一次。
- 远程 catalog provider 已支持 HTTPS、loopback fixture、15 秒默认超时、1 MiB 默认上限、流式二次计数、JSON/identity/200 校验、外部取消和最终重定向 URI；Bootstrap 可从私人文件或 Editor 环境变量配置，不向仓库或 APK写入密钥。
- 电脑端发布器已按稳定文件顺序、固定 ZIP 时间戳和实际字节生成内容寻址归档与 schema catalog；发布后会用正式 Planner/安装器安装到临时目录并由运行时 Catalog 读回，验证完成才算成功。
- Base Set 与 Neo Genesis 已生成两个本机 fixture：下载大小 14,906,006 / 16,437,718 bytes，安装大小 15,189,695 / 16,754,096 bytes；连续构建 3 个发布文件的 Hash 全部不变，输出位于 Git 忽略目录。
- 私人 R2 Editor/Batch 上传器已就绪：凭据仅从进程内输入或环境变量读取，限制 S3 endpoint，拒绝覆盖冲突的不可变 ZIP，先验证 origin 与公开 URL，最后发布 catalog 并生成私人运行配置。
- 独立 `Cloud/TCGContentSite` 已作为阶段 7E 的临时托管适配器：公开 catalog/ZIP 读取与私人 ChatGPT owner 发布完全分离，不使用 D1，不保存 Cloudflare 管理密钥；以后替换成独立 R2 时不改变 Unity catalog schema 或安装收据。
- Site 本机 R2 已用 `en.base1`、`en.neo1` 完成真实端到端验证：14,906,006 / 16,437,718 bytes 的全量 `200`、开放式 Range `206`、非法 Range `416`、Content-Range 和 SHA-256 均正确；错误账号分别返回 `401/403`。
- 远程 catalog 在线验证成功后会原子保存为来源绑定的本地缓存；断网重启仍能列出内容包并显示双语离线警告，来源改变、损坏、超限或链接缓存不会被采用。
- 真实 ZIP/HTTP fixture 已验证下载截断后销毁旧协调器，重建协调器会从 `.part` 实际长度发送精确 Range、完成安装并清理下载缓存。
- `project_completion_audit.ps1` 已把最终 EditMode/PlayMode、APK 新鲜度/隐私边界/纯 ARM64 ABI/权限/签名/对齐、正式音频、本机发布包 Hash、远程运行配置、ADB 状态和十四项设备软件收据统一成一个 100% 判定；静态契约自测 4/4 与 `-RequireComplete` 均已通过。

非阻塞的后续改进：

- 已接入系列的来源模拟仍可在取得更权威资料后版本化精炼；未知产品继续明确使用等概率模拟规则。
- 第二大阶段全量卡图达到 Site 容量或成本门槛后迁移至独立 Cloudflare R2。
- 在实体 Android 上补测震动手感、扬声器音质和真实蜂窝切换；这不改写已经完成的模拟器软件结论。

按验收条件而不是代码数量估算，第一大阶段的技术底座、公开 Site HTTPS 发布、Android 14 软件验收与生产 ARM64 产物均已完成，`project_completion_audit.ps1 -RequireComplete` 验证为 100%。迁移到独立 Cloudflare R2 属于第二大阶段的容量/成本优化，不阻塞当前 Site 版本结论。

## 四、阶段计划

### 阶段 0：工程与模块整理

状态：基础切片已完成（2026-07-16）；Application Catalog 边界已于 2026-07-23 补齐，后续模块会在迁移时逐步增加各自 asmdef。

目标：建立稳定的开发基线。

工作内容：

- 保护现有未提交场景和 ProjectSettings 修改。
- 整理 Domain、Application、Infrastructure、Presentation、Editor 目录。
- 增加 asmdef 并限制错误依赖方向。
- 保留 Unity `.meta`，避免场景引用丢失。
- 建立统一的游戏反馈接口和音效键规范。
- 固定编译、测试和 Missing Script 检查。

验收：

- Unity 编译无错误。
- 场景没有 Missing Script。
- 当前测试全部通过。
- UI 控制器不直接依赖 Cloud Save、R2 或 TCGdex。

预计：1–2 小时。

实施记录：

- 新增 `Gacha.Presentation.asmdef`，旧代码通过接口接入，不让 Presentation 依赖旧管理器。
- `AudioManager` 作为音频输出适配器注册到统一反馈服务，并修复空配置、缺失 Key 与无效音源索引导致的异常或无反馈。
- `UIFeedbackAutoInstaller` 会为所有已加载场景中的 UGUI `Button` 自动添加 `GameFeedbackButton`。
- 设置数据已支持 `reduceMotion`、`hapticsEnabled` 与 `uiAnimationSpeed`，接口已可供设置页绑定。
- Unity 6000.0.73f1 编译成功；EditMode 7/7 通过；场景和 Prefab 文本未发现 `m_Script: {fileID: 0}`。
- 阶段 5C 已补齐静音、减少动态、震动和动画速度控件；五套年代开包音效已由正式原创 WAV 覆盖，其他通用反馈仍保留可替换的低音量程序化后备声。
- `GachaViewController` 已通过 Application `CatalogSession` 使用内容，不再直接构造私人内容读取器；阶段 3 的 Catalog 边界问题已经关闭。

### 阶段 1：通用数据模型

状态：领域模型完成（2026-07-18）；旧固定枚举系统已直接退役，Inspector 编辑体验待继续。

目标：移除固定 `C/R/SR/UR` 和宝可梦专用假设。

模型：

```text
GameDefinition
SetDefinition
CollectibleItemDefinition
PrintingDefinition
RarityDefinition
ProductDefinition
VariantDefinition
LanguageDefinition
```

印刷版本身份：

```text
Game + Set + CardNumber + Language + Variant
```

验收：

- 能表示当前导入资料中的 12 种稀有度。
- 不同语言和卡面版本分别计数。
- 旧测试卡和固定稀有度资产已移除，不再限制新模型。
- Inspector 有清晰分组、校验提示和错误图标。

预计：4–6 小时。

实施记录：

- 新增无 Unity 引擎依赖的 `Gacha.Domain.asmdef`，领域层可独立测试。
- 已建立 `GameDefinition`、`SetDefinition`、`CollectibleItemDefinition`、`PrintingDefinition`、`RarityDefinition`、`ProductDefinition`、`VariantDefinition` 与 `LanguageDefinition`。
- 稀有度、变体、语言和产品类型全部使用数据 ID，不再需要为新游戏或新年代修改 enum。
- `PrintingIdentity` 使用 `Game + Set + CardNumber + Language + Variant`，语言和卡面版本会分别计数。
- `UniversalCatalog` 会检查重复 ID、缺失引用、跨游戏引用与重复印刷身份。
- 以五个已下载系列实际出现的 12 种稀有度建立测试。
- 根据项目决策，不再维护 `LegacyCatalogAdapter`；`Card`、`CardDatabase`、`PackDefinition`、旧 `Rarity` enum、`GachaService` 和示例 Resources 资产均已退役。
- 存档与云同步属于可升级的外围能力，继续保存新的 Printing ID 和 Product ID。
- Unity EditMode 当前 16/16 通过。

### 阶段 2：通用抽卡规则

状态：通用规则引擎、可替换规则提供器、概率摘要和单包 UI 已完成；英文 Base Set Unlimited、Neo Genesis First Edition 与 EX Ruby & Sapphire 为三份历史规则，Sword & Shield Base 与 Scarlet & Violet Base 为两份来源指导模拟（2026-07-25）。

目标：让抽卡引擎适用于不同游戏、产品和年代。

规则：

- `SlotRule`：每个卡槽从哪个池抽取。
- `WeightedPool`：概率权重。
- `GuaranteeRule`：保底和每包最低稀有度。
- `VariantRule`：普通、闪、反向闪和其他卡面。
- `CollationRule`：真实配列扩展点。
- `IGachaRandom`：可重现的随机源。

表现要求：

- 抽取结果包含可供动画使用的 reveal 顺序和稀有度提示。
- 规则只产出事件，不直接播放动画和声音。
- Presentation 根据事件决定翻卡、停顿、特效和音效。

验收：

- 多卡槽、保底、变体和空卡池测试通过。
- 相同 Seed 产生相同结果。
- 保底只影响需要调整的卡槽。

预计：4–7 小时。

实施记录：

- 新增 `WeightedPool`、`SlotRule`、`GuaranteeRule`、`ProductDrawRules` 和 `GachaEngine`。
- 权重直接引用 Printing ID，不依赖任何固定稀有度枚举。
- 多卡槽可独立设置数量、权重池、揭示顺序和是否允许重复。
- 保底规则按 Product 开启次数触发，只替换达到最低数量所需的非合格卡槽。
- 相同 Seed 会得到相同抽取结果；空池、错误引用和无法满足去重时会给出明确错误。
- `GachaViewController` 已直接读取私人 Catalog 并调用新引擎；库存记录 Printing ID 和 Product ID。
- `SimulatedProductRuleFactory` 只提供明确标记的均匀模拟卡包；真实 Pokémon 配列不会伪装成已验证概率。
- `IProductRuleProvider` 返回完整 `ProductRuleProfile`；当前 `UniformSimulationRuleProvider` 明确使用 `Simulated + Unverified + unspecified`，有资料但仍需模拟的未来规则可使用 `SourceInformedSimulation`，只有具备核验资料与可信度的配置才能声明为 `HistoricallyVerified`。
- `ProductOddsAnalyzer` 汇总多卡槽权重池的平均卡位概率；条件保底会单独标记，不与基础概率混淆。
- 模拟卡池只包含当前 Content Language 的印刷版本，不会把不同语言卡面混入同一包。
- `PokemonHistoricalRuleProvider` 为英文 Base Set Unlimited 提供 11 卡槽经验规则；规则来源、Machamp 例外和不能推断的工厂序列记录在 `RULE_SOURCES.zh-CN.md`。
- Base Set Profile 只选择非 First Edition Printing；Rare 池排除 Starter Deck 专属 Machamp，Holo/非 Holo 总权重维持样本记录的约 1:3 比例。
- Neo Genesis Profile 只选择 First Edition Printing，按来源使用 7 Common、3 Uncommon、1 Rare，并以类别权重维持 Holo 总概率约 1:3。
- `ProductRuleProfile` 可携带中英规则说明和地区名，并为每个 HTTPS 证据保存标题与核验日期；开包界面会直接显示版本、槽位、可信度、核验日期和全部来源入口，不需要通过宝可梦专用 UI 判断 Profile。
- Neo 来源只说明 7 张 Common，未说明独立能量槽，因此当前 Common 池包含基础能量但不保证每包能量；Unlimited 与精确印刷序列继续保持未验证。
- 已知限制：引擎具备 `GuaranteeRule`，但均匀模拟配置没有历史保底、变体或真实配列；不能把“引擎支持保底”描述成“当前运行时卡包已配置保底”。
- 下一条设备切片：部署公开 Site、生成已验证的 HTTPS 运行配置，再连接 Android 真机运行现有 APK 与私人配置推送脚本。

### 阶段 3：双层语言系统

状态：已完成（2026-07-25）。Application 语言核心、回退、持久化、所有现有场景静态文本、运行时切换、中文字体和 Android 桌面应用名均已通过自动化验证。

目标：区分应用界面语言与卡牌内容语言。

```text
UI Language       菜单、按钮、提示
Content Language  卡名、卡图、系列和产品
```

工作内容：

- `LanguageService` 控制 Unity Localization。
- `ContentLanguageService` 控制卡牌内容。
- 回退顺序，例如 `zh-CN → zh-TW → en`。
- 设置页面分别选择两种语言。
- 保存语言偏好。
- 语言切换动画避免界面突然跳变。
- 下拉菜单、确认和切换有声音反馈。

验收：

- 中文 UI 可以显示英文或日文卡牌。
- 切换 UI 语言不改变收藏。
- 缺少语言时正确回退并提示。
- 重启后恢复设置。

预计：3–5 小时。

实施记录：

- `LanguageSelectionService` 分别保存 UI Language 与 Content Language，切换界面语言不会改变内容选择或收藏身份。
- 内容语言支持父语言、`zh-CN → zh-TW` 区域回退、语言定义回退和英文最终回退。
- `GameApplicationBootstrap` 使用 PlayerPrefs 恢复语言，并把 UI Language 应用到 Unity Localization。
- 设置场景会运行时安装独立双语言面板；按钮使用统一按下动画和确认音效，切换时遵守减少动态效果设置。
- `Card_UI` 中英文 String Table 已加入语言设置文本；缺少内容语言时会显示当前回退结果。
- `Card_UI` 新增稳定的 `app.display_name`，Localization Settings 配置 Android App Info；实际 Android 构建已验证两种 `strings.xml` 与 Manifest 标签，并消除未配置元数据警告。
- `CardUiText` 在 Presentation 层统一从 `Card_UI` 读取并按 locale 缓存文本，缺表或初始化异常时使用英文兜底；收藏控制器、`AsyncCardImageView` 和 `GachaViewController` 不再保存中英成对文案。
- 收藏/卡图新增 28 个中英文键；实际执行 Seeder 后，表完整性、中文字体和内容管理回归均通过，重复 Seed 不再覆盖卸载或离线 catalog 后续文案。
- 开包流程新增 29 个中英文键；PlayMode 在同一次真实 11 张开包中验证选择页、待翻卡状态和结果页的 `zh ↔ en` 即时切换，同时继续验证历史规则、揭示顺序、`Reveal All`、音效与震动提示事件。
- `LegacySceneTextLocalizer` 以场景级映射覆盖主菜单四个入口及设置、开包、收藏三个旧 UGUI 标题，不改动旧场景序列化引用；动态面板中的空 TMP 文本会被安全忽略。新增 5 个菜单/设置键，开始场景没有静态文字，关闭按钮 `X` 不需要翻译。
- 截至 2026-07-24，全量 EditMode 60/60 与 PlayMode 4/4 通过，其中包含设置场景强制切换中文和零缺字日志回归。
- 截至本次补强，全项目 EditMode 229/229、PlayMode 7/7 通过；Android/IL2CPP 干净 APK 为 55,027,355 bytes（约 52.48 MiB）、414 个条目且私人内容名称匹配为 0。
- 已加入 62.2 KB、227 个 UI codepoint 的可重建 Noto Sans SC 修改子集作为 TMP 全局回退，并检查当前 String Table 与代码内全部中文字符；新增中文后必须继续重新生成子集。

### 阶段 4：接入私人导入内容

状态：已完成（2026-07-23）。Catalog 转换、安全图片源、纹理缓存和系列/卡牌浏览纵向切片均已通过自动化验证。

目标：让运行时使用已经导入的五个历史系列。

工作内容：

- 实现 `ImportedCardCatalog`。
- 读取并校验 `manifest.json`。
- 将数据转换成通用模型。
- JPG 异步加载和内存缓存。
- 缺图占位、加载动画和失败重试。
- 系列浏览页显示封面、年代、数量和语言。

验收：

- 读取全部 796 张卡。
- 卡号、稀有度、语言和变体正确。
- 滚动列表不会一次加载所有高清图。
- 图片加载有占位动画，失败不会卡住 UI。

预计：3–5 小时。

实施记录：

- 新增 `Gacha.Infrastructure.asmdef`，只向内依赖 `Gacha.Domain`。
- `PrivateContentManifestReader` 会递归读取已安装内容并检查 Schema、语言和系列基本字段。
- `PrivateManifestCatalogAdapter` 会聚合多语言名称、系列、卡牌、稀有度、变体、印刷版本和模拟卡包产品。
- 普通、反向闪、闪卡与第一版等标记会展开成独立 `PrintingIdentity`，收藏可以分别计数。
- 本机原始资料集成验证：5 个 manifest、796 张来源卡、12 种稀有度、1278 个印刷版本、0 个导入警告，所有图片路径存在；宝可梦适配策略在运行时另将 sv01 的实体 foil 形态补正为总 Catalog 1462 个 Printing，不回写来源文件。
- `PrivateContentImageSource` 只允许读取安装目录内的相对路径，异步返回图片字节并验证 manifest SHA-256；缺图、越界路径和损坏文件会返回可区分状态。
- `CardTextureCache` 会合并相同卡图的并发请求，以容量 32 的 LRU 释放淘汰纹理；调用方取消不会误杀其他共享请求。
- `AsyncCardImageView` 提供加载脉冲、双语失败提示和手动重试，虚拟化列表复用行时会取消并忽略过期结果，错误音效不会连续轰炸玩家。
- 收藏场景使用固定高度虚拟化 `ListView` 浏览系列和卡牌，系列行显示封面、年代、数量和内容语言；只有可见行会请求图片。
- 卡牌详情提供图片、卡号、稀有度、变体和语言，并以遵守动画速度与减少动态效果设置的淡入缩放演出显示；选择、翻卡、返回和重试均接入统一反馈。
- 自动化验收：EditMode 36/36、PlayMode 2/2；运行时验证 5 个系列，缓存数量不超过 32 且小于当前系列卡牌总量。
- 已知限制：TCGdex variant 布尔值不能完整表达所有真实印刷组合；当前展开结果是可用的模拟模型，后续可由更精确的数据覆盖。

### 阶段 5：本地游戏闭环

状态：存档、统一反馈、已安装内容浏览、三套历史规则、两套来源模拟、通用模拟后备、单包闭环、收藏库存和阶段 5C 自动化均已完成；Android/IL2CPP 构建冒烟通过，真机设备验收待连接手机执行。

目标：完成第一版真正可玩的离线模拟器。

流程：

```text
选择游戏
→ 选择内容语言
→ 选择系列
→ 选择卡包
→ 查看概率
→ 撕包动画
→ 逐张翻卡
→ 稀有卡演出
→ 结果总结
→ 加入收藏
→ 保存与恢复
```

工作内容：

- 系列与产品选择页面。
- 开包场景和可跳过动画。
- 卡牌翻转、稀有度光效、音效和震动。
- 新卡、重复卡和数量反馈。
- 收藏网格、详情、搜索和筛选。
- 所有按钮接入统一点击反馈。
- 设置中提供动画速度、减少动态效果和震动开关。

采用三个可独立验收的纵向切片：

#### 阶段 5A：单卡包可玩切片

- 只选择现有五个系列中的一个系列和一个产品。
- 完成语言、系列、产品、概率、开包、逐张揭示、结果总结和保存。
- 未经史料验证的规则必须在界面标记为“模拟规则”。
- 所有等待、成功、失败和缺图路径都有视觉与声音反馈。
- 完成记录（2026-07-23）：五个已安装产品可选择；界面明确显示模拟规则和按稀有度汇总的平均概率；撕包、逐张翻卡、结果总结、卡图加载、声音、震动、新卡标记和本地保存均已接通。
- 自动化验收：全量 EditMode 45/45、PlayMode 3/3；开包流程会先完整验证 Base Set，再切换到 Neo Genesis 并确认 11 张结果全部为第一版 Printing。
- 已完成 Base Set Unlimited、Neo Genesis First Edition 与 EX Ruby & Sapphire 三份历史 Profile，以及 Sword & Shield Base、Scarlet & Violet Base 两份来源指导模拟；当前五个本机系列均有各自 Profile，通用纯模拟仍用于未来未知内容。

#### 阶段 5B：收藏体验

- 收藏网格、详情、搜索、筛选、新卡和重复数量反馈。
- 图片按需加载，不因浏览收藏一次载入所有高清卡图。
- 完成记录（2026-07-24）：虚拟化收藏列表已显示每个 Printing 的拥有数量和 NEW 标记；系列行显示已收藏/总数与新卡数量。
- 支持名称/卡号即时搜索、稀有度下拉筛选、仅拥有、仅新卡和一键清除；无结果时显示双语空状态并使用减少动态兼容的淡入反馈。
- 打开 NEW 卡牌详情时会通过 `ICollectionProgressStore` 原子保存已查看状态；保存失败会回滚并保留 NEW，同时播放错误反馈。
- 库存快照 v3 保存未查看 Printing，旧 v2 快照迁移为“已有卡视为已查看”，避免一次性产生大量伪 NEW。
- 自动化验收：全量 EditMode 48/48、PlayMode 3/3；收藏场景覆盖库存/NEW、搜索、稀有度筛选、空结果、已查看和保存失败路径，纹理缓存仍不超过 32。

#### 阶段 5C：完整性与性能

- 设置控件、可跳过动画、减少动态效果和静音行为。
- 连续开包、场景切换和大型收藏的内存与帧率检查。
- 增加 PlayMode 测试，并完成第一次 Android 本地内容冒烟测试。

完成记录（2026-07-24）：

- Application 层 `ExperienceSettingsService` 管理静音、减少动态、震动和动画速度，PlayerPrefs 适配器负责移动端持久化；保存失败保持旧状态且不发布变更。
- 设置场景新增四个双语控件、即时预览、保存状态、按下动画、确认音效和启用震动时的单次预览。
- 开包场景新增 `Reveal All / 查看全部`，跳过剩余逐张揭晓动画后直接生成完整结果列表，不重复播放每张卡的声音或震动。
- `ProductOpeningService` 缓存已经验证的规则 Profile；当前真实五系列连续开 500 包/5100 张卡约 0.075 秒，本轮托管内存净增长约 0.063 MiB，低于 32 MiB 阈值。
- 1 万卡库存快照往返约 0.027 秒、JSON 约 464 KB；256 张卡图压力测试始终保持 32 张 LRU 上限。
- 三轮开包/收藏/设置场景切换约 4.4 秒，热身后托管内存净增长约 0.137 MiB，旧场景 Controller 不会残留。
- 中文 UI 当前使用 62.2 KB 修改字体子集，EditMode 会检查 String Table 与代码中文，PlayMode 强制切换中文并拒绝缺字日志。
- 全量 EditMode 60/60、PlayMode 4/4 通过；Android/IL2CPP 构建成功，APK 约 51.6 MiB，包内私人内容条目为 0。
- 已提供 `Tools/Android/install_smoke_content.ps1`，但当前没有连接 Android 设备，因此触摸、真实震动、扬声器声音和 `persistentDataPath/Content` 读取仍需真机验收。

验收：

- 开包、展示、收藏和重启恢复完整可用。
- 静音时不播放任何点击或演出音效。
- 减少动态效果开启后缩短或替换强烈动画。
- 连续开包不会产生明显内存增长。

预计：8–14 小时。

阶段 5 的代码与自动化已达到本地通用抽卡模拟器 MVP；真机设备验收通过后正式关闭该阶段。

### 阶段 6：内容管理系统

状态：6A 安装前决策与本地状态适配层、6B 原子归档安装事务、6C1 下载状态与文件断点缓存、6C2 HTTP Range 传输、6C3 catalog 与协调安装闭环、6C4 玩家内容管理页面、6C5 安全卸载与同页重装均已完成（2026-07-24）。

目标：在游戏内安装、更新和删除系列。

工作内容：

- 可下载内容列表。
- 下载大小与剩余空间检查。
- 下载、暂停、取消、重试和删除。
- 版本和 Hash 校验。
- 下载状态动画、进度条和音效。
- 下载完成后使用游戏内通知，不阻断玩家操作。

6A 完成记录（2026-07-24）：

- 新增无 Unity 依赖的 `ContentPackagePlanner`，统一返回 Install、Update、Repair 或 None，不让 UI 自己比较版本与 Hash。
- 安装前空间按“下载归档 + 完整解压临时副本 + 32 MiB 安全余量”计算，确保更新期间旧内容保持可用；超大数值使用饱和加法避免溢出。
- 远程包元数据会在读取本地状态前校验包 ID、单调 Revision、版本、下载/安装大小和 64 位十六进制 SHA-256。
- `.packages/<package-id>.json` 收据只保存内容安装状态，不接触收藏存档；缺少收据视为未安装，损坏、串包或路径型 ID 会被拒绝。
- Android 剩余空间通过 `StatFs` 读取，编辑器和桌面读取目标卷；探测不存在的 Content 目录时不会提前创建目录。
- `GameApplicationBootstrap` 已把服务接入 `ApplicationServices.ContentPackages`，后续下载管理界面只依赖 Application 层。
- 定向测试 23/23、全量 EditMode 78/78、PlayMode 4/4 通过；Missing Script、重复 GUID 和 Domain/Application 分层越界均为 0。
- 本切片属于后台基础设施，不宣称已完成玩家体验；下载进度动画、点击/完成/失败音效、震动、本地化提示和错误防抖会随阶段 6 UI 一起验收。

6B 完成记录（2026-07-24）：

- `ContentPackageDescriptor` 与安装收据新增显式 `InstallRelativePath`，不再从 Package ID 猜测目录；绝对路径、`..`、空段、内部状态目录和非便携文件名会在访问磁盘前被拒绝。
- `IContentPackageInstaller` 返回结构化的成功、无效计划、缺少归档、完整性失败、无效归档、取消、普通失败和回滚失败状态。
- `FileSystemContentPackageInstaller` 核对 ZIP 长度与 SHA-256，在 Content 同卷但实时 Catalog 之外的 staging 解压，并核对每个 Entry 与整包实际解压字节数。
- ZIP Entry 会阻止 rooted path、Zip Slip、重复大小写路径、控制字符和 Windows/Android 不可移植名称；下载归档由下载器拥有，安装成功后不会擅自删除。
- 提交时先把旧系列目录移动到事务回滚区，再移动 staging、备份旧收据并发布新收据；任何提交异常都会恢复旧目录和旧收据。
- 若回滚本身失败，结果会返回 `RollbackFailed` 和恢复工作区位置，最终清理不会删除旧内容的唯一副本。
- 新安装不能覆盖没有收据的同名目录，已安装包也不能在没有显式迁移的情况下偷偷改变安装路径。
- 安装器定向测试 10/10、内容包模块测试 33/33、运行时接线 6/6、全量 EditMode 94/94、PlayMode 4/4 通过；Missing Script、重复 GUID 和 Domain/Application 分层越界均为 0。
- 新事务代码通过 Android/IL2CPP smoke build：5 个场景，构建约 4 分 51 秒，APK 74.7 MiB，私人 `LocalContent`/五个历史系列/manifest 文件名匹配为 0；当前 APK 最大压缩条目是 `libil2cpp.so`、`libunity.so` 和 `global-metadata.dat`，包体增长不能归因于卡图。
- 本切片仍是后台基础设施；下载速度/进度、状态动画、点击与完成/失败音效、震动、本地化和错误防抖尚未作为玩家体验完成。

6C1 完成记录（2026-07-24）：

- 新增无 Unity 依赖的 `ContentPackageDownloadTask`，以 Idle、Downloading、Paused、Completed、Cancelled 和 Failed 表达单个包的完整生命周期。
- `IContentPackageTransfer` 只暴露已落盘绝对字节、offset 下载、删除临时数据和完成归档路径；Application 不知道 HTTP、R2、文件目录或 UnityWebRequest。
- 重复 Start 会复用同一任务；Pause 保留 `.part`，Cancel 删除临时归档，Retry 从真实文件长度继续并增加 Attempt。
- 每个失败 Attempt 只发布一次 `FailureReported`，暂停/取消不是错误；UI 事件订阅者即使抛异常也不会污染下载结果，后续失败音效可按事件播放而不轰炸玩家。
- `FileSystemContentPackageTransfer` 使用 `<package-id>.part` 保存实际写入字节，达到声明大小后原子重命名为 `.zip`；完整 part 在重启后无需重新访问来源即可发布。
- 本机文件字节源实现同一 offset 合同；模拟连接在 30 字节中断后，Retry 从 30 而非 0 继续并恢复完整内容。
- offset 与文件长度不一致、来源超过声明大小、来源提前结束和删除失败都会返回结构化 Failed，不能制造稀疏文件或假完成。
- 状态机定向测试 9/9、文件传输测试 7/7、全量 EditMode 110/110、PlayMode 4/4 通过；Missing Script、重复 GUID 和 Domain/Application 分层越界均为 0。
- 本切片未新增玩家 UI，因此动画、进度条、点击/暂停/完成/失败音效、震动、本地化提示和主线程事件派发尚未完成。

6C2 完成记录（2026-07-24）：

- 新增 `IContentPackageUriResolver` 与 `HttpContentPackageByteSource`；Application 只要求“包对应哪个 URI”，不会知道 R2、域名或 catalog 格式。
- 公开内容只允许 HTTPS；HTTP 仅允许 loopback fixture，URI 中的用户凭证和 fragment 会在请求前拒绝，最终响应 URI 也会再次校验。
- 新下载只接受无 `Content-Range` 的 `200 OK`；续传只接受 `206 Partial Content`，并严格匹配起点、末字节、整包大小和可用的 `Content-Length`。
- 请求固定使用 `Accept-Encoding: identity`，响应若使用 gzip 等内容编码会失败，避免 wire byte offset 与落盘字节不一致。
- 服务器忽略 Range 返回 `200`、三类错误 `Content-Range` 和错误长度时会在打开响应体前失败，不会追加到旧 `.part`。
- 截断响应只保留实际写入字节；测试中的 40 字节 partial 收到 20 字节后失败，Retry 会发送 `bytes=60-` 并恢复完整归档。
- 阻塞请求可由 Pause 取消且不发布失败事件；错误尝试仍只发一次结构化错误。
- HTTP 定向测试 12/12、全量 EditMode 122/122、PlayMode 4/4 通过；Android/IL2CPP 构建成功，5 个场景，约 5 分 34 秒，APK 74.8 MiB，413 个条目中私人内容名称匹配为 0。
- 当前没有持久化 ETag/`If-Range`；正式 catalog 必须先提供不可变、带版本的对象 URL，避免同一路径在续传期间被替换。整包 SHA-256 仍会在安装前进行最终完整性校验。
- 本切片仍未新增玩家 UI；下载进度动画、点击/暂停/完成/失败音效、震动、本地化提示、错误防抖和主线程事件派发继续由下一阶段验收。

6C3 完成记录（2026-07-24）：

- 新增 schema v1 `ContentPackageCatalog`、结构化加载结果和 provider 边界；清单 revision 必须为正，包 ID 必须唯一，所有 descriptor 在网络访问前复用 planner 规则验证。
- archive URL 只允许 HTTPS 或 loopback HTTP，并必须在路径中包含包的完整 SHA-256；相对 URL 会以 catalog URI 为基准解析，从协议层阻止 `latest.zip` 被覆盖后污染断点续传。
- Catalog 同时实现 `IContentPackageUriResolver`，解析时会核对 Package ID、Revision、路径、版本、大小和 Hash，旧描述符不能只凭相同 ID 获取新地址。
- 新增 `ContentPackageInstallCoordinator`，以 Planning、Blocked、Downloading、Paused、Installing、Succeeded、AlreadyCurrent、Cancelled 和 Failed 统一整个单包生命周期。
- 协调器会顺序执行 planner、断点下载与原子安装；成功后清理下载归档，清理失败只作为安装成功后的警告，不会谎报安装失败。
- 安装器若报告 ArchiveNotFound、IntegrityMismatch 或 InvalidArchive，协调器会 `DiscardAsync` 损坏归档并在 Retry 时重新下载；普通磁盘类安装失败则保留已验证归档，避免无意义重下。
- 真实端到端 fixture 已使用版本化 JSON catalog、进程内 HTTP、实际 ZIP、`.part/.zip`、SHA-256、staging、目录原子替换和收据验证完整闭环；损坏更新不会触碰 revision 1，重下正确包后才发布 revision 2。
- `HttpContentPackageInstallCoordinatorFactory` 会按 Package ID/版本缓存操作；切换 catalog revision 前要求旧操作取消，避免两个版本争用同一下载路径。
- Bootstrap 已把共享 HTTP 工厂和独立 `ContentDownloads` 目录接入 `ApplicationServices.ContentPackageOperations`；Reset 会释放工厂，后续 UI 不需要直接操作 planner、传输层或 installer。
- Catalog 定向测试 10/10、协调器测试 11/11、端到端测试 2/2、工厂测试 3/3、全量 EditMode 148/148、PlayMode 4/4 通过。
- Android/IL2CPP 构建成功，5 个场景，约 2 分 21 秒，APK 74.8 MiB；413 个 APK 条目中私人内容名称匹配为 0。
- 6C3 完成时仅有本机 JSON catalog provider，尚未决定正式 R2 catalog URL/读取鉴权，协调器事件也未切回 Unity 主线程；主线程桥已在 6C4 解决，远程 provider 继续由阶段 7 完成。

6C4 完成记录（2026-07-24）：

- 新增第 6 个可构建场景 `006_ContentScene` 与主菜单 `CONTENT` 入口；场景 Seeder 会自验证 UXML 引用、幂等更新入口并维护 Build Settings。
- `ContentManagementController` 只消费 `IContentPackageCatalogProvider`、协调器工厂、catalog entry 与快照，不直接访问 HTTP、ZIP、收据或 R2。
- `ContentPackageOperationUiBridge` 通过捕获的 Unity `SynchronizationContext` 把后台状态、进度和错误派发回主线程；销毁页面后已排队回调不会再触碰 UI。
- 页面提供安装、更新、修复、继续、重试、暂停、取消和刷新；展示版本、下载大小、进度与友好错误，未配置远程 catalog 时不会崩溃。
- 已完成页面淡入、状态切换、错误抖动、按钮按压、下载开始/完成/失败音效、完成震动、错误 Attempt 去重、减少动态和动画速度支持。
- `Card_UI` 新增 32 组英/中文内容管理文案；运行时切换中文后页面和动态行会同步刷新。
- 页面 PlayMode fixture 从后台线程加载两个包，覆盖成功安装、失败一次、错误只提示一次、重试成功、完成震动、中文切换和主线程更新。
- 全量 EditMode 164/164、PlayMode 5/5 通过；Android/IL2CPP 构建成功，6 个场景，APK 74.83 MiB，413 个条目中私人内容匹配为 0。
- 正式 Site HTTPS catalog 已由电脑发布器验证并生成 Git 忽略的 `LocalContent/remote-content.json`；APK 仍不内置私有卡图或发布凭据，真机安装时再把只读运行配置放入应用私人目录。

6C5 完成记录（2026-07-24）：

- `IContentPackageLifecycleService` 将查找/卸载边界放在 Application；文件、收据和同卷事务细节留在 Infrastructure，Presentation 不直接删除目录。
- 卸载器只处理收据登记的内容目录和该收据；路径逃逸、链接、损坏收据和未登记文件会被拒绝或保留，玩家库存与设置位于内容根目录之外，不在可删除范围内。
- 实时内容先移动到 `.<ContentRoot>-removing` 事务，再提交收据；提交失败会恢复原目录，二次回滚失败则保留唯一恢复副本和明确路径。
- 内容页对已安装当前版隐藏安装按钮，对旧版显示更新，并提供 4 秒内二次确认的删除按钮；确认、删除成功/失败都有动画、音效或震动，减少动态仍生效。
- 删除成功会丢弃旧下载归档状态、重载本地 catalog，并允许同一个页面/协调器立即重新安装；收藏存档在实际 ZIP 安装→删除→重装期间保持逐字节不变。
- 卸载定向测试 6/6、全量 EditMode 197/197、PlayMode 6/6 通过；Android/IL2CPP 构建成功，APK 74.85 MiB，413 个条目中私人内容名称匹配为 0。

下一切片：`en.base1`、`en.neo1` 与 catalog 的公网远程闭环已经完成；随后让 Android 完成真实首次下载、中断续传、离线重启和卸载/重装真机验收。不要先批量上传全部卡图。

验收：

- 网络中断不会破坏已安装内容。
- 删除系列不删除收藏记录。
- 重新安装后收藏恢复。
- 下载失败不会重复播放错误音效。

预计：4–7 小时。

### 阶段 7：Site / R2 与按需内容发布

状态：7A 受限 HTTPS catalog provider、7B 确定性发布器、7C 私人 R2 上传工具、7D 离线缓存与跨重启续传、7E Site 内容中继均已完成；安全卸载/重装已由 6C5 完成。当前 Site-first 已真实发布 229 包并通过 Android 验收，独立 Cloudflare R2 延后为容量/成本优化。

目标：实现小 APK 和首装后按需下载。

电脑端：

```text
私人导入器
→ 内容转换
→ 确定性 ZIP + SHA-256
→ schema catalog
→ 上传临时 Site R2（以后复制到独立 Cloudflare R2）
```

手机端：

```text
读取 catalog
→ 下载/校验/原子安装系列或语言包
→ 文件缓存与收据
→ 离线加载
```

安全要求：

- 写入密钥只存在电脑端。
- 手机只有读取权限。
- 卡图不进入 Git。
- Android、Windows 分别构建。

计划优化：卡牌数据库与卡图继续使用已经通过真实 ZIP/HTTP fixture 的内容包、收据和文件图片源；不再为了“必须使用 Addressables”复制第二套版本、断点和缓存状态。Addressables 保留给通用卡背、特效、声音或未来确实需要 Unity AssetBundle 的资源。

7A 完成记录（2026-07-24）：

- `HttpContentPackageCatalogProvider` 只允许 HTTPS 与 loopback HTTP，拒绝嵌入用户密码、fragment、非 200、非 JSON 与非 identity 响应。
- 默认请求超时 15 秒、catalog 上限 1 MiB；既检查 `Content-Length`，也对未知长度流逐块计数，防止以内存放大方式绕过上限。
- 外部取消继续抛出，不会伪装成网络失败；内部超时返回结构化错误，最终重定向 URI 会作为相对归档地址的基准并再次执行安全校验。
- Bootstrap 支持 Editor 环境变量、Git 忽略的 `LocalContent/remote-content.json`、可选公开只读 Resources 配置和 Player 私人持久目录；配置只含 URL/超时/大小，不接受仓库内长期密钥。
- `ApplicationServices.Reset` 和重复 Configure 会释放旧 catalog provider 拥有的 `HttpClient`，避免进入/退出 Play Mode 后泄漏连接池。
- provider 定向测试 11/11、Bootstrap/Application 定向测试 7/7；真实 PlayMode fixture 经过 `TcpListener → System.Net.Http → Bootstrap → ApplicationServices → ContentManagementController` 成功列出远程包。
- 全量 EditMode 176/176、PlayMode 6/6 通过；Android/IL2CPP 构建成功，APK 74.84 MiB，413 个条目中私人内容和私人配置匹配为 0。

7B 完成记录（2026-07-24）：

- 新增 EditorWindow 与 Batch 入口；可以扫描私人 manifest、选择任意语言/系列，并显式指定 Catalog Revision、Package Revision 和版本。
- 发布器拒绝空包、重复 Package ID/安装路径、source/output 互相嵌套、链接文件/目录、大小写冲突和不可移植路径。
- ZIP 使用 Ordinal 文件顺序、固定 1980 时间戳、固定外部属性与 UTF-8 Entry；源文件时间和文件系统枚举顺序不进入归档 Hash。
- 每包按实际源文件求 InstalledBytes，归档完成后求 DownloadBytes/SHA-256，以 `packages/{package-id}/{sha256}.zip` 发布，并最后原子写入排序后的 schema v1 `catalog.json`。
- 相同输入连续发布两次时 2 个 ZIP 与 catalog 共 3 个文件均无字节变化；临时 `.publishing-*` 工作区为 0。
- 发布后自动使用正式 Planner、`FileSystemContentPackageInstaller` 和收据安装到隔离验证目录，再由 `PrivateContentCatalogProvider` 读回预期系列数；失败不会把 catalog 当成可发布结果，验证目录最终为 0。
- 发布器定向测试 6/6、全量 EditMode 182/182 通过；新增代码只存在 Editor，不改变已通过的 PlayMode 6/6 与阶段 7A Android/IL2CPP 包。
- 本机已发布 `en.base1` 与 `en.neo1`：ZIP 为 14,906,006 / 16,437,718 bytes，安装内容为 15,189,695 / 16,754,096 bytes，SHA-256 分别为 `2522292c…beceac` 与 `f353fe80…7a861b`；发布输出由 `.gitignore` 保护。

7C 工具完成记录（2026-07-24）：

- 新增 `Private R2 Publisher` EditorWindow 与环境变量 Batch 入口；离线预检先用正式 catalog reader 核对本机 catalog、归档路径、大小和 SHA-256，不触发网络写入。
- 直接实现 R2 S3 Signature V4，region 使用官方要求的 `auto`；接收 Dashboard 给出的完整 S3 endpoint，支持默认/EU jurisdiction，并拒绝把凭据发送到非 `*.r2.cloudflarestorage.com` 主机。
- 内容寻址 ZIP 先 HEAD；冲突对象禁止覆盖，匹配对象可复用。新上传和复用对象都要从 S3 origin 与公开只读 URL 完整读回并重算大小/Hash。
- 所有 ZIP 验证后才发布 `catalog.json`；本机 catalog 若在预检后改变会终止。catalog 的 origin/公开读取再次验证后，才原子写入 Git 忽略的 `LocalContent/remote-content.json`。
- Access Key/Secret 只存在于 Editor 字段或环境变量，不写入项目、日志、catalog、配置或 APK；批处理缺少任一必需值会明确失败。
- R2 发布定向测试 8/8 通过，包含固定 Signature V4 向量、凭据 endpoint 防泄露、catalog-last 顺序、冲突拒绝、对象复用和失败不生成配置。
- Android 私测脚本保留默认本地直推，同时新增 `Remote`、`ResetDownloadedContent`、`ValidateOnly` 与 `SelfTest`；远程配置严格拒绝秘密字段并只推送公开 catalog 参数，纯脚本自测 8/8、本地/远程预检均通过。

7D 完成记录（2026-07-24）：

- `CachedContentPackageCatalogProvider` 只缓存已经通过正式 reader 的 catalog，并与当前配置的 source URI 精确绑定；改变域名/路径后不会静默使用旧来源数据。
- 缓存限制 UTF-8 与大小，拒绝链接、损坏 schema、不同来源和无效 catalog；使用 `.tmp` 原子提交，`File.Replace` 不可用时以 `.backup` 同卷事务替代，并能在中断后恢复旧缓存。
- 在线 catalog 可用但缓存写入失败时仍允许玩家继续使用在线列表，只显示双语琥珀色警告；远程失败且缓存有效时内容页继续显示包列表，切换中英文会同步更新离线提示。
- 外部取消不会回退到陈旧缓存；PlayMode 的 loopback 缓存使用独立临时路径，不覆盖开发者本机私人目录。
- 真实 HTTP 截断测试会先留下实际 `.part`，再销毁旧协调器模拟重启；新协调器从持久字节数发送精确 `206 Range`，完成 SHA/ZIP 安装和缓存清理。
- 缓存定向测试 7/7、安装/重启集成测试 4/4、Presentation 定向测试 16/16、Bootstrap 1/1、全量 EditMode 206/206、PlayMode 6/6 通过。
- Android/IL2CPP 构建成功，APK 74.86 MiB；413 个条目中私人内容、运行配置和 catalog 缓存名称匹配为 0。

7E 完成记录（2026-07-27）：

- 新增独立 vinext Site 与 Sites R2 `FILES` binding；不依赖 Unity、不使用 D1，公开读取 API 与 owner-only 发布 API 分离。
- `/admin` 使用与小说云端相同的唯一管理员邮箱策略：Sign in with ChatGPT 身份头在服务端核对 `TCG_CONTENT_OWNER_EMAIL`；匿名和错误账号写操作分别拒绝为 401/403，生产缺配置时关闭后台。
- 服务端严格读取 schema v1，拒绝未知字段、路径穿越、可变 archive URL、重复包、错误字节和错误 Hash；ZIP 全部验证后才允许切换 catalog。
- ZIP API 提供精确 `Content-Length`、identity、不可变缓存、`Accept-Ranges: bytes` 与开放式 `206 Content-Range`；不支持的 Range 返回带总长度的 416。
- 自动测试 14/14、lint、生产构建与依赖审计 0 漏洞通过；两个真实卡包在本机 Site R2 完成 200/206/416 与全量 SHA-256 读回。
- Site 已公开部署到 `https://universal-gacha-content.jiejingleek.chatgpt.site`，唯一 owner 邮箱环境配置存在且已规范化。公网对两条游戏资源路由共执行 8 个写方法探测，全部返回 `405 Allow: GET, HEAD`；匿名管理写入与伪造身份头均返回 `401`。
- 协议与对象键已经固定；未来把对象复制到独立 Cloudflare R2 并更换 `catalogUrl` 即可，不修改 Unity 下载、安装、收藏或收据身份。
- 发布入口已升级为电脑直传：Unity 新增 Sites API 存储适配器、本机随机凭据和 Batch 入口；`/admin` 只绑定/轮换/撤销令牌 SHA-256，不再读取 ZIP。服务器只保存 Hash，明文只在 Git 忽略的 `LocalContent`，APK 继续完全只读。
- 新增配对认证与适配器定向测试：Site 当前 19/19、Unity Sites Publisher 4/4、Unity 完整 EditMode 233/233 通过；错误/撤销令牌、跨来源 owner、令牌目标域名、公开请求不带凭据和浏览器文件控件退役均有覆盖。批处理可安全生成并自动读取 Git 忽略的本机凭据，不在命令行或日志暴露令牌。

7E 公网收尾（2026-07-27）：电脑直传版本已部署并绑定本机发布器；Unity 成功上传两个 ZIP 后最后切换 catalog。独立客户端再次验证两包完整字节数、SHA-256、中点 `206 Content-Range` 与 8/8 只读方法边界，远程运行配置通过 Android 安装器的无设备预检，项目审计提升到 96%。下一切片只安装到 Android 私人持久目录，完成首次下载、中断续传、离线重启和真机卸载/重装；独立 Cloudflare bucket、Token 与自定义域名延后到 Site 容量或成本需要优化时处理。

7F Play Mode 优先与 Android UI 稳定化（2026-07-29）：验证流程固定为源码修改后先跑定向 PlayMode、完整 PlayMode 和必要 EditMode，只有平台边界才制作候选 APK。首轮把四个操作改为永久节点并以控制器守卫无效操作；第一份候选仍会使文字/背景错位。第二份移除 scale 后仍丢失原生 Button 背景。第三轮彻底移除原生 `Button` 与 `:active`，纯 `VisualElement + Label` 的文字/热区保持正确，但无效 Pause 仍让根背景消失；因此真正共同边界是根 action 节点 background/border transition。第四轮让页头和内容 action 的根节点 class、背景、边框在运行时完全不可变，手动 pointer/keyboard 的按压反馈只改变 Label 颜色，有效操作继续使用统一音效；契约拒绝根 transition、根 pressed class、原生 Button 与 `:active` 回归，合成点击仍会真实启动下载。定向 EditMode 18/18、内容 PlayMode 1/1、完整 PlayMode 7/7、完整 EditMode 235/235 通过。干净 Android 14 同时发现并修复“首次启动前 adb 无权创建 app external files root”的安装器问题，脚本现在先启动一次应用创建目录再推送公开配置，12/12 自测和真实 `-SkipInstall` 路径通过；下一步只构建一份根背景不可变候选完成 Android 远程状态流验收。

7G Android 14 软件验收（2026-07-29）：New Input only 的最终 x86_64 包在网络恢复后不再出现 Input System 空引用；同一包顺序完成首次下载、暂停/恢复/取消、离线缓存、网络恢复、设置持久化、后台/音频焦点、震动请求与真实开包。11 张收藏存档在移除/重装 `en.base1` 前后保持 1317 bytes 与同一 SHA-256，重装后收藏页恢复 `11/204 collected` 和卡图。最终 PlayMode 7/7、EditMode 236/236，空间失败/回滚与云冲突使用明确测试证据；十四项模拟器软件收据已完成，并保留实体触感、扬声器音质和蜂窝切换三项硬件限制。下一步只做一次 ARM64 生产构建与最终审计。

暂停检查点（2026-07-29 08:05，历史记录）：首次收尾构建虽然成功，但 `aapt` 证明产物仍是 x86_64，因此已拒收，项目当时继续保持 96%。生产构建器不再依赖可变 ProjectSettings，而是显式强制 ARM64；模拟器构建显式强制 X86_64，两者都在结束时恢复原状态，定向 EditMode 3/3 已通过。当时的恢复顺序固定为：完整 EditMode → 确认已提交的 ABI 修复 → 重建一次最终 ARM64 → 静态审计 → 启动同一模拟器 → `project_completion_audit.ps1 -RequireComplete`；该顺序已于 2026-07-30 完成。

7H 第一大阶段最终收尾（2026-07-30）：完整 EditMode 刷新为 236/236；修复后的唯一一次生产构建以 6 个场景和 `BuildResult.Succeeded` 完成。最终 APK 为 51,978,834 bytes，SHA-256 `abd93360d61f708f5c1d9bcd02d5bcd9f25761f023b5b01672967436a692dcde`，414 个条目与 6 个原生库均只包含 `arm64-v8a`，敏感名称匹配 0；min/target SDK 为 23/36，权限边界、v1/v2 签名和 zipalign 均通过。完成度审计新增 ABI、权限、签名与对齐硬门槛，自测 4/4；连接同一 Android 14 模拟器后，最终审计输出 `PROJECT COMPLETION VERIFIED: 100%`。当前 APK 使用个人测试 Debug 证书，不冒充应用商店 release 签名。

验收：

- APK 不包含历史卡图。
- 更新内容不重新发布 APK。
- 手机可离线使用缓存内容。

预计：5–9 小时。

### 阶段 8：宝可梦适配层

状态：8A 规则证据基础层以及 8B 的五系列规则覆盖、年代主题框架、五张原创包装、粒子光效与十个正式原创音效均已完成（2026-07-27）。五个测试系列中三套为历史规则、两套为来源模拟；真实 Site/Android 发布验收也已在第一、第二大阶段收尾时完成。

目标：在通用系统上提供不同年代和地区的宝可梦卡包。

工作内容：

- Pokémon 数据适配器。
- 区分国际版、日版和其他地区。
- 研究并记录不同年代的卡包槽位。
- 建立普通、闪卡、反向闪、V、VMAX、Illustration Rare 等规则。
- 给每条规则记录来源、日期和可信度。
- 无法确认时明确标记为模拟概率。
- 为不同年代设计相应包装、开包动画和音效主题，但共用核心系统。

8A 完成记录（2026-07-25）：

- Application 新增通用 `ProductRuleEvidence`、`ProductRuleConfidence` 与三档 `ProductRuleTrust`；地区和中英地区名属于 Profile，不把国际版或日版判断写死在 Presentation。
- `HistoricallyVerified` 与 `SourceInformedSimulation` 都必须提供 HTTPS 证据和非 `Unverified` 可信度；纯模拟规则不能冒充已佐证规则。
- Base Set Unlimited 与 Neo Genesis First Edition 均标记为国际英文地区、`Corroborated`，并记录 2026-07-23 核验日期；现有来源属于佐证资料，因此没有升级为 `Authoritative`。
- 开包页新增双语证据摘要和动态来源按钮；多来源会全部显示，按压动画与确认音效沿用统一反馈服务，模拟规则显示无来源的明确警告。
- 规则/本地化/字体定向 EditMode 15/15、开包场景 PlayMode 1/1、全量 EditMode 211/211、PlayMode 7/7 通过；Android/IL2CPP 干净构建成功。

8B 已完成记录（2026-07-25）：

- EX Ruby & Sapphire 使用 5 Common、2 Uncommon、1 Reverse Holo、1 Rare 的九张卡结构；Rare 槽按 PSA 开盒记录建模为 Non-Holo 26.5/36、常规 Holo 6.5/36、Pokémon-ex 3/36。
- Profile 标记为国际英文地区、`HistoricallyVerified + Corroborated`，核验日期与 PSA 来源会直接显示在现有双语开包界面；不声称 Nintendo 未公开的 Pokémon-ex 精确插入率或工厂序列。
- EX 规则定向 EditMode 4/4、开包场景 PlayMode 1/1、全量 EditMode 212/212、PlayMode 7/7 通过；Android/IL2CPP 干净 APK 为 54,495,759 bytes（51.97 MiB）、413 个条目、私人内容名称匹配 0。
- Sword & Shield Base 使用独立 `PokemonModernRuleProvider`，并由 `PokemonRuleProvider` 与历史模块组合；其 5 Common、3 Uncommon、1 Reverse、1 Rare 和 Rare 权重来自官方边界、4,628 包样本与第三方卡表交叉佐证。
- TCGdex 对 #34/#35 Cinderace 的 VMAX 错标在规则适配层显式修正，不改写私人原始 manifest；实体 Basic Energy 与 code card 因不属于 swsh1 收藏 manifest 而明确省略。
- 玩家界面新增蓝色“来源模拟规则 / SOURCED SIMULATION”第三状态；来源按钮继续使用统一按压动画与确认音效，纯模拟和历史已验证状态仍分别保留。
- Sword & Shield 规则定向 EditMode 2/2、开包场景 PlayMode 1/1、全量 EditMode 214/214、PlayMode 7/7 通过；500 包/5200 张性能回归通过，Android/IL2CPP 干净 APK 为 54,503,774 bytes（51.98 MiB）、413 个条目、私人内容名称匹配 0。
- Scarlet & Violet Base 按官方边界实现 4 Common、3 Uncommon、第一 Reverse、第二 foil 与 Rare-or-higher 共 10 张可收藏系列卡；第二 foil 槽和 Rare-or-higher 槽分别按超过 8,000 包研究归一，不把样本权重声明为官方插入率。
- `PokemonImportedCardVariantPolicy` 在 Catalog 适配边界为 sv01 补正 normal/reverse/holo Printing，使产品由来源卡展开为 444 个可抽取 Printing；策略接口可替换，且不改写 TCGdex 私人原始 manifest。
- Scarlet & Violet 规则定向 EditMode 3/3、开包场景 PlayMode 1/1、全量 EditMode 215/215、PlayMode 7/7 通过；500 包/5100 张性能回归约 0.075 秒、托管内存净增长约 0.063 MiB。Android/IL2CPP 干净 APK 为 54,507,485 bytes（51.98 MiB）、413 个条目、私人内容名称匹配 0。
- 通用 `Gacha.Presentation` 新增 `IProductOpeningThemeProvider` 与验证过的默认主题；宝可梦映射放入独立 `Gacha.Pokemon.Presentation`，不会把系列 ID、年代配色或稀有度命名写进通用控制器。
- 五个系列的主题 ID、USS class、撕包/揭晓节奏和音效键均不同；当前导入资料没有设置 `RarityDefinition.PresentationKey`，宝可梦主题因此在适配层用可配置稀有度 ID 片段补足稀有揭晓，未知游戏仍使用通用后备主题。
- 稀有卡会显示主题光环并播放主题音效；减少动态效果会保留静态强调而跳过缩放动画。所有原本缺少素材的核心反馈与十个主题音效键都有低音量程序化后备，正式 `AudioClipConfig` 仍有优先权。
- 主题契约定向 EditMode 4/4、程序化音频定向 EditMode 1/1、开包场景 PlayMode 1/1、全量 EditMode 221/221、PlayMode 7/7 通过。Android/IL2CPP 干净 APK 为 54,522,308 bytes（52.00 MiB）、413 个条目、私人内容名称匹配 0。
- 五张原创包装分别使用 vintage、forest、ruby、electric、gallery 抽象视觉，不包含 Pokémon 名称、角色或官方包装元素；生成提示词和替换规则记录在 `ERA_THEME_ART.zh-CN.md`。
- `ThemeArtworkImportProcessor` 将五张源图限制在移动端合适的 512px、无 mipmap、Clamp 与 Android ASTC 6×6；Editor 测试同时验证实际导入尺寸和平台覆盖设置。
- 包装资源/导入设置定向 EditMode 3/3、主题映射 EditMode 4/4、开包场景 PlayMode 1/1、全量 EditMode 222/222、PlayMode 7/7 通过。Android/IL2CPP 干净 APK 为 54,826,948 bytes（52.29 MiB），相对上版增加约 0.29 MiB，413 个条目、私人内容名称匹配 0。
- `ProductOpeningParticleTheme` 在通用 Presentation 层验证环境/爆发粒子数量、周期、漂移、持续时间、半径和脉冲范围；宝可梦模块为五个系列提供不同参数，通用控制器不判断系列 ID。
- `ThemeParticleField` 预建并循环复用最多 12 个 UI 元素，以约 30 FPS 更新；准备卡包时播放环境漂浮，稀有卡揭示时播放径向爆发，切换阶段、查看结果或返回选择页都会停止并隐藏。减少动态效果开启时不会启动调度器。
- 粒子/主题/界面定向 EditMode 9/9、开包场景 PlayMode 1/1、全量 EditMode 224/224、PlayMode 7/7 通过。Android/IL2CPP 干净 APK 为 54,852,963 bytes（52.31 MiB），相对上版增加 26,015 bytes（约 0.02 MiB），413 个条目、私人内容名称匹配 0。
- 五套主题各新增一份撕包 WAV 与稀有揭晓 WAV；资产由确定性编辑器生成器烘焙，不包含第三方录音或官方音频，来源与重建规则记录在 `THEME_AUDIO.zh-CN.md`。
- 音频/反馈定向 EditMode 12/12、连续重建 10/10 Hash 不变、全量 EditMode 229/229、PlayMode 7/7 通过。Android/IL2CPP 干净 APK 为 55,027,355 bytes（52.48 MiB），相对上版增加 174,392 bytes（约 0.17 MiB），414 个条目、私人内容名称匹配 0。

验收：

- 五个测试年代使用各自规则。
- 旧系列不会使用现代稀有度逻辑。
- 玩家能看出真实规则与模拟规则的区别。

预计：10–25 小时。

### 阶段 9：Android 真机验收

状态：Android 14 模拟器软件验收、最终 ARM64 构建、静态审计与 100% 完成度审计均已完成（2026-07-30）。实体震动手感、扬声器音质和蜂窝切换保留为实体机限制，不被模拟器结论替代。

重点：

- 首装和首次下载。
- Wi-Fi 与移动网络切换。
- 下载中断、空间不足和强制关闭。
- 大型收藏列表性能。
- 音频焦点、来电和后台恢复。
- 动画在不同帧率下的稳定性。
- 震动、静音和减少动态效果。
- 云存档冲突。

验收：

- 中端 Android 操作流畅。
- 冷启动不加载全部卡图。
- 下载失败不损坏收藏。
- 所有核心交互都有适量而不扰人的反馈。

预计：4–8 小时。

## 五、预计投入

- 阶段 0–5，本地 MVP：23–39 小时。
- 阶段 6–7，远程内容：9–16 小时。
- 阶段 8–9，宝可梦适配与真机验收：14–33 小时。
- 完整项目：约 46–88 小时，不含外部等待和大量规则资料调查。

以 2026-07-30 的最终完成度复核：

- 第一个可玩的纵向切片：已完成。
- 完成本地通用模拟器 MVP：代码、正式年代主题资产和自动化均已完成。
- 达到软件范围完整 100%：Site、模拟器十四项收据、自动回归、最终 ARM64 构建、静态审计和完成度审计均已完成。实体震动手感、扬声器音质与蜂窝切换仍需未来连接实体 Android 补充，不改写当前模拟器证据。
- 第二大阶段完整 100%：229 个按需内容包、全世代图鉴、卡牌关联、全量 Site 发布和 Android 14 最终收据均已完成；后续工作属于新范围。

Token 仅能估算累计工作量，平台没有固定可见的单任务总上限：

- 本地 MVP：约占整体 75%。
- 远程内容：约占整体 15%。
- 宝可梦适配与验收：约占整体 10%。
- 全项目可能累计需要约 30 万–60 万 Token，分阶段推进并依赖上下文压缩。

## 六、执行顺序与暂停条件

原先的固定阶段顺序保留为职责编号，不再作为严格施工顺序。当前关键路径改为：

```text
阶段 3：双层语言 + Application Catalog 边界（完成）
→ 阶段 4：卡图异步加载 + 系列/卡牌浏览（完成）
→ 阶段 5A：单卡包可玩闭环（模拟规则、Base Set、Neo Genesis 与 EX Ruby & Sapphire 历史规则完成）
→ 阶段 5B：库存、新卡、搜索与筛选体验（完成）
→ 阶段 5C：设置完整性、性能、中文字体与 Android 构建（完成）
→ Android 14 模拟器私人内容、触摸、设置、后台与震动请求验收（完成；实体硬件限制保留）
→ 阶段 6 内容管理页面、主线程派发与游戏反馈（完成）
→ 阶段 7A：HTTPS catalog + 私人运行时配置（完成）
→ 阶段 7B：确定性内容包发布器 + 两个历史系列本机 fixture（完成）
→ 阶段 7C：私人 R2 安全上传工具（完成；真实写入等待私人参数）
→ 阶段 6C5：安全卸载、保留收藏与同页重装（完成）
→ 阶段 7D：已验证 catalog 离线缓存 + 跨重启续传（完成）
→ 阶段 7E：临时 Site R2 内容中继 + 真实双包本机读回（完成）
→ 阶段 8A：地区、可信度、核验日期与多来源证据基础层（等待外部条件期间并行完成）
→ 阶段 8B 第一套：EX Ruby & Sapphire 九张卡历史规则（完成）
→ 阶段 8B 第二套：Sword & Shield Base 来源指导模拟（完成）
→ 阶段 8B 第三套：Scarlet & Violet Base 来源指导模拟 + foil 形态补正（完成）
→ 阶段 8B 第四套：五年代模块化开包主题、动画、光效与音效键基线（完成）
→ 阶段 8B 第五套：五张原创年代包装、移动端纹理导入与准备动画（完成）
→ 阶段 8B 第六套：通用主题粒子场、五年代环境/爆发光效与减少动态适配（完成）
→ 阶段 8B 第七套：十个原创烘焙 WAV、确定性重建与移动端音频导入（完成）
→ 最小真实 Site 发布 + Android 下载/中断/离线缓存验收（完成）
→ 阶段 9：模拟器软件验收（完成）
→ 第一大阶段完成：ARM64 生产构建 + 静态审计 + 最终完成度审计均通过
→ 第二大阶段 Phase 2A：数据契约与稳定排序（完成，整体 8%）
→ 第二大阶段 Phase 2B：metadata-only 全量清单盘点（完成，整体 15%）
→ 第二大阶段 Phase 2C：英文全量可恢复导入与完整性审计（完成，整体 30%）
→ 第二大阶段 Phase 2D：218 包确定性发布与 Gen 1 Site pilot（完成，整体 45%）
→ 第二大阶段 Phase 2E：PokéAPI generation/species/form 图鉴资料层（完成，整体 57%）
→ 第二大阶段 Phase 2F：Card→Species/Form 多对多关联与人工复核（完成，整体 71%）
→ 第二大阶段 Phase 2G：第一世代图鉴玩家旅程（完成，整体 83%）
→ 第二大阶段 Phase 2H：全世代、地区形态与图鉴图片（完成，整体 91%）
→ 第二大阶段 Phase 2I：按需相关卡牌画廊（完成，整体 96%）
→ 第二大阶段 Phase 2J：229 包全量发布、远端与 Android 验收（完成，整体 100%）
→ 第三阶段：原创训练家终端 UI、中英日实体卡、顺畅度和语言隔离（完成，整体 100%）
→ 第四阶段计划：资料语义、内容体验、存档恢复、R2、实体机与模块化（4A–4J、4L 本机范围完成；4K R2 暂停）
→ 当前下一步：4M 只构建一次最新 ARM64 APK并完成本机静态审计；4H 真实邮箱、4I 真机性能与 4K R2 等待外部条件
```

Phase 2A–2J 已完成。手机端只读取已发布的内容寻址 catalog/ZIP，不直接请求 TCGdex、PokéAPI 或 GitHub；电脑端导入、转换、压缩、Hash 与发布继续保持私人写入边界。

第三阶段已于 2026-07-30 完成。范围包括原创训练家终端 UI、Android 顺畅度，以及英文 `en`、日文 `ja`、简体中文 `zh-cn` 三种实体卡牌版本。跨语言卡牌采用独立版本组：只有一个已安装版本时直接显示；有两个或三个版本时才显示语言切换，并同时更换卡图与全部印刷资料。应用语言和卡片语言不共享状态、事件或可用性判断。最终容量、数据身份、性能策略与验收证据见 `PHASE_3_UI_PERFORMANCE_MULTILINGUAL_CARDS.zh-CN.md`。Cloudflare R2 迁移和实体硬件体验仍是独立后续范围，不能冒充已经验证的结果。

第四阶段计划已于 2026-07-31 建立，纳入正式跨语言卡牌身份、日文缺图、Catalog v2、虚拟化内容库、批量下载、数据驱动配列、存档恢复、独立日文 UI、图片内存预算、Cloudflare R2、实体 Android 和剩余模块化。第三阶段的技术验收不再被当作第四阶段的资料语义证据；第四阶段从正式 44,076 份来源记录的可重复审计开始。

第四阶段 4A 已于 2026-07-31 完成：只读审计正式扫描 524 个 Set、44,076 张来源卡牌和 37,588 张本地图片，结构失败为 0；两次独立运行得到同一快照 SHA-256 `183195b01a22c8e36198834aa6fa39fbd4bddf036380168647c5d965c6e520ef`。518 个直接比对候选组仍只是 4B 的复核输入，不视为已经合并的卡牌身份。Cloudflare R2 迁移按用户要求暂停，本轮不申请凭据、不上传、不切换远端配置。

第四阶段 4B 已于 2026-07-31 完成：版本化 Set/人工 override 数据绑定 4A 快照，过期、歧义与同语言合并均失败关闭；身份编译器在正式资料上自动接受 147 个 `en+ja` 双强信号组（294 张卡），把 224 个 `en+zh-cn` 单信号编号碰撞组（448 张卡）保留为人工复核，并让其余 43,334 张保持未匹配。两次生产编译的身份快照均为 `8fd886f0b8caa77f17c9e4c57a9a73b2b0494cc2b5b6b2aa8ea97bd3b04c10d7`，精简复核队列也逐字节稳定；定向测试 5/5、完整 EditMode 352/352 通过。运行时 Catalog 接入留在 4D，现有收藏 `printingId` 未改变。

第四阶段 4C1 缺图来源复查已于 2026-07-31 完成：只读工具用 119 个按 Set/直链请求重新核对全部 6,488 张缺图；英文 1,616 与日文 4,862 张在当前 TCGdex 响应中仍没有图片字段，简中 10 个旧来源 URL 均为 404，可下载队列为 0，网络探测失败为 0。两次远端复查的稳定快照均为 `b9a0e92cea60643f62c6a9d24ac1890d396be480b912cc4001769b99a3b16cf8`；定向测试 4/4、完整 EditMode 356/356 通过。该结果随后作为非破坏性简中 WebP 95/90/85 分层容量实验的输入。

第四阶段 4C2 简中 WebP 容量实验已于 2026-07-31 完成：只读工具扫描 12,463 张、681,504,080 bytes 的正式图片并分层抽取 120 张；Q90 样本节省 23.07%（预计全量约 157,235,573 bytes），Q85 节省 40.24%（预计约 274,249,066 bytes）。修正并测试 Unity/WebP 行方向后，两次正式运行快照均为 `c4ff57a3f56e6fd42ba458b1f2e072ae137c8ff5dfea1f18442723e26076ce3f`，报告与 48 个复核文件 Hash 也逐字节稳定。桌面并排抽查未见明显差异，但因现有来源已是有损 WebP 且没有实体手机放大详情证据，本轮按门槛保留原图，不二次有损改写或发布新 revision。定向测试 3/3、完整 EditMode 359/359 通过；4C 至此完成，下一步进入 4D 正式跨语言运行时整合。

第四阶段 4D1 正式身份运行时整合已于 2026-07-31 完成：4B 报告被确定性编译为 147 组/294 成员/84,777 bytes 的严格玩家覆盖文件，两次 SHA-256 均为 `3568e20cdf3d202867c2739c1ded291911c1d67c72d5f1a4ee8adbef066cedce`。正式 Catalog 让 147/147 组进入运行时，同时保留 44,076 个独立来源 Item 与 53,480 个正式规则 Printing；只有显式接受组可以切换，待复核英文/简中同号碰撞保持单语言。收藏数量仍按 Printing 保存，卡图/名称/地区 Set/卡号/稀有度/variant 同步切换，应用语言和全局卡片语言不被详情切换修改，并保留 120 ms 淡入、翻卡音效与减少动态效果。完整 EditMode 363/363、PlayMode 10/10 通过。

第四阶段 4D2 本地重发已于 2026-07-31 完成：三语言 Card→Subject 快照使用新唯一 Item ID 重建且 0 失败；本地 Catalog 从 revision 4/537 包迁移到 revision 5/538 包，534 个旧 descriptor 不变、3 个关联包升级、1 个 4,621 bytes 的语言覆盖 ZIP 新增，实际增量下载 1,265,341 bytes。连续两次构建稳定，Catalog/package identity SHA-256 分别为 `5e03070e29a28e91cf583c49f43f514802a4929c956bda47686816db2886a180` / `26a368a8d769f598084049b06989e52d0615ec81b41625cc914d85904b8ef01c`；538/538 本地安装回读、44,076 卡、53,480 Printing、147 语言组、44,076 图鉴关联全部通过。revision 4 Catalog 与 537 个旧归档已验证可回滚；没有上传 Site/R2。4D 至此完成，下一步进入 4E Catalog v2 通用玩家元数据。

第四阶段 4E–4F 已于 2026-07-31 完成：Catalog schema v2 为 538 个包加入通用种类、语言、名称、日期、世代、排序和依赖元数据；内容管理页改为虚拟化、筛选/排序/批选、有限并发队列、空间/网络预检与可恢复断点。正式 538 包和合成 2,000 包均保持有限可见行，重启后队列以暂停状态恢复且不会绕过当前网络策略。完整 EditMode 393/393、PlayMode 11/11 通过；没有上传或配置 Site/R2。

第四阶段 4G 第一组已于 2026-07-31 完成：建立品牌无关、schema v1 的版本化规则数据、JSON source 与严格编译器，把 3 套历史规则和 2 套来源模拟从硬编码 Pokémon provider 迁入数据文件。Profile 显式携带 revision、卡片语言、发行日与排除项，资料的发行日、筛选卡数、卡池和卡位引用在游戏前失败关闭；旧卡位、概率与例外保持等价，并补充日文规则说明。定向 EditMode 17/17、完整 EditMode 395/395、PlayMode 11/11 通过。4G 下一组为 1/10 包原子事务、批量动画、开包历史和统计。

第四阶段 4G 第二组已于 2026-07-31 完成：1 包与 10 包共用单次库存事务，十连只写盘一次，失败同时回滚卡片、NEW、计数、历史与统计。存档 v4 保留 v2/v3 读取并新增 250 条有界历史以及语言/Set/稀有度统计。玩家页加入中英文 1/10 包选择、首包完整动画、后续短过渡、全部揭晓、批次总结和历史统计面板，沿用按压、开包、翻卡、稀有与收藏音效及减少动态效果。真实场景验证 10 包/110 张批次及累计 12 包/132 张统计；完整 EditMode 397/397、PlayMode 11/11 通过。4G 至此完成，下一步为 4H 存档恢复。

第四阶段 4H 本地安全恢复已于 2026-07-31 完成：版本化 envelope 以独立游戏命名空间、来源安装 ID、UTC 和 SHA-256 严格校验收藏/NEW/开包历史统计、独立语言偏好和体验设置；预览确认前不写入，导入前自动备份，失败自动回滚，卡图与任何凭据不进入存档。设置页提供中英保存、选择预览、确认使用、状态反馈、音效和减少动态动画。Android 原生桥使用 Storage Access Framework 且不申请广泛存储权限；完整 EditMode 406/406、PlayMode 11/11、ARM64 Clean Build 和 DEX 类检查均通过。APK 为 52,849,127 bytes（50.40 MiB），SHA-256 `3405495aca82c1f5325d83eb1232fa627573a8df4fe0aca8dd20a4566ae21fac`；本切片没有访问 Site/R2。4H 下一组为可恢复游戏身份以及本地/云端冲突摘要、选择、合并和回滚。

第四阶段 4H 云冲突选择已于 2026-07-31 完成：双方都有不同进度时启动流程保留本地且停止普通云写入，不再由时间戳静默覆盖；设置页显示两边摘要并提供保留当前、采用云端和保守合并。保守合并按卡版/统计取最大值、联合 NEW 与不同交易 ID，避免快照重复相加。选择前自动导出安全备份，云写入失败会回滚本地并保留冲突。完整 EditMode 412/412、PlayMode 11/11 通过。4H 只剩可恢复身份；现有 Unity Player Accounts Client ID 为空，真实邮箱入口继续关闭，下一步建立安全 Profile 切换和外部配置预检。

第四阶段 4H 可恢复身份基础设施已于 2026-07-31 完成：版本化游戏身份配置固定独立 Project ID、命名空间和认证 Profile，只允许 `openid/email/offline_access`，不申请 Gmail/Drive。Client ID 为空时设置页明确显示本机保存状态并禁用入口。新身份原地绑定现有匿名 Player；已有身份不使用 `ForceLink`，而是先备份、切换隔离 Profile、读取云存档并复用显式冲突选择；失败恢复本地与原认证 Profile。仅持久化有效 Profile 名，邮箱只显示掩码且不记录令牌。完整 EditMode 423/423、PlayMode 11/11 通过；没有重建 APK，也没有访问 Site/R2。4H 本机实现完成，真实邮箱/换机演练等待外部 Client ID；当前转入 4J。

第四阶段 4J 独立日文 UI 已于 2026-07-31 完成：新增正式 `ja` Locale、Addressables 日文表、Android 日文应用名和图鉴三语文本；`Card_UI` 215/215 个键全部具备日文翻译与格式参数一致性保护。应用语言固定为 `en/zh/ja`，卡牌语言仍为 `en/ja/zh-cn`，9 种组合、设置页和图鉴场景均验证互不驱动。CJK 动态字体子集由 63,648 增至 197,712 bytes，并改用真实 TMP 字形加入验证以捕获缺字。最终 EditMode 426/426、PlayMode 11/11 通过，日文场景缺字与缺翻译日志均为 0；未重建 APK，未访问 Site/R2。当前转入 4I 图片内存预算与 4L 模块化收尾。

第四阶段 4I 本机图片内存保护已于 2026-07-31 完成：卡图缓存采用张数与 48 MiB 解码 bytes 双门槛，压缩图片读取默认限制为 32 MiB；显示纹理由租约保护，Android low-memory 只立即销毁未显示资源，显示资源在解绑后释放，旧代加载不得重新污染缓存。收藏、开包和图鉴均公开实时 bytes/预算诊断值。定向 EditMode 16/16、定向 PlayMode 4/4、完整 EditMode 432/432、完整 PlayMode 11/11 通过，其中 256 张连续加载始终保持预算。没有重建 APK、没有访问 Site/R2；实体 Android 长时 Profiler、系统 low-memory、触觉、音频与蜂窝证据仍待设备验收。当前转入 4L 模块化收尾。

第四阶段 4L1 组合根程序集边界已于 2026-07-31 完成：模块目录外的 39 个应用组合脚本全部由新增 `Gacha.Runtime` asmdef 拥有，原七个模块边界保持独立；脚本路径和 meta GUID 均不移动。动态编译图测试验证这些脚本不会回落到 `Assembly-CSharp`。无 Scene/Prefab/源码引用的旧 `CardFlip` 与未使用的 Visual Scripting 依赖被删除。独立程序集编译 0 error，边界测试 1/1、完整 EditMode 433/433、完整 PlayMode 11/11 通过，场景往返无 Missing Script；没有构建 APK或访问 Site/R2。下一步按独立 Git 切片拆分存档/身份与玩家控制器。

第四阶段 4L2 玩家资料、存档与身份核心边界已于 2026-07-31 完成：`002_Core` 的库存模型、Local/Cloud Save、冲突/恢复、可恢复身份、Bootstrap、内容交付与玩家进度 Store 全部进入独立 `Gacha.Runtime.Core`；组合根单向引用 Core，Core 不反向引用设置、控制器或组合根。脚本与 meta GUID、存档 v4、云端 key、PlayerPrefs 和场景序列化均未改变。动态编译图验证 39 个旧应用脚本精确归属于两个 Runtime 程序集且不回落到 `Assembly-CSharp`。Core 独立编译 0 error，边界 EditMode 1/1、完整 EditMode 433/433、完整 PlayMode 11/11 通过；没有构建 APK，也没有访问 Site/R2。下一切片拆分玩家控制器。

第四阶段 4L3 设置、场景与音频基础边界已于 2026-07-31 完成：`001_Baisc` 的 GameManager、设置、文件辅助、场景/分辨率和音频脚本进入 `Gacha.Runtime.Foundation`；被设置与加载共同使用的 `Fade` 进入无反向依赖的 `Gacha.Runtime.Utility`。Foundation 只单向引用 Core/Utility 与既有通用模块，不引用组合根；脚本/meta 路径、场景 GUID、设置状态、动画和音效不变。两个新程序集独立编译均为 0 warning / 0 error，边界 EditMode 1/1、完整 EditMode 433/433、完整 PlayMode 11/11 通过且无 Missing Script；没有构建 APK，也没有访问 Site/R2。下一切片拆分玩家控制器。

第四阶段 4L4 玩家控制器边界已于 2026-07-31 完成：开包、收藏、主菜单、返回与启动 5 个场景控制器进入独立 `Gacha.Runtime.Controllers`，只引用 Foundation/Core 与既有通用模块，不引用组合根。5 个原 meta GUID 与各自 Scene 引用均保持不变；编译图契约精确验证源码归属和依赖方向。Controllers 独立编译 0 warning / 0 error，边界 EditMode 1/1、完整 EditMode 433/433、完整 PlayMode 11/11 通过且无 Missing Script；没有构建 APK，也没有访问 Site/R2。组合根只剩 `GradientBackground`，下一切片收拢它并退役空根。

第四阶段 4L5 空组合根退役已于 2026-07-31 完成：最后的 `GradientBackground` class/meta GUID 在 Scene、Prefab、源码及其他资产均为 0 引用，现行玩家背景由 UI Toolkit 主题实现，因此安全删除；`Gacha.Runtime` 根 asmdef GUID 也只有自身 meta 且无人引用，随之退役。剩余 38 个旧应用脚本全部精确归属于 Core、Foundation、Utility 或 Controllers，不回落 `Assembly-CSharp`。最终边界 EditMode 1/1、完整 EditMode 433/433、完整 PlayMode 11/11 通过且无 Missing Script；存档、场景 GUID、动画、音效与玩家行为不变，没有构建 APK，也没有访问 Site/R2。4L 本机模块化到此完成。

第四阶段 4M1 本机完成度审计基线已于 2026-07-31 完成：完成度审计器从旧 218 包/schema v1 改为严格验证正式 538 包/revision 6/schema v2 及 1.30 GB 归档 Hash；Android 权限契约接受内容网络策略需要的 `ACCESS_NETWORK_STATE`，仍拒绝广泛存储权限。远端不再只凭 HTTPS URL 通过，必须由运行配置、HEAD/Range/8 次写拒绝报告和当前 Catalog URL/包数/Hash 三方一致；现有 Site 报告仍对应旧 537 包，因此正确阻塞。审计自测 5/5，433/433 EditMode、11/11 PlayMode、538/538 本地归档、现有 APK 的 ARM64/隐私/权限/签名均通过；因 APK 早于最新源码且无连接设备，当前审计为 81%。下一步只构建一次最新 ARM64 APK完成本机静态收口，不访问 R2。

以下情况必须暂停后续阶段并先修复：

- 自动化测试失败。
- 存档迁移可能导致收藏丢失。
- 数据模型无法表达真实导入内容。
- UI 功能完成但缺少必要反馈、音效或可访问性设置。
- 远程下载可能覆盖或删除本地收藏。
- 宝可梦规则来源不明确且界面没有标记为模拟。

在阶段 5 完成前，不继续批量下载更多系列；在阶段 7 完成前，不把当前私人卡图上传到远程存储。

## 七、计划修改规则

每次修改本计划时：

1. 更新文档顶部日期。
2. 说明修改原因。
3. 不删除已完成记录，只标记替代方案。
4. 新增功能必须说明属于哪个模块和阶段。
5. 新增玩家交互必须同时指定动画、音效和异常反馈。
6. 实施结果、测试数据和已知限制写回对应章节。
