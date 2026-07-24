# Universal Gacha Simulator 项目主计划

最后更新：2026-07-24

本次修改原因：阶段 7C 的私人 R2 离线预检、S3 Signature V4 上传、不可变对象冲突保护、origin/公开 URL 双重下载复核和 catalog-last 发布工具已完成。下一步需要用户提供私人 R2 参数后执行最小真实上传，再进入手机真实下载；卡牌内容不再强制重复包装成 Addressables。

本文档是项目实施、验收和后续修改的主要依据。架构细节参考 `ARCHITECTURE.zh-CN.md`，远程资源细节参考 `REMOTE_CONTENT.zh-CN.md`。

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
| 普通按钮 | 按下缩放、回弹、禁用状态 | 通用点击音效 |
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
- 当前本地资料为 796 张卡、约 104.1 MB 卡图、0 个 Hash 错误。
- 当前核心 EditMode 测试全部通过。
- 已建立第一个独立程序集 `Gacha.Presentation`，作为后续模块化迁移基线。
- 已建立统一 `UIFeedbackService`、稳定音效键、震动接口与无障碍偏好。
- 现有 UGUI 按钮会在运行时自动获得按下、悬停和回弹动画，不需要修改场景引用。
- 音效资源尚未配置时会使用低音量程序化点击声，后续可由正式音效无缝覆盖。
- 反馈系统、通用领域模型、Application 状态、内容适配器、图片源、纹理缓存、新抽卡引擎、产品开启、收藏进度、体验设置、版本化资源包 catalog、协调安装、HTTP 断点下载、远程 catalog、确定性发布器和内容管理 Presentation 均有自动化测试；当前项目 EditMode 测试为 182/182 通过。
- 私人 `manifest.json` 已能在运行时转换为 `UniversalCatalog`，不再只属于编辑器导入流程。
- 本机五个历史系列已验证为 5 个系列、796 个收藏项目、12 种稀有度和 1278 个可分别计数的印刷版本。
- 已建立无 Unity 依赖的 `Gacha.Application`，Controller 通过 `CatalogSession` 使用内容，不再直接构造私人导入读取器。
- UI 与卡牌内容语言已经分离，设置场景提供两个独立选择器、回退提示、持久化、淡入动画和确认音效。
- 私人卡图已经支持异步读取、重复请求合并、32 张 LRU 纹理缓存、加载占位、失败重试和失效请求取消。
- 收藏场景已经可以按系列浏览本机 796 张卡，仅为可见列表项加载图片，并提供双语界面、详情入场动画、翻卡/返回/错误反馈和减少动态效果支持。
- 收藏场景已接入真实库存数量、持久化 NEW 状态、名称/卡号搜索、稀有度筛选、仅拥有/仅新卡切换和空结果反馈；查看新卡详情会立即保存已查看状态。
- 库存快照已升级为 v3，云端优先读取 `inventory_v3` 并保留 `inventory_v2` 回读；旧 v2 收藏不会在迁移后被误标为新卡。
- 已建立可替换的 `IProductRuleProvider`、规则可信度标记、卡位平均概率摘要和原子库存提交；落盘失败会回滚本次开包。
- 抽卡场景已经支持五个系列/产品选择、模拟概率说明、准备卡包、撕包动画、逐张翻卡、新卡/持有数量和结果总结，并立即保存本地库存。
- 英文 Base Set Unlimited 已接入第一份 `HistoricallyVerified` 配列：5 Common、2 Basic Energy、3 Uncommon、1 Rare，Holo 平均约三包一张；Machamp 和 First Edition 已按来源排除。
- 英文 Neo Genesis First Edition 已接入第二份 `HistoricallyVerified` 配列：7 Common、3 Uncommon、1 Rare，Holo 平均约三包一张；只会抽出第一版 Printing。
- 设置页已提供静音、减少动态、震动和 0.5x–2.0x 动画速度控件，修改会原子保存并立即预览；保存失败不会发布错误状态。
- 开包页已提供 `Reveal All / 查看全部`，可以取消正在播放的逐张揭晓动画并直接进入完整总结。
- 51 KB 的 Noto Sans SC 修改子集已作为全局 TMP 中文回退字体；自动化会同时检查 String Table 与代码内中文，不再出现缺字方框。
- 连续开 500 包/5500 张卡约 0.138 秒，测试后托管内存净增长约 0.059 MiB；三轮核心场景切换净增长约 0.137 MiB；1 万卡存档 JSON 约 464 KB。
- Android/IL2CPP 开发 APK 已成功构建，包名 `com.personal.universalgacha`；阶段 7A 最新 APK 为 74.84 MiB，6 个场景、413 个 ZIP 条目中私人内容和 `remote-content.json` 匹配均为 0，ADB 私人内容推送脚本已准备。阶段 5C 曾约 51.6 MiB，新增约 23.2 MiB 需继续结合 IL2CPP stripping 与构建生成设置做包体回归分析。
- 抽卡、收藏、设置、内容管理、远程 catalog 和场景切换 PlayMode 测试为 6/6 通过。
- 通用 `ContentPackagePlanner` 已能判断新装、更新、Hash 修复、无需操作、空间不足和存储不可用；不会用旧 catalog 降级有效的新版本。
- 本地 `.packages/<package-id>.json` 安装收据读取器已阻止路径逃逸、串包和损坏收据；Android 使用 `StatFs`、编辑器/桌面使用卷信息检查剩余空间。
- ZIP 安装器会先核对下载字节数与 SHA-256，再在实时 Catalog 目录外解压；只有实际解压字节数也匹配后才原子替换系列目录并发布收据。
- 归档路径逃逸、重复路径、损坏归档、取消、未登记目录冲突或收据发布失败都不会覆盖旧内容；回滚也失败时会保留恢复工作区而不是清理唯一副本。
- 下载任务已支持暂停、继续、取消、失败重试、单包并发去重和绝对已落盘字节进度；一次失败尝试只发布一次错误事件。
- `.part` 文件可以跨任务和重启按真实长度继续，达到声明大小后才发布为 `.zip`；本机文件源已经用与未来 HTTP Range 相同的 offset 合同完成验证。
- HTTP 字节源只允许公开 HTTPS 和 loopback HTTP；新下载必须返回精确 `200`，续传必须返回匹配 offset、末字节与总大小的 `206 Content-Range`，忽略 Range、错误长度、压缩编码和截断响应不会制造假完成。
- schema v1 包清单要求每个 archive URL 包含对应 SHA-256；单包协调器已经统一规划、下载、暂停/取消/重试、原子安装和归档清理，玩家界面不需要自行排列基础设施调用。
- 主菜单已加入 `CONTENT` 入口；内容管理页按包显示版本、下载大小、状态与进度，并提供安装、更新、修复、继续、重试、暂停、取消和 catalog 刷新操作。
- 内容管理页使用 Unity 主线程桥接协调器事件，支持进入/状态切换/失败抖动动画、按钮按压、下载开始/完成/失败音效、完成震动、减少动态和 32 组中英文本；一次失败尝试只提示一次。
- 远程 catalog provider 已支持 HTTPS、loopback fixture、15 秒默认超时、1 MiB 默认上限、流式二次计数、JSON/identity/200 校验、外部取消和最终重定向 URI；Bootstrap 可从私人文件或 Editor 环境变量配置，不向仓库或 APK写入密钥。
- 电脑端发布器已按稳定文件顺序、固定 ZIP 时间戳和实际字节生成内容寻址归档与 schema catalog；发布后会用正式 Planner/安装器安装到临时目录并由运行时 Catalog 读回，验证完成才算成功。
- Base Set 与 Neo Genesis 已生成两个本机 fixture：下载大小 14,906,006 / 16,437,718 bytes，安装大小 15,189,695 / 16,754,096 bytes；连续构建 3 个发布文件的 Hash 全部不变，输出位于 Git 忽略目录。
- 私人 R2 Editor/Batch 上传器已就绪：凭据仅从进程内输入或环境变量读取，限制 S3 endpoint，拒绝覆盖冲突的不可变 ZIP，先验证 origin 与公开 URL，最后发布 catalog 并生成私人运行配置。

