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
  内容导入、字体子集、概率校验、Android/Addressables 构建与发布工具
```

当前已加入的替换点：

- `Gacha.Domain`：无 Unity 引擎依赖的通用定义、印刷身份和 Catalog 校验。
- `Gacha.Infrastructure`：读取私人 manifest，并把外部数据转换成 `UniversalCatalog`。
- `Gacha.Application`：`CatalogSession`、双层语言、体验设置、产品开启、收藏进度与资源包安装决策均不依赖 Unity。
- `Gacha.Presentation`：统一按钮动画、音效键、震动、静音、动画速度与减少动态效果。
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

## 通用数据模型现状与下一步

固定 `Card`、`Rarity` 枚举和旧抽卡服务已经退役。当前运行时使用以下数据驱动模型：

- `CollectibleItemDefinition`：ID、名称、图片、所属系列和自定义字段。
- `RarityDefinition`：字符串 ID、显示名、排序权重；不能固定为 C/R/SR/UR。
- `ProductDefinition`：卡包、礼盒或其他产品。
- `SlotRule`：每个位置从哪个卡池抽取及其权重。
- `GuaranteeRule`：N 包保底、每包至少一张、首抽奖励等规则。
- `VariantRule`：普通、闪卡、反向闪、异画等版本规则。
- `CollationRule`：需要模拟真实卡包配列时使用，而不是假设每一张都独立随机。

这些模型已经支撑五个本机系列、1278 个 Printing、模拟规则和两套历史规则。阶段 6A 已加入与平台无关的安装决策、版本/Hash 前置校验和本地收据边界；下一步是在 Infrastructure 实现可中断的原子安装事务，再接 R2/Addressables 和玩家界面。

## 两阶段路线

### 第一阶段：万能抽卡模拟器

1. 数据驱动模型、核心 asmdef、抽卡、收藏、设置与本机内容浏览已经完成。
2. 连接 Android 真机完成本地 MVP 设备验收。
3. 加入内容包管理页：可下载、暂停、删除和更新单个系列。
4. 为断网、空间不足、Hash 失败和内容升级编写测试。
5. 接入 Addressables 与私人 R2，不让托管实现渗透到 UI。

### 第二阶段：宝可梦内容适配器

1. 选择数据源和语言范围。
2. 用编辑器/桌面导入器获取元数据，转换成通用内容清单。
3. 根据真实产品建立 SlotRule、VariantRule 和配列规则。
4. 构建 Android Addressables 内容包并上传私有或公开对象存储。
5. 做逐系列下载，不让手机一次下载全部历史资源。

注意：卡牌数据库“收录了哪些卡”与“真实卡包如何配列”是两类数据。公开 API 通常提供前者，不一定提供可信的真实开包概率。
