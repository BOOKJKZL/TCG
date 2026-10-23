# Core Neutral UI 实施与验证

日期：2026-10-24。工作室事项：`WI-20261024-001`。这是可运行的视觉候选，不是用户最终审美确认，也不是 Android 发布验收。

## 单一视觉接口

- 色彩源：`Assets/Resources/UI/CoreNeutralTokens.uss`。画布浅灰、卡片白色、正文深灰、主操作蓝色，成功/警告/失败有独立文字和背景 Token。
- 共享组件：`MobileGameDesignSystem.uss` 与 `Mobile/*`；首页、首次准备、图鉴、设置分别消费同一 Token，内容/开包/收藏由 `Assets/UI/Styles.uss` 消费。
- 保留 UI Toolkit 原生结构和生产 PanelSettings；不改变下载、存档、抽取、卡片身份或 UI/打印语言的边界。
- 背景稳定的操作根节点不跟随 pressed/disabled/loading 改动；反馈主要落在文字与独立子层。不能重新加入父容器整体缩放、淡入等 Android 风险写法。
- 五个现有底部入口暂时保留。四入口方案尚未完成全页面设置入口和返回路径适配，不能只隐藏第五项。
- Gallery/Vintage/Forest/Ruby/Electric 是包装、粒子及揭晓表现，不是全局深浅主题；本批没有全局深色开关。
- 卡图保持原比例完整显示；未拥有/缺图/未安装用明确文字状态区分，不以整卡透明度替代。

## 两种真实渲染样本

| 样本 | 内容 | 能证明什么 / 不能证明什么 |
|---|---|---|
| `CoreNeutralVisualSampleTests` | 真实 Home presenter 与共享状态组件；克隆生产 PanelSettings | 生产参考缩放下的三语言、尺寸、安全区模拟和 resolved style；共享状态页是组件图库，不是业务页面 |
| `-coreNeutralSceneCapture` | 既有开包、收藏、内容、设置 PlayMode 用例中的真实控制器/presenter | 当前 fixture 的实际渲染；固定720×1600逻辑视口、scale1，不是生产物理屏幕或端到端云端测试 |

输出在本地忽略目录 `TestResults/UIRefresh/`，PNG 配套 JSON 记录屏幕、语言、状态、fixture、模拟安全区与非黄金图属性。真实卡图及随机抽取样本依赖本机已有目录，不能充当稳定像素黄金图。内容管理用缓存/内存 fixture；设置展示用 no-op 操作回调，不代表真实恢复/连接操作已经执行。

## 重现验证

使用项目匹配的 Unity `6000.0.73f1`。单实例依序运行；所有路径按本机实际位置替换。

```text
Unity.exe -batchmode -nographics -projectPath <game> -runTests -testPlatform EditMode -testResults <game>/TestResults/UIRefresh/editmode.xml -logFile <game>/TestResults/UIRefresh/editmode.log
Unity.exe -batchmode -projectPath <game> -runTests -testPlatform PlayMode -coreNeutralSceneCapture -testResults <game>/TestResults/UIRefresh/playmode.xml -logFile <game>/TestResults/UIRefresh/playmode.log
```

PlayMode 截图运行不能加 `-nographics`，没有图形设备应失败而不是输出假截图。先检查 NUnit XML 的通过/失败/跳过，再看导入/编译警告，最后人工查看实际页面。测试通过不自动证明没有遮挡：S2 首次3/3通过后仍从截图发现顶栏设置被覆盖，修正后已加入几何断言。

2026-10-24 最终工作区候选：EditMode **595/595**、PlayMode **37/37**，均0失败/跳过。首轮失败与修复过程保存在工作室事项中。新增断言覆盖图鉴聚焦禁用对比、三语言动画速度文本及短屏收藏视口不得侵入底栏；不会靠把测试改为竖屏绕过短屏错误。收藏当前本机目录样本缺图，详情截图记录missing-art/Retry，不等同有图详情验收；开包样本另要求真实卡图Ready。

## 剩余验收边界

Android 原生安全区域、字体/密度、手势与焦点、返回键、性能/内存、读屏、目标玩家可用性与用户美术确认仍单独验收。模拟器可以补平台冒烟，但不能替代实体机和玩家观察。最新结果与全部剩余工作以工作室事项 state/evidence 为准，不从本说明推导发布状态。