尚未完成：

- EX、Sword & Shield、Scarlet & Violet 等其余年代具有可引用来源的真实卡包配列规则；未验证产品继续明确使用等概率模拟规则。
- 其余菜单和游戏场景尚未全部迁入 Unity Localization String Table；当前运行时双语文本与中文回退字体已可用。
- 真实 R2 参数、最小上传与手机真实下载闭环；上传代码已完成，外部写入尚未授权/执行。
- 已安装内容的卸载/缓存删除操作；必须保留收藏记录，并验证重装后恢复。
- 宝可梦不同年代的真实卡包配列规则。
- Android 真机验证。

按验收条件而不是代码数量估算，当前技术底座约完成 99%，本地通用模拟器 MVP 约完成 96%，完整计划约完成 74%。本地 MVP 剩余 4% 是必须在连接 Android 真机后完成的设备验收；阶段 7 仍需真实 R2 发布、卸载/重装和断网真机闭环。

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
- 阶段 5C 已补齐静音、减少动态、震动和动画速度控件；正式音效资产仍可在后续美术阶段替换当前低音量程序化反馈声。
- 已知限制：`GachaViewController` 仍直接构造私人内容读取器，不满足 Presentation 只依赖 Application 接口的最终边界；该问题并入阶段 3 的语言与 Catalog 状态服务一起解决。

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

