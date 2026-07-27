# 通用抽卡模拟器架构

## 目标

核心系统只理解“可收集物、产品、抽取规则、库存和内容来源”，不理解宝可梦、角色战斗数值或某一家卡牌游戏。宝可梦应当是第二阶段接入的内容包和数据适配器。

## 模块边界

当前核心已经整理为以下五个运行时程序集；旧场景适配脚本与组合根暂留在 `Assembly-CSharp`，但通过接口调用核心，不允许 Presentation 反向依赖 Infrastructure。

```text
Gacha.Domain
  CollectibleItem / Set / Product / SlotRule / GuaranteeRule
  GachaEngine / Inventory / 概率与保底规则
  不依赖 UI、Cloud Save、Addressables

Gacha.Application
  Catalog/语言/体验设置状态、开包用例、规则证据 Profile、收藏进度、内容安装与卸载接口

Gacha.Infrastructure
  ResourcesCatalog / AddressablesCatalog / LocalSave / UnityCloudSave
  将外部 API 数据转换为领域模型的 Adapter

Gacha.Presentation
  通用反馈、本地化、产品开包主题/粒子契约与 UI 状态

Gacha.Pokemon.Presentation
  宝可梦系列到通用开包主题的映射
  只依赖 Domain + Presentation，不进入通用业务模块

Assembly-CSharp / Editor-only code
  场景控制器、启动组合根
  内容导入、确定性 ZIP/catalog 发布、字体子集、概率校验、Android/Addressables 构建工具

Cloud/TCGContentSite（独立部署模块）
  Sites R2 内容适配器、owner-only 发布台、公开 catalog/Range API
  不依赖 Unity；未来可整体替换为独立 Cloudflare R2
```

当前已加入的替换点：

- `Gacha.Domain`：无 Unity 引擎依赖的通用定义、印刷身份和 Catalog 校验。
- `Gacha.Infrastructure`：读取私人 manifest，并把外部数据转换成 `UniversalCatalog`。
- `Gacha.Application`：`CatalogSession`、双层语言、体验设置、产品开启、收藏进度与资源包安装决策均不依赖 Unity。
- `Gacha.Presentation`：统一按钮动画、音效键、震动、静音、动画速度、减少动态效果、`CardUiText` String Table 解析与英文兜底，以及只观察 Application 快照的内容管理页面；收藏、共享卡图状态和开包流程共用该文本边界，并能在当前页面状态中即时刷新语言。`LegacySceneTextLocalizer` 通过场景级映射接管仍使用 TMP/UGUI 的静态标题和菜单入口，避免把本地化职责重新塞回导航控制器。
- `IProductOpeningThemeProvider`：通用 Presentation 只定义主题 ID、USS class、音效键、动画参数和稀有强调判断；`Gacha.Pokemon.Presentation` 负责五个宝可梦系列的具体映射，`GameApplicationBootstrap` 只在组合根注入实现。未知游戏或未知产品使用已验证参数范围内的通用后备主题。
- `ProductOpeningParticleTheme` / `ThemeParticleField`：主题数据只声明经过范围验证的环境与爆发参数；运行时粒子场预建最多 12 个 `VisualElement` 并循环复用，以约 30 FPS 驱动漂浮和径向爆发。控制器只按开包状态启动/停止，不识别宝可梦系列；减少动态效果开启时不会启动调度器，并继续保留静态包装和稀有光环。
- `ProductOpeningTheme.PackArtworkResourcePath` 是可选的轻量包装皮肤引用；控制器只读取主题声明并通过 `Resources` 加载，加载失败时回退到原有系列卡图，不根据宝可梦系列 ID 分支。当前五张核心皮肤的 APK 总增量约 0.29 MiB，大量卡图仍属于安装后下载内容；若主题数量显著增长，再把包装资产迁入内容包。
- `ThemeArtworkImportProcessor` 只处理 `Assets/Resources/Gacha/Themes/`，固定最长边 512px、无 mipmap、Clamp 和 Android ASTC 6×6，避免源图分辨率或人工导入设置扩大移动端包体。
- 主题音频沿用 `UIFeedbackService` 的语义事件，但允许把实际播放键替换为产品主题键；因此统计/震动仍识别 `PackOpen` 与 `RareReveal`。`ThemeAudioAssetGenerator` 以固定参数和种子烘焙五套撕包/稀有揭晓 WAV，`AudioClipConfig` 在 `AudioManager.Awake` 时先注册正式资源，程序化音只补足缺失键。所有主题 WAV 使用单声道 44.1 kHz、ADPCM、`DecompressOnLoad` 和预加载；播放源明确为 2D。
- 当前 TCGdex 运行时导入没有填充 `RarityDefinition.PresentationKey`；宝可梦主题在自身适配层用可配置的稀有度 ID 片段补足强调判断，避免把 `rare`、V、VMAX 或 Illustration Rare 写死进通用控制器。
- 旧 `Card`、`CardDatabase`、`PackDefinition`、固定 `Rarity` enum 和 `GachaService` 已退役，不再保留兼容层。

