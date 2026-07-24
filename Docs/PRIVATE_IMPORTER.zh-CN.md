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

导入完成后可打开 `Tools > Universal Gacha > Private Content Publisher`，选择语言/系列并生成确定性 ZIP 与 schema catalog。发布输出位于 `LocalContent/Releases/android`，同样由 `.gitignore` 保护；发布器会用运行时 Planner、原子安装器和私人 Catalog 完整读回后才报告成功。当前 `Publish Base + Neo Fixtures` 批处理只用于最小远程闭环，不会自动发布其余三个系列。

```text
LocalContent/Imports/en/{set-id}/
  manifest.json
  images/
    {card-id}.jpg
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

## 注意

- 当前图片选择 `low.jpg`，用于开发和手机 UI 验证。
- 最终高清资源可改用 `high.jpg`，但不应在模型和界面稳定前批量下载。
- TCGdex 的 `variants` 记录普通、反向闪、闪卡和第一版等可用版本；它不等于真实卡包位置与概率。
- 导入器保存数据，不负责推测真实开包配列。