状态：通用规则引擎、可替换规则提供器、概率摘要和单包 UI 已完成；英文 Base Set Unlimited 与 Neo Genesis First Edition 已成为两份附来源的历史规则（2026-07-23），其余年代待逐包验证。

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
- `IProductRuleProvider` 返回规则可信度和来源引用；当前 `UniformSimulationRuleProvider` 标记为 `Simulated`，以后可以按产品替换成 `HistoricallyVerified` 配置。
- `ProductOddsAnalyzer` 汇总多卡槽权重池的平均卡位概率；条件保底会单独标记，不与基础概率混淆。
- 模拟卡池只包含当前 Content Language 的印刷版本，不会把不同语言卡面混入同一包。
- `PokemonHistoricalRuleProvider` 为英文 Base Set Unlimited 提供 11 卡槽经验规则；规则来源、Machamp 例外和不能推断的工厂序列记录在 `RULE_SOURCES.zh-CN.md`。
- Base Set Profile 只选择非 First Edition Printing；Rare 池排除 Starter Deck 专属 Machamp，Holo/非 Holo 总权重维持样本记录的约 1:3 比例。
- Neo Genesis Profile 只选择 First Edition Printing，按来源使用 7 Common、3 Uncommon、1 Rare，并以类别权重维持 Holo 总概率约 1:3。
- `ProductRuleProfile` 可携带中英规则说明；开包界面会直接显示版本、槽位和已验证平均比例，不需要通过宝可梦专用 UI 判断 Profile。
- Neo 来源只说明 7 张 Common，未说明独立能量槽，因此当前 Common 池包含基础能量但不保证每包能量；Unlimited 与精确印刷序列继续保持未验证。
- 已知限制：引擎具备 `GuaranteeRule`，但均匀模拟配置没有历史保底、变体或真实配列；不能把“引擎支持保底”描述成“当前运行时卡包已配置保底”。
- 下一条设备切片：连接 Android 真机运行已生成的 APK 与私人内容推送脚本。当前代码主线已经进入阶段 6；EX、Sword & Shield 与 Scarlet & Violet 继续保持模拟标记，等内容安装闭环稳定后再逐套调查。

### 阶段 3：双层语言系统

