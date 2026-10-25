# Core Neutral UI 实施与验证

日期：2026-10-24。工作室事项：`WI-20261024-001`。这是可运行的视觉候选，不是用户最终审美确认，也不是 Android 发布验收。

## 单一视觉接口

- 色彩源：`Assets/Resources/UI/CoreNeutralTokens.uss`。画布浅灰、卡片白色、正文深灰、主操作蓝色，成功/警告/失败有独立文字和背景 Token。
- 共享组件：`MobileGameDesignSystem.uss` 与 `Mobile/*`；首页、首次准备、图鉴、设置分别消费同一 Token，内容/开包/收藏由 `Assets/UI/Styles.uss` 消费。
- 保留 UI Toolkit 原生结构和生产 PanelSettings；不改变下载、存档、抽取、卡片身份或 UI/打印语言的边界。
- 背景稳定的操作根节点不跟随 pressed/disabled/loading 改动；反馈主要落在文字与独立子层。不能重新加入父容器整体缩放、淡入等 Android 风险写法。
- 焦点环、选中线与加载符号使用`display:none/flex`明确控制绘制；加载正文用`visibility:hidden/visible`保留布局。Android候选曾把仅opacity0的三个子层同时绘出；不要退回只改opacity的隐藏方式。根节点的背景、几何和opacity仍保持稳定。
- 2026-10-25候选：底栏固定首页/开包/收藏/内容四项；设置通过首页卡片或顶栏进入。设置返回使用独立来源记录，先关闭确认层，操作繁忙时不离开；完整测试结果见本事项本轮证据。
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

2026-10-25第二轮当前回归：完整Editor **608/608**、PlayMode **42/42**，0失败/跳过；生成33张当前PNG/JSON。收藏Ready卡图现已通过真实加载链并居中，关闭释放lease。修复了缓存新纹理在交付lease前被LRU淘汰的问题，22项缓存回归通过，未扩大预算。另修正图鉴选中形态蓝底文字对比与重复原生英文空态。新增反馈层测试覆盖五种tone的显示隔离、复位与稳定根几何。历史失败和当前输出分别保留，旧missing-art记录不覆盖为成功。

导航组合仍由页面控制器负责。`MobilePrimaryNavigation`保留五个场景动作以兼容pending/失败恢复，但底栏只挂四项；设置动作显式挂到顶栏，不自动查询或搬移页面树。`SettingsReturnNavigation`只记录四种合法来源，不写玩家存档。Content与Settings往返保留Content更早的来源；同步加载异常保留来源并恢复按钮。现有LoadManager只有dispatch接口，异步转场完成并无本轮新增保证。

设置页的UI Toolkit `NavigationCancelEvent`已接入同一返回逻辑；此接线不能证明Android硬件/手势返回已经产生正确事件，也不代表其它页面返回优先级全量验收。

Android 原生安全区域、字体/密度、手势与焦点、返回键、性能/内存、读屏、目标玩家可用性与用户美术确认仍单独验收。模拟器可以补平台冒烟，但不能替代实体机和玩家观察。最新结果与全部剩余工作以工作室事项 state/evidence 为准，不从本说明推导发布状态。

本轮Android candidate-02为Development/X86_64/Built-in，APK SHA-256 `e91be899ce643cc29a1216574f87d45ca795c5c6acce414aa0139e20b594e2fd`；同一离线API34模拟器完成三次启动预检及8张稳定画面审阅。反馈层重叠消失，中英日设置和Home/Content来源返回通过。KEYCODE_BACK、旧启动页/边缘背景及加载中的空白/旧文案仍是后续项；此结果不等于完整移动验收或正式发布。构建使用当前checkout及预存字体/设置dirty，相关原始字节保持不变。
