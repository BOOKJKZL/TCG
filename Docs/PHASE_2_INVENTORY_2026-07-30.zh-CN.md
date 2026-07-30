# Phase 2B TCGdex 全量 metadata 清单审计

审计时间：2026-07-30

状态：通过，可进入 Phase 2C

本机完整报告：`LocalContent/Inventory/tcgdex-inventory.json` 与 `tcgdex-inventory.md`（被 Git 忽略，可用导入器重建）

内容 SHA-256：`5443dcd1e46babb041432f46b7da46114b1d244a23f18296753861513a3e21a5`

## 范围与方法

- 数据源使用 TCGdex v2 的只读 HTTPS GET 接口；Sets 发现接口为 `https://api.tcgdex.net/v2/{language}/sets`。
- 对 TCGdex 当前支持的 17 个语言代码读取 Set 概况；只对首批英文读取 218 个 Set 的详情和卡牌 brief。
- 图片容量使用 12 张确定性分布样本，在内存中分别读取 `high.jpg` 与 `low.webp`；没有把样本卡图写入项目、APK 或 Git。
- 盘点程序只写本机 `LocalContent/Inventory`，没有调用 Site/R2 写入接口。
- 官方接口说明：[Searching Sets](https://tcgdex.dev/rest/sets)、[REST API](https://tcgdex.dev/rest)、[Language Invalid Error](https://tcgdex.dev/errors/language-invalid)。

## 结果

17 个语言列表合计发现 1,631 个唯一 Set 条目、181,719 个语言版本卡牌记录。这个合计包含同一实体卡牌的不同语言版本，不能当成唯一卡牌数量。

| 语言 | Set | API total cards | official cards |
|---|---:|---:|---:|
| de | 153 | 20,696 | 18,193 |
| en | 218 | 23,746 | 20,729 |
| es | 154 | 18,381 | 16,022 |
| fr | 200 | 22,350 | 19,665 |
| id | 70 | 7,808 | 7,305 |
| it | 190 | 21,037 | 18,697 |
| ja | 177 | 16,192 | 14,784 |
| ko | 95 | 7,933 | 7,875 |
| nl | 3 | 228 | 228 |
| pl | 2 | 253 | 253 |
| pt | 123 | 17,058 | 15,112 |
| pt-br | 11 | 1,486 | 1,124 |
| pt-pt | 0 | 0 | 0 |
| ru | 9 | 1,049 | 1,049 |
| th | 72 | 7,569 | 7,276 |
| zh-cn | 56 | 6,953 | 6,820 |
| zh-tw | 98 | 8,980 | 8,281 |

### 英文详情

- 218/218 个 Set 详情读取成功。
- Set detail 实际列出 23,444 个卡牌条目，比列表的 `total` 少 302；导入报告必须保留这个来源差异，不能静默补齐。
- 21,828/23,444 个卡牌条目提供图片 URL，覆盖率 93.1%；1,616 个条目当前没有图片 URL。
- 当前世代 override 已映射 5 个验证 Set，剩余 213 个 Set 为 `unmapped`。Phase 2C 在全量图片下载前必须先补齐并验证英文 Set 世代/顺序表。
- TCGdex 简体中文列表重复返回 `CSV1C` 两次；盘点器已确定性保留一个条目并记录 `set-list-duplicate`，没有把来源异常隐藏掉。

### 图片容量抽样

| 规格 | 12 张平均 | 按 21,828 张有图英文卡估算 |
|---|---:|---:|
| high JPG | 666.39 KiB | 13.87 GiB |
| low WebP | 15.72 KiB | 335.07 MiB |

样本用于容量级别判断，不代表最终压缩参数。Phase 2C 会记录每张图的真实字节数和 SHA-256，再由确定性压缩管线给出最终包容量。

## 实施决定

1. 首批内容语言固定为英文；UI 继续支持中文/英文，卡牌内容语言仍与 UI 语言分离。
2. 手机首批包以低清 WebP 为默认资源，避免把约 14 GiB 高清卡图直接放进 Site 或手机；高清来源是否保留在私人存储由 Phase 2C 实测决定。
3. Phase 2C 先完成 218 个英文 Set 的世代/顺序映射，再启动 checkpoint、限速、重试和断点续跑的批量导入。
4. Site/R2 仍不在 Phase 2C 写入；只有本地 Hash、失败队列和确定性包通过后，Phase 2D 才执行发布与远端回读。

## 自验证

- `TcgdexInventoryServiceTests`：5/5 通过。
- 完整 EditMode：253/253 通过。
- 完整 PlayMode：7/7 通过。
- `Assembly-CSharp-Editor.csproj` 编译：0 error；现有 Unity/System.Net.Http 版本与旧测试警告未增加错误。
- 实现提交：`8d1c938 feat(importer): add all-set inventory discovery`。
