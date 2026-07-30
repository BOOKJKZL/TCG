# Phase 2 最终验收收据

日期：2026-07-30

结论：第二大阶段 Phase 2A–2J 已完成，软件范围验收为 100%。

## 发行内容

- 229 个内容包：218 个英文 Set、1 个 Pokémon taxonomy、1 个卡牌关联、9 个世代图鉴图片包。
- 下载总量：548,304,599 bytes；安装总量：578,905,470 bytes；最大单包：27,914,532 bytes。
- 运行时离线回读：218 Set、23,444 卡、32,426 Printing、1,025 物种、1,579 形态、23,444 卡牌关联、1,571 图鉴图。
- Catalog SHA-256：`9ca7c1f8d876c4f6d32c67eb7dbfce089c8e1d27c0c421d4e4d0d1eb7d8e249d`。
- 包身份集合 SHA-256：`58c0ed676870ec9270d5fb2469710bee25c193930523be23c9f6954a1aeaed38`。
- 相同输入连续构建两次结果一致；隔离安装收据 229/229，失败 0。

## 图鉴与卡牌关联

- 9 世代、全国图鉴 #001–#1025；第一世代严格为 #001–#151。
- 23,444 张卡关联状态总和无缺口：515 `matched-form`、18,994 `matched-species`、126 `multi-species`、3,574 `not-applicable`、235 `needs-review`。
- 1,571 张 PNG 已按世代打包；8 个来源明确缺图，没有伪造替代图片。
- 图鉴支持中英双语、世代/全国编号排序、搜索、详情、地区形态双向跳转、当前形态/全部同种卡牌范围、稳定排序、按需下载状态与现有收藏详情跳转。
- 列表与卡牌画廊虚拟化；图鉴图和卡图使用有界缓存；未安装、失败、离线和减少动态效果状态均有明确反馈。

## 远端 Site

- 地址：`https://universal-gacha-content.jiejingleek.chatgpt.site/api/content/catalog.json`。
- 发布结果：206 个新对象上传、23 个对象复用；catalog 在全部对象通过 origin 与公开完整 SHA-256 回读后最后切换。
- 独立匿名审计：229/229 HEAD、229/229 中点 Range `206` 与精确 `Content-Range` 全通过。
- Catalog 与一个 ZIP 的 `POST/PUT/PATCH/DELETE` 共 8/8 返回 `405`；审计未使用 Authorization。
- 电脑发布令牌只保存在 Git 忽略的本机凭据；APK、手机配置、catalog、日志与测试收据不含明文令牌或 owner 邮箱。
- 可重跑工具：`Tools/Validation/audit_public_content_release.ps1`；本机 JSON 收据位于 Git 忽略的完整发行目录。

## 自动测试与 Site 工程

- Unity EditMode：299/299 通过，失败 0。
- Unity PlayMode：8/8 通过，失败 0。
- Site：TypeScript typecheck、vinext 生产构建、19/19 Node 测试与 ESLint 全通过。
- 共享内容根目录回归：卡牌 reader 只扫描 `<language>/<set>/manifest.json`，不会把 `pokedex/artwork/.../manifest.json` 当成卡包；定向测试通过。

## Android 平台

- 产物：`Builds/Android/UniversalGachaSimulator-smoke.apk`。
- 大小：52,607,315 bytes；SHA-256：`cc0028aa221820427cb165fc0fc52ed9613003169344dec95e8398d8ac710676`。
- 6 个场景；418 个 ZIP 条目；全部原生库仅为 `arm64-v8a`。
- `apksigner` 与 `zipalign -c -v 4` 通过；使用个人测试用途 Debug 证书，不宣称为商店 release 签名。
- 权限只有 `INTERNET`、`VIBRATE` 与 Android 自动生成的应用内部 receiver 权限；意外权限 0。
- APK 私人资源/配置名称匹配 0，没有打包 548 MB 卡图、图鉴图、发布令牌或 `remote-content.json`。
- Android 14 `emulator-5554` 声明 `x86_64,arm64-v8a`，同一生产 ARM64 APK 直接安装成功；应用进程处于前台，公开只读配置已安装，敏感字段匹配为 false。
- 既有 14 项 Android 模拟器软件收据继续有效；实体震动手感、实体扬声器音质与真实蜂窝切换仍诚实保留为实体机体验补测。

## 最终判定

使用本阶段测试结果、229 包 catalog、公开 Site、最终 APK 与 Android 14 收据执行：

```powershell
./Tools/Validation/project_completion_audit.ps1 `
  -EditModeResults TestResults/phase2-final-editmode.xml `
  -PlayModeResults TestResults/phase2-final-playmode.xml `
  -ReleaseCatalog LocalContent/Releases/android-complete/catalog.json `
  -RequireComplete
```

结果：`PROJECT COMPLETION VERIFIED: 100%`。

独立 Cloudflare R2 迁移、额外卡牌语言、更多真实历史配列和实体硬件体验属于后续可选范围，不作为 Phase 2 未完成项。
