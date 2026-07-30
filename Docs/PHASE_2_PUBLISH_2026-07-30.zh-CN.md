# Phase 2D 确定性打包与 Site pilot 审计

日期：2026-07-30

状态：通过，可进入 Phase 2E

## 一、运行时包边界

每个 `en.{setId}` 是一个可独立安装、更新、修复和卸载的内容包，安装到 `en/{setId}`。发布器不再复制整个私人导入目录，而是从 Manifest 建立显式清单：

- 必须包含 `manifest.json`。
- 只包含各卡记录实际引用且位于 `images/` 下的图片。
- 不包含 `raw/`、checkpoint、审计报告、旧 JPG 或未引用图片。
- 显式文件仍执行路径逃逸、目录链接、重复便携路径、缺失文件和空包检查。

这让手机包保留运行时所需的完整 metadata 与卡图，同时让私人来源快照只留在电脑。

## 二、218 包本机发布

| 项目 | 结果 |
|---|---:|
| Catalog schema / revision | 1 / 2 |
| Catalog bytes | 84,420 |
| 内容包 | 218 |
| ZIP 下载 bytes | 350,550,171 |
| 安装 bytes | 365,127,653 |
| ZIP 文件条目 | 22,046 |
| Manifest | 218 |
| WebP | 21,828 |
| 最大单包 | `en.B1` / 5,941,742 bytes |
| 发布/验证临时目录残留 | 0 / 0 |
| 独立审计失败 | 0 |

所有包都经过正式 `ContentPackagePlanner`、原子 ZIP 安装器、安装收据与 `PrivateContentCatalogProvider` 回读。独立 PowerShell 审计没有复用发布器的判断：它重新计算每个 ZIP SHA-256、实际解压长度与 Manifest 图片集合，并拒绝任何非 Manifest/WebP 条目。

发布目录保留两个旧 Base/Neo 内容寻址归档，但 revision 2 Catalog 不引用它们；这符合不可变归档策略，不会被上传或安装。

## 三、确定性证据

相同 218 Set 输入完整发布两次；两次都重新压缩、重新执行 218 包安装回读，而不是只复用先前成功状态：

- Catalog SHA-256：`76560e4e143c3edfa4c68a0e3be0069ddf21925f3590c0d4d9df2bda9fb58c5f`。
- 按稳定 package ID 串接的 218 包 Hash 集合 SHA-256：`9ad09159564fbbe77e5001ecb88baff8b08a06ead13360c2bd64a75a36db3769`。
- 两次 Catalog 与包集合均完全相同。
- 两次 `.publishing-*` 与 `.verification-*` 残留均为 0。

## 四、第一世代 Site pilot

Pilot 固定选择映射为 `generation-1` 的 11 个英文 Set：

`base1`、`base2`、`base3`、`base4`、`base5`、`basep`、`gym1`、`gym2`、`jumbo`、`miscp`、`wp`。

| 项目 | 结果 |
|---|---:|
| Catalog revision / version | 2 / `2.0.0-pilot.1` |
| Catalog bytes | 4,397 |
| Catalog SHA-256 | `c9f768e05d9e6a664d71a0900df6d78fcdf38a15b1845729fbe0a16418725384` |
| 包数 | 11 |
| 下载 bytes | 10,916,516 |
| 安装 bytes | 11,388,535 |
| 最大包 | 1,940,883 bytes |
| 新上传 / 复用 | 11 / 0 |

真实发布地址为 `https://universal-gacha-content.jiejingleek.chatgpt.site/api/content/catalog.json`。发布顺序为不可变 ZIP → 服务端检查 → 公网完整 Hash 回读 → Catalog 上传 → Catalog 服务端/公网 Hash 回读 → 原子写入本机运行配置。

独立于 Publisher 的公网复核：

- 11/11 ZIP 完整下载 bytes 与 SHA-256 正确。
- 11/11 ZIP 从中点请求得到 `206`，长度和 `Content-Range` 精确。
- 公开 Catalog 与本机 4,397 bytes / SHA-256 完全相同。
- Catalog 与一个真实 ZIP 的 `POST`、`PUT`、`PATCH`、`DELETE` 共 8 项全部为 `405 Allow: GET, HEAD`。

## 五、凭据与容量边界

- 1,219 个 Git 跟踪文件中，本机发布令牌原文匹配为 0。
- `remote-content.json` 与 pilot Catalog 中令牌匹配为 0。
- 游戏配置只有 `catalogUrl`、`timeoutSeconds`、`maxCatalogBytes`。
- Site 单包上限为 100 MiB，当前最大全量包约 5.67 MiB。
- Site Catalog 上限为 1 MiB，当前 218 包 Catalog 约 82.44 KiB。

Pilot 证明接口、对象写入、公开读取、Range 和权限边界可用，但不凭空声称未知的 Site 总储存配额。Phase 2J 会先尝试发布最终全量；若服务器返回真实容量阻塞，则使用现有同 Catalog 契约的 Cloudflare R2 target，不改 package ID、安装收据或收藏。

## 六、自验证

- Publisher 定向 EditMode：8/8。
- Site：typecheck、生产构建、页面/内容/owner/凭据测试 19/19、lint 全通过。
- 完整 Unity EditMode：270/270。
- 完整 Unity PlayMode：7/7。
- APK：未重建；本阶段是 Editor 发布工具与远端资料变化，没有改变 APK 平台边界。

实现提交：

- `7d3cbf7 feat(publisher): package only runtime card content`
- `34e9481 feat(publisher): add generation one site pilot`

下一步进入 Phase 2E：从 PokéAPI 生成版本化 generation/species/form snapshot，并先用 #001–#151 与地区形态关系关闭资料契约。
