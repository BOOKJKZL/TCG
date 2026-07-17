# 通用抽卡模拟器架构

## 目标

核心系统只理解“可收集物、产品、抽取规则、库存和内容来源”，不理解宝可梦、角色战斗数值或某一家卡牌游戏。宝可梦应当是第二阶段接入的内容包和数据适配器。

## 模块边界

建议最终整理为以下程序集；当前代码已经先通过接口建立边界，后续再迁移目录和加入 asmdef，避免一次移动大量场景脚本。

```text
Gacha.Domain
  CollectibleItem / Set / Product / SlotRule / GuaranteeRule
  GachaEngine / Inventory / 概率与保底规则
  不依赖 UI、Cloud Save、Addressables

Gacha.Application
  启动流程、开包用例、收藏查询、存档协调、内容安装用例

Gacha.Infrastructure
  ResourcesCatalog / AddressablesCatalog / LocalSave / UnityCloudSave
  将外部 API 数据转换为领域模型的 Adapter

Gacha.Presentation
  UI Toolkit / UGUI 控制器、下载进度、开包动画、收藏界面

Gacha.Editor
  内容导入、概率校验、Addressables 构建与发布工具
```

当前已加入的替换点：

- `Gacha.Domain`：无 Unity 引擎依赖的通用定义、印刷身份和 Catalog 校验。
- `Gacha.Infrastructure`：读取私人 manifest，并把外部数据转换成 `UniversalCatalog`。
- `Gacha.Presentation`：统一按钮动画、音效键、震动和减少动态效果设置。
- 旧 `Card`、`CardDatabase`、`PackDefinition`、固定 `Rarity` enum 和 `GachaService` 已退役，不再保留兼容层。

- `ICardCatalog`：卡牌来源可由 Resources、Addressables 或下载后的数据库实现。
- `ICardInventory`：抽卡规则不再依赖 Inventory 单例。
- `IGachaRandom`：可注入固定随机序列进行自动测试。
- `IInventoryConflictResolver`：本地与云端冲突策略可替换。
- `IContentDeliveryService`：资源托管商不会渗透到 UI 和业务代码。

## 通用数据模型下一步

现有 `Card` 和 `Rarity` 枚举暂时保留，以兼容已经创建的 Unity 资产。成为真正的万能模拟器前，需要逐步迁移到数据驱动模型：

- `CollectibleItemDefinition`：ID、名称、图片、所属系列和自定义字段。
- `RarityDefinition`：字符串 ID、显示名、排序权重；不能固定为 C/R/SR/UR。
- `ProductDefinition`：卡包、礼盒或其他产品。
- `SlotRule`：每个位置从哪个卡池抽取及其权重。
- `GuaranteeRule`：N 包保底、每包至少一张、首抽奖励等规则。
- `VariantRule`：普通、闪卡、反向闪、异画等版本规则。
- `CollationRule`：需要模拟真实卡包配列时使用，而不是假设每一张都独立随机。

这样宝可梦、游戏王或原创卡池都只需要提供数据和规则，不需要复制 `GachaService`。

## 两阶段路线

### 第一阶段：万能抽卡模拟器

1. 完成上述数据驱动模型并提供旧资产迁移工具。
2. 接通抽卡和收藏 UI，显示概率、保底进度与重复数量。
3. 加入内容包管理页：可下载、暂停、删除和更新单个系列。
4. 为概率、保底、空卡池、存档迁移和断网启动编写测试。
5. 将 Core、Infrastructure、Presentation 分成 asmdef。

### 第二阶段：宝可梦内容适配器

1. 选择数据源和语言范围。
2. 用编辑器/桌面导入器获取元数据，转换成通用内容清单。
3. 根据真实产品建立 SlotRule、VariantRule 和配列规则。
4. 构建 Android Addressables 内容包并上传私有或公开对象存储。
5. 做逐系列下载，不让手机一次下载全部历史资源。

注意：卡牌数据库“收录了哪些卡”与“真实卡包如何配列”是两类数据。公开 API 通常提供前者，不一定提供可信的真实开包概率。
