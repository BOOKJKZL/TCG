# 交互卡包视觉资源契约

交互开包保留 `ProductOpeningService.OpenBatch` 作为唯一抽取与库存提交边界。旋转、选包、单双指撕包都只属于 Presentation；手势成功前不得抽取，提交成功后展示失败也不得再次抽取。

## 当前资源

- `ProductOpeningTheme.PackPresentation` 描述正面、背面、封口高度、背缝宽度与撕开阈值。
- 现有五个时代主题继续复用 `Resources/Gacha/Themes/*-pack.png`；未提供背面时使用同图作为安全回退。
- 图片必须是原创或已获授权内容，不得含官方卡包商标或受限素材。
- 移动端导入沿用现有卡包图约束：最大 512、关闭 mipmap、Clamp、Android ASTC 6x6。

## 后续远端内容

远端卡包正反面不得直接接受任意文件路径。若扩展 private manifest，应为每个可选图片提供包内相对路径和 SHA-256，并走既有内容根目录、完整性校验、大小上限与 leased texture 生命周期。加载失败只回退通用包装；绝对路径、URL、hash 与底层异常不得进入玩家 UI。
