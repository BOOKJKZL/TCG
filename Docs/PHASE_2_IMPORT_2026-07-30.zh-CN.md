# Phase 2C 英文全量导入与完整性审计

日期：2026-07-30

状态：通过，可进入 Phase 2D

## 一、导入范围

- 内容语言：英文（`en`）。
- 资料源：Phase 2B 固定的 TCGdex inventory snapshot。
- Set：218/218 已映射、已导入、checkpoint 为 `completed`。
- 卡牌记录：23,444/23,444 已处理。
- 图片：21,828 张低清 WebP，345,786,690 bytes。
- 无图片来源记录：1,616；这些记录没有来源图片 URL，Manifest 保留 metadata 与明确空图片引用。
- 失败卡、失败 Set、孤儿图片、`.download` 临时文件：全部为 0。

本机 `LocalContent/` 为 Git 忽略的私人工作目录，不进入 APK 或 Git。完整导入目录包含来源快照、Manifest、WebP、checkpoint 与报告，共 543,734,046 bytes；Phase 2D 的手机包只选择运行时所需 metadata/图片，不打包原始来源 JSON。

## 二、可恢复执行证据

第一次真实批量执行完成 217 个 Set。监视脚本在 Windows 上读取 checkpoint 时没有允许删除共享，导致 `base4` 的完成态原子替换短暂遇到 sharing violation；其 130 张卡和图片已经全部落盘，资料没有损坏。

修复后：

- checkpoint 原子替换会对瞬时 `IOException` 重试。
- 已完成 Set 在恢复时直接跳过，不重新枚举或下载。
- 第二次执行跳过 217 个 Set，只恢复 `base4`。
- `base4` 复用 130 份 metadata 与 130 张图片，网络重复下载为 0。
- 最终 checkpoint：218 个 Set、23,444 个 processed、0 failed card、0 failure record。

自动化还覆盖可重试 HTTP 失败、永久失败隔离、逐卡中断恢复、重复执行复用和完成 Set 快速跳过。

## 三、完整性审计

`bulk-import-audit.json` 的最终结果：

| 项目 | 结果 |
|---|---:|
| `IsValid` | `true` |
| Manifest / Set | 218 |
| 卡牌记录 | 23,444 |
| 原始卡牌 JSON | 23,444 |
| WebP 图片 | 21,828 |
| 无图片引用 | 1,616 |
| 图片 bytes | 345,786,690 |
| 孤儿图片 | 0 |
| 临时下载文件 | 0 |
| 审计失败 | 0 |

审计逐项验证 Manifest schema/排序、来源 JSON 存在性、图片声明长度与 SHA-256、安全相对路径、跨 Set 重复卡牌 ID、孤儿文件和未完成下载。损坏 Hash、路径逃逸、重复 ID 与临时文件都有反例测试。

## 四、运行时回读

218 个 Manifest 已由现有 adapter 组成完整 `UniversalCatalog`。玩家收藏与抽卡列表在大资料库下继续使用虚拟化；测试不再依赖列表第一项或固定五 Set，而是按稳定 Set ID 选择研究样本。

低清资源使用 WebP 以避免约十倍的低清 JPG 容量。Unity 内建 `ImageConversion.LoadImage` 不能承担这条格式边界，因此运行时固定接入 `unity.webp` `0.3.22`（锁定 commit `9818db6`），同时包含 Editor/Windows 与 Android 解码库。单元测试实际生成并解码 WebP；PlayMode 实际读取本机导入卡图，纹理缓存仍不超过 32 张。

## 五、自验证与提交

- WebP 定向 EditMode：5/5。
- 完整 EditMode：268/268。
- 完整 PlayMode：7/7。
- 导入完整性：有效，失败 0。
- APK：未重建；本阶段没有改变权限、ABI、储存路径或图形设置。Android WebP 原生库留到 Phase 2J 的平台验收。

相关提交：

- `e789f3b feat(importer): map all english pokemon sets`
- `5049056 feat(importer): add resumable bulk card import`
- `4953d79 fix(importer): retry checkpoint replacement`
- `72e14ee perf(importer): skip completed sets on resume`
- `fe29104 feat(importer): audit imported content integrity`
- `e4e7984 test(content): support full installed catalog`
- `6302a7a feat(content): decode imported WebP card images`
- `ef72f14 test(playmode): support full content catalog`

## 六、Phase 2D 入口条件

Phase 2C 的本机导入与运行时回读已关闭。Phase 2D 从相同 Manifest 生成按语言/Set 拆分的确定性不可变包，先做连续构建 Hash 与正式安装器离线回读，再执行 Site pilot 和容量门槛判断；公开 Catalog 永远最后更新。