状态：Application 语言核心、回退、持久化和设置界面切片已完成（2026-07-23）；全场景文本迁移和 CJK 字体待继续。

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
- 截至 2026-07-24，全量 EditMode 60/60 与 PlayMode 4/4 通过，其中包含设置场景强制切换中文和零缺字日志回归。
- 已加入约 51 KB 的可重建 Noto Sans SC 修改子集作为 TMP 全局回退，并检查当前 String Table 与代码内全部中文字符；主菜单、开包和收藏文本尚未全部迁入 String Table，后续新增中文后必须重新生成子集。

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
- 本机集成验证：5 个 manifest、796 张来源卡、12 种稀有度、1278 个印刷版本、0 个导入警告，所有图片路径存在。
- `PrivateContentImageSource` 只允许读取安装目录内的相对路径，异步返回图片字节并验证 manifest SHA-256；缺图、越界路径和损坏文件会返回可区分状态。
- `CardTextureCache` 会合并相同卡图的并发请求，以容量 32 的 LRU 释放淘汰纹理；调用方取消不会误杀其他共享请求。
- `AsyncCardImageView` 提供加载脉冲、双语失败提示和手动重试，虚拟化列表复用行时会取消并忽略过期结果，错误音效不会连续轰炸玩家。
- 收藏场景使用固定高度虚拟化 `ListView` 浏览系列和卡牌，系列行显示封面、年代、数量和内容语言；只有可见行会请求图片。
- 卡牌详情提供图片、卡号、稀有度、变体和语言，并以遵守动画速度与减少动态效果设置的淡入缩放演出显示；选择、翻卡、返回和重试均接入统一反馈。
- 自动化验收：EditMode 36/36、PlayMode 2/2；运行时验证 5 个系列，缓存数量不超过 32 且小于当前系列卡牌总量。
- 已知限制：TCGdex variant 布尔值不能完整表达所有真实印刷组合；当前展开结果是可用的模拟模型，后续可由更精确的数据覆盖。

### 阶段 5：本地游戏闭环

状态：存档、统一反馈、已安装内容浏览、两套历史规则、模拟单包闭环、收藏库存和阶段 5C 自动化均已完成；Android/IL2CPP 构建冒烟通过，真机设备验收待连接手机执行。

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
- 已完成 Base Set Unlimited 与 Neo Genesis First Edition 两份历史 Profile；当前五个本机系列中两套为历史规则，三套继续明确标记为模拟。

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
- `ProductOpeningService` 缓存已经验证的规则 Profile；真实五系列连续开 500 包/5500 张卡约 0.138 秒，托管内存净增长约 0.059 MiB。
- 1 万卡库存快照往返约 0.027 秒、JSON 约 464 KB；256 张卡图压力测试始终保持 32 张 LRU 上限。
- 三轮开包/收藏/设置场景切换约 4.4 秒，热身后托管内存净增长约 0.137 MiB，旧场景 Controller 不会残留。
- 中文 UI 使用 51 KB 修改字体子集，EditMode 会检查 String Table 与代码中文，PlayMode 强制切换中文并拒绝缺字日志。
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

状态：6A 安装前决策与本地状态适配层、6B 原子归档安装事务、6C1 下载状态与文件断点缓存、6C2 HTTP Range 传输、6C3 catalog 与协调安装闭环、6C4 玩家内容管理页面已完成（2026-07-24）；远程 provider 与卸载/重装闭环待继续。

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
- 当前正式运行时尚未配置远程 catalog 地址，因此页面会显示“尚未配置远程内容”；这不是静默假成功，下一切片必须提供 HTTPS provider 与私人 R2 配置后才进行手机真实下载。

下一切片：进入阶段 7 的最小远程闭环。先实现带超时、取消、大小上限和结构化错误的 HTTPS catalog provider，再通过非敏感配置接入一个私人 R2 fixture；随后让内容页完成真实下载、离线缓存、卸载但保留收藏、重装恢复与断网测试。不要先批量上传全部卡图。

验收：

- 网络中断不会破坏已安装内容。
- 删除系列不删除收藏记录。
- 重新安装后收藏恢复。
- 下载失败不会重复播放错误音效。

预计：4–7 小时。

### 阶段 7：私人 R2 与按需内容发布

