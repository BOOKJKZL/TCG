# 私人内容导入器

## 打开方式

在 Unity Editor 中选择：

```text
Tools > Gacha > Private Content Importer
```

也可以直接执行预设的五个历史验证系列：

```text
Tools > Gacha > Import Historical Sample Sets
```

预设系列：

- `base1`：Base Set，102 张
- `neo1`：Neo Genesis，111 张
- `ex1`：Ruby & Sapphire，109 张
- `swsh1`：Sword & Shield，216 张
- `sv01`：Scarlet & Violet，258 张

## 输出目录

所有导入内容写入项目根目录的 `LocalContent/Imports`。该目录已经加入 `.gitignore`，不得把卡图或原始 API 数据提交到代码仓库。

导入完成后可打开 `Tools > Universal Gacha > Private Content Publisher`，选择语言/系列并生成确定性 ZIP 与 schema catalog。`Publish All English Sets` 会把全部英文 Set 发布到 `LocalContent/Releases/android`；`Publish Generation One Site Pilot` 则生成独立的 11 包 pilot。发布目录均由 `.gitignore` 保护，且发布器会用运行时 Planner、原子安装器和私人 Catalog 完整读回后才报告成功。

```text
LocalContent/Imports/en/{set-id}/
  manifest.json
  images/
    {card-id}.webp
  raw/
    set.json
    cards/
      {card-id}.json
```

`manifest.json` 是游戏和后续 Addressables 构建器使用的标准化清单。`raw` 保存 TCGdex 原始数据，未来扩展字段时不需要再次请求 API。

每张图片记录：

- 来源 URL
- 相对路径
- 文件大小
- SHA-256

重复运行默认复用已完成的 JSON 和图片。勾选 `Refresh existing files` 才会强制重新下载。

## 当前验证结果

2026-07-13 已完成五个英文系列的首次导入：

- 796 张卡牌
- 796 张低清 JPG
- 图片合计约 104.1 MB
- 0 个下载错误
- 0 个缺失文件
- 0 个 Hash 错误
- 0 个重复卡牌 ID

真实数据包含 12 种稀有度，证明现有固定 `C/R/SR/UR` 枚举必须在下一阶段替换为字符串 ID 和可排序的 `RarityDefinition`。

2026-07-30 已完成英文全量导入与发布：218 个 Set、23,444 份卡牌 metadata、21,828 张低清 WebP；218 个运行时 ZIP 合计 350,550,171 bytes，两次完整构建的 Catalog 与包 Hash 集合相同。ZIP 只含 Manifest 实际引用的图片，不含 `raw/` 或遗留 JPG。

## 私人图鉴导入器

在 Unity Editor 中选择：

```text
Tools > Gacha > Private Pokedex Importer
```

也可使用 `PrivatePokedexImporterWindow.ImportAndAuditFromCommandLine` 执行可恢复的全量导入与审计。资料写入 `LocalContent/Pokedex`，手机与 APK 不直接请求 PokéAPI：

```text
LocalContent/Pokedex/
  import-checkpoint.json
  raw/
    lists/
    generations/
    species/
    pokemon/
    forms/
    version-groups/
  snapshot/pokemon-taxonomy.json
  pokedex-import-audit.json
```

真实快照固定 9 世代、1,025 物种、1,351 个具体变体与 1,579 个形态。重复执行默认只重新取得 3 份发现清单并复用逐资源原子缓存；勾选 `Refresh cached resources` 才强制刷新详情。形态名称缺失会进入显式 fallback 警告，Mega/Gigantamax/战斗限定按版本控制政策进入人工复核，不能被静默分类。

## 完整宝可梦发行包

卡牌、图鉴 taxonomy、卡牌关联和图鉴图片都准备完成后，可在 Unity Editor 执行：

```text
Tools > Universal Gacha > Publish Complete Pokemon Archive
```

批处理入口为 `Gacha.EditorTools.Content.PokemonCompleteReleasePublisher.PublishFromCommandLine`。输出写入 Git 忽略的 `LocalContent/Releases/android-complete`，当前 revision 4 共 537 包：218 个英文 Set、177 个日文 Set、129 个简中 Set、1 个 taxonomy、3 个按卡牌语言拆分的关联包和 9 个世代图片包。发布器会连续构建两次验证确定性，再把全部包安装到隔离内容根目录，通过正式运行时 reader 回读数量后才生成有效审计。

当前完整发行下载 1,301,893,754 bytes、安装 1,356,266,175 bytes，最大单包 27,914,532 bytes；catalog SHA-256 为 `4e01bb463c0c9b6952bb40d6417257da2c095e096932756f1918e6ad33ec9d89`，package identity SHA-256 为 `141c62be2ac30186fcf89c8dd337112e21131f72734021acfa1cc72654d1e24a`。隔离安装回读 537 份收据、499 个逻辑 Set、43,705 张逻辑卡和 53,480 个 Printing，失败为 0。远端 package id 规范化为小写并安全转义路径字符，但来源 `setId`、安装目录与玩家显示资料保持原值。电脑仍是唯一导入/写入端；APK 和手机只持有公开 catalog URL。

## 注意

- 当前全量图片选择 `low.webp`，运行时由固定的跨平台 WebP 解码器读取；旧五系列 JPG 只留在私人来源目录，不进入新 ZIP。
- 最终高清资源可改用 `high.jpg`，但不应在模型和界面稳定前批量下载。
- TCGdex 的 `variants` 记录普通、反向闪、闪卡和第一版等可用版本；它不等于真实卡包位置与概率。
- 导入器保存数据，不负责推测真实开包配列。
