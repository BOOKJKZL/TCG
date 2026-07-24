# 通用抽卡模拟器架构

## 目标

核心系统只理解“可收集物、产品、抽取规则、库存和内容来源”，不理解宝可梦、角色战斗数值或某一家卡牌游戏。宝可梦应当是第二阶段接入的内容包和数据适配器。

## 模块边界

当前核心已经整理为以下四个运行时程序集；旧场景适配脚本暂留在 `Assembly-CSharp`，但通过接口调用核心，不允许 Presentation 反向依赖 Infrastructure。

```text
Gacha.Domain
  CollectibleItem / Set / Product / SlotRule / GuaranteeRule
  GachaEngine / Inventory / 概率与保底规则
  不依赖 UI、Cloud Save、Addressables

Gacha.Application
  Catalog/语言/体验设置状态、开包用例、收藏进度、内容安装接口

Gacha.Infrastructure
  ResourcesCatalog / AddressablesCatalog / LocalSave / UnityCloudSave
  将外部 API 数据转换为领域模型的 Adapter

Gacha.Presentation
  UI Toolkit / UGUI 控制器、下载进度、开包动画、收藏界面

Gacha.Editor
  内容导入、确定性 ZIP/catalog 发布、字体子集、概率校验、Android/Addressables 构建工具
```

当前已加入的替换点：

- `Gacha.Domain`：无 Unity 引擎依赖的通用定义、印刷身份和 Catalog 校验。
- `Gacha.Infrastructure`：读取私人 manifest，并把外部数据转换成 `UniversalCatalog`。
- `Gacha.Application`：`CatalogSession`、双层语言、体验设置、产品开启、收藏进度与资源包安装决策均不依赖 Unity。
- `Gacha.Presentation`：统一按钮动画、音效键、震动、静音、动画速度、减少动态效果，以及只观察 Application 快照的内容管理页面。
- 旧 `Card`、`CardDatabase`、`PackDefinition`、固定 `Rarity` enum 和 `GachaService` 已退役，不再保留兼容层。

- `ICatalogProvider`：Catalog 可由私人 manifest、Addressables 或下载后的数据库提供。
- `IContentImageSource`：卡图可由本机目录、Addressables 或远程缓存提供。
- `IProductRuleProvider`：每个产品可替换模拟或历史配列 Profile。
- `IInventoryProgressStore` / `ICollectionProgressStore`：Application 用例不依赖 Inventory 单例。
- `IExperienceSettingsStore` / `ILanguagePreferenceStore`：玩家设置的业务状态与 PlayerPrefs 分离。
- `IGachaRandomSource`：可注入固定随机序列进行自动测试。
- `IInventoryConflictResolver`：本地与云端冲突策略可替换。
- `IContentDeliveryService`：资源托管商不会渗透到 UI 和业务代码。
- `IInstalledContentPackageRegistry`：已安装版本和整包 Hash 由可替换收据存储提供。
- `IContentStorageProbe`：安装决策只读取可用字节，不依赖 Android 或桌面存储 API。
- `IContentPackageInstaller`：Application 只接收结构化安装结果；ZIP、staging、目录替换、收据和回滚细节留在 Infrastructure。
- `IContentPackageTransfer` / `IContentPackageByteSource`：Application 管理暂停、重试和失败事件；Infrastructure 管理 `.part/.zip` 与本机或 HTTP 字节流。
- `ContentPackageCatalog` / `IContentPackageInstallCoordinatorFactory`：版本化清单同时充当严格 URI resolver；Presentation 通过工厂取得单包协调器，不直接排列规划、传输和安装调用。
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

这些模型已经支撑五个本机系列、1278 个 Printing、模拟规则和两套历史规则。阶段 6A–6C4 已加入安装决策、安全路径、本地收据、可回滚 ZIP 安装、下载状态机、文件断点缓存、严格 HTTP Range、版本化 catalog、协调器，以及带动画、音效、本地化和主线程派发的玩家内容管理页面；阶段 7A 已加入受限 HTTPS catalog 与私人配置，7B 已加入确定性 ZIP/catalog 发布和运行时安装自验证，7C 已加入凭据仅存电脑端、ZIP-first/catalog-last、origin/公开 URL 双重校验的私人 R2 上传边界。下一步是带真实私人参数的 R2/Android 远程闭环。

## 两阶段路线

### 第一阶段：万能抽卡模拟器

1. 数据驱动模型、核心 asmdef、抽卡、收藏、设置与本机内容浏览已经完成。
2. 连接 Android 真机完成本地 MVP 设备验收。
3. 内容包管理页已支持下载、暂停、取消、重试、修复和更新；卸载/重装仍待远程闭环验证。
4. 为远程断网、空间不足、Hash 失败、卸载保留收藏和内容升级补齐测试。
5. 接入 HTTPS catalog、确定性 ZIP 与私人 R2；Addressables 只用于确实需要 Unity AssetBundle 的共用资源，不让托管实现渗透到 UI。

### 第二阶段：宝可梦内容适配器

1. 选择数据源和语言范围。
2. 用编辑器/桌面导入器获取元数据，转换成通用内容清单。
3. 根据真实产品建立 SlotRule、VariantRule 和配列规则。
4. 构建 Android Addressables 内容包并上传私有或公开对象存储。
5. 做逐系列下载，不让手机一次下载全部历史资源。

注意：卡牌数据库“收录了哪些卡”与“真实卡包如何配列”是两类数据。公开 API 通常提供前者，不一定提供可信的真实开包概率。