状态：7A 受限 HTTPS catalog provider、私人运行时配置和 Bootstrap/页面真实 loopback 闭环，7B 确定性包发布器与两个历史系列本机 fixture，7C 私人 R2 安全上传工具均已完成（2026-07-24）；真实 R2 写入等待用户参数，卸载/重装和手机断网验证待继续。

目标：实现小 APK 和首装后按需下载。

电脑端：

```text
私人导入器
→ 内容转换
→ 确定性 ZIP + SHA-256
→ schema catalog
→ 上传私人 R2
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

下一切片：用户在 Cloudflare 建立只限定目标 bucket 的 Object Read & Write Token、公开只读自定义域名，并提供/在本机填写 S3 endpoint、bucket、公开 Base URL、Access Key ID、Secret Access Key。只执行 `en.base1`、`en.neo1` 与 catalog 的最小真实上传；成功后把 `remote-content.json` 安装到 Android 私人持久目录，验证首次下载、中断续传、离线重启。没有这些账号信息时不猜测 bucket、域名或写入密钥。

验收：

- APK 不包含历史卡图。
- 更新内容不重新发布 APK。
- 手机可离线使用缓存内容。

预计：5–9 小时。

### 阶段 8：宝可梦适配层

目标：在通用系统上提供不同年代和地区的宝可梦卡包。

工作内容：

- Pokémon 数据适配器。
- 区分国际版、日版和其他地区。
- 研究并记录不同年代的卡包槽位。
- 建立普通、闪卡、反向闪、V、VMAX、Illustration Rare 等规则。
- 给每条规则记录来源、日期和可信度。
- 无法确认时明确标记为模拟概率。
- 为不同年代设计相应包装、开包动画和音效主题，但共用核心系统。

验收：

- 五个测试年代使用各自规则。
- 旧系列不会使用现代稀有度逻辑。
- 玩家能看出真实规则与模拟规则的区别。

预计：10–25 小时。

### 阶段 9：Android 真机验收

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

以 2026-07-24 的当前完成度重新估算剩余工作：

- 第一个可玩的纵向切片：已完成。
- 完成本地通用模拟器 MVP：代码和自动化已完成；连接手机后的安装、内容推送、触摸、声音和震动验收预计 1–2 小时。
- 远程内容和宝可梦适配仍沿用原估算，待真机 MVP 验收后再校准。

Token 仅能估算累计工作量，平台没有固定可见的单任务总上限：

- 本地 MVP：约占整体 75%。
- 远程内容：约占整体 15%。
- 宝可梦适配与验收：约占整体 10%。
- 全项目可能累计需要约 30 万–60 万 Token，分阶段推进并依赖上下文压缩。

## 六、执行顺序与暂停条件

原先的固定阶段顺序保留为职责编号，不再作为严格施工顺序。当前关键路径改为：

```text
阶段 3：双层语言 + Application Catalog 边界（核心完成）
→ 阶段 4：卡图异步加载 + 系列/卡牌浏览（完成）
→ 阶段 5A：单卡包可玩闭环（模拟规则、Base Set 与 Neo Genesis 历史规则完成）
→ 阶段 5B：库存、新卡、搜索与筛选体验（完成）
→ 阶段 5C：设置完整性、性能、中文字体与 Android 构建（完成）
→ Android 真机私人内容、触摸、声音与震动冒烟测试（等待设备，可并行）
→ 阶段 6 内容管理页面、主线程派发与游戏反馈（完成）
→ 阶段 7A：HTTPS catalog + 私人运行时配置（完成）
→ 阶段 7B：确定性内容包发布器 + 两个历史系列本机 fixture（完成）
→ 阶段 7C：私人 R2 安全上传工具（完成；真实写入等待私人参数）
→ 当前：最小真实 R2 上传 + 手机真实下载
→ Android 下载/中断/离线缓存测试
→ 阶段 8：宝可梦真实规则适配
→ 阶段 9：最终 Android 验收
```

当前不再下载更多系列。现有五个英文系列足以验证“中文 UI + 英文卡牌内容”、图片加载、收藏和开包流程；只有本地 MVP 暴露出数据模型缺口时才补充最小测试资料。

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