- `ICatalogProvider`：Catalog 可由私人 manifest、Addressables 或下载后的数据库提供。
- `IContentImageSource`：卡图可由本机目录、Addressables 或远程缓存提供。
- `IProductRuleProvider`：每个产品可替换纯模拟、来源辅助模拟或历史配列 Profile；Profile 明确携带地区、可信度、核验日期与证据，不由 UI 猜测。
- `IInventoryProgressStore` / `ICollectionProgressStore`：Application 用例不依赖 Inventory 单例。
- `IExperienceSettingsStore` / `ILanguagePreferenceStore`：玩家设置的业务状态与 PlayerPrefs 分离。
- `IGachaRandomSource`：可注入固定随机序列进行自动测试。
- `IInventoryConflictResolver`：本地与云端冲突策略可替换。
- `IContentDeliveryService`：资源托管商不会渗透到 UI 和业务代码。
- `IInstalledContentPackageRegistry`：已安装版本和整包 Hash 由可替换收据存储提供。
- `IContentStorageProbe`：安装决策只读取可用字节，不依赖 Android 或桌面存储 API。
- `IContentPackageInstaller`：Application 只接收结构化安装结果；ZIP、staging、目录替换、收据和回滚细节留在 Infrastructure。
- `IContentPackageLifecycleService`：Application 只表达查找与卸载结果；Infrastructure 只删除收据登记的内容，并用同卷事务保证失败可恢复且不接触玩家存档。
- `IContentPackageTransfer` / `IContentPackageByteSource`：Application 管理暂停、重试和失败事件；Infrastructure 管理 `.part/.zip` 与本机或 HTTP 字节流。
- `ContentPackageCatalog` / `IContentPackageInstallCoordinatorFactory`：版本化清单同时充当严格 URI resolver；Presentation 通过工厂取得单包协调器，不直接排列规划、传输和安装调用。
- `CachedContentPackageCatalogProvider`：Infrastructure 只持久化已通过正式 reader 的 catalog，并绑定配置来源、限制大小、原子替换；Application 结果明确标记是否使用离线缓存，Presentation 只显示本地化警告。
- `IUiThreadDispatcher` / `ContentPackageOperationUiBridge`：后台协调器事件统一切回 Unity 主线程；页面销毁后不会继续更新已失效的 VisualElement。

## 通用数据模型现状与下一步

固定 `Card`、`Rarity` 枚举和旧抽卡服务已经退役。当前运行时使用以下数据驱动模型：

- `CollectibleItemDefinition`：ID、名称、图片、所属系列和自定义字段。
- `RarityDefinition`：字符串 ID、显示名、排序权重；不能固定为 C/R/SR/UR。
- `ProductDefinition`：卡包、礼盒或其他产品。
- `SlotRule`：每个位置从哪个卡池抽取及其权重。
- `GuaranteeRule`：N 包保底、每包至少一张、首抽奖励等规则。
- `VariantRule`：普通、闪卡、反向闪、异画等版本规则。
- `CollationRule`：需要模拟真实卡包配列时使用，而不是假设每一张都独立随机。

这些模型已经支撑五个本机系列、1462 个运行时 Printing、三套历史规则和两套来源指导模拟。阶段 6A–6C5 已加入安装决策、安全路径、本地收据、可回滚 ZIP 安装/卸载、下载状态机、文件断点缓存、严格 HTTP Range、版本化 catalog、协调器，以及带动画、音效、本地化和主线程派发的玩家内容管理页面；卸载只触及收据登记内容，收藏存档保持独立，并允许同页重装。阶段 7A 已加入受限 HTTPS catalog 与私人配置，7B 已加入确定性 ZIP/catalog 发布和运行时安装自验证，7C 已加入私人 R2 上传边界，7D 已加入来源绑定的已验证 catalog 离线缓存和跨重启续传，7E 已加入不依赖 Unity 的临时 Site R2 中继并用两个真实卡包验证同一网络契约。阶段 8A 已将规则来源升级为 Application 证据模型；8B 已让五个系列分别使用可替换规则、独立主题、原创包装、有界粒子与正式烘焙音效。下一步是 Site 公网部署与 Android 真机验收，独立 Cloudflare R2 延后为存储适配器迁移。

## 两阶段路线

### 第一阶段：万能抽卡模拟器

1. 数据驱动模型、核心 asmdef、抽卡、收藏、设置与本机内容浏览已经完成。
2. 连接 Android 真机完成本地 MVP 设备验收。
3. 内容包管理页已支持下载、暂停、取消、重试、修复、更新、安全卸载和同页重装；收藏隔离已由真实文件 fixture 验证。
4. 首次下载、中断续传和断网重启的本机自动化已完成；连接公开 Site 与 Android 真机补齐设备验收。
5. HTTPS catalog、确定性 ZIP 与 Site R2 中继已接入；未来迁移独立 Cloudflare R2 只替换托管适配器。Addressables 只用于确实需要 Unity AssetBundle 的共用资源。

### 第二阶段：宝可梦内容适配器

1. 选择数据源和语言范围。
2. 用编辑器/桌面导入器获取元数据，转换成通用内容清单。
3. 根据真实产品建立 SlotRule、VariantRule 和配列规则。
4. 构建 Android Addressables 内容包并上传私有或公开对象存储。
5. 做逐系列下载，不让手机一次下载全部历史资源。

注意：卡牌数据库“收录了哪些卡”与“真实卡包如何配列”是两类数据。公开 API 通常提供前者，不一定提供可信的真实开包概率。
