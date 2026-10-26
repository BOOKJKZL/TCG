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

2026-10-26补正：固定720×1600的收藏/内容命中测试始终创建匹配尺寸的真实临时RenderTexture面板，避免只创建大子容器却仍被1000×750面板裁剪；这些渲染测试同样需要图形设备。PNG导出仍由`-coreNeutralSceneCapture`控制。独立短屏收藏、1000×600状态矩阵和2倍字号测试不改成竖屏。确认Sheet拥有自己的SafeArea，模拟取证必须给它与底页一致的inset，不能让图片与JSON声称的边界不符。

2026-10-24 最终工作区候选：EditMode **595/595**、PlayMode **37/37**，均0失败/跳过。首轮失败与修复过程保存在工作室事项中。新增断言覆盖图鉴聚焦禁用对比、三语言动画速度文本及短屏收藏视口不得侵入底栏；不会靠把测试改为竖屏绕过短屏错误。收藏当前本机目录样本缺图，详情截图记录missing-art/Retry，不等同有图详情验收；开包样本另要求真实卡图Ready。

## 剩余验收边界

2026-10-25第二轮当前回归：完整Editor **608/608**、PlayMode **42/42**，0失败/跳过；生成33张当前PNG/JSON。收藏Ready卡图现已通过真实加载链并居中，关闭释放lease。修复了缓存新纹理在交付lease前被LRU淘汰的问题，22项缓存回归通过，未扩大预算。另修正图鉴选中形态蓝底文字对比与重复原生英文空态。新增反馈层测试覆盖五种tone的显示隔离、复位与稳定根几何。历史失败和当前输出分别保留，旧missing-art记录不覆盖为成功。

导航组合仍由页面控制器负责。`MobilePrimaryNavigation`保留五个场景动作以兼容pending/失败恢复，但底栏只挂四项；设置动作显式挂到顶栏，不自动查询或搬移页面树。`SettingsReturnNavigation`只记录四种合法来源，不写玩家存档。Content与Settings往返保留Content更早的来源；加载异常保留来源并恢复按钮。2026-10-26增加LoadManager异步成功/失败回调，转场停用立即释放等待计数；销毁页面不再被失败续体写入。

2026-10-26的`SystemBackNavigation`集中分发取消与InputSystem后备返回：先交给原生下拉/文本编辑，再按确认层、详情/筛选、页面来源处理；同帧去重，繁忙操作消费返回。开包揭晓阶段返回只完成既有结果呈现，不重新抽取。主页和启动页显示退出确认。实际Android手势与TalkBack仍需平台取证。

Android 原生安全区域、字体/密度、手势与焦点、返回键、性能/内存、读屏、目标玩家可用性与用户美术确认仍单独验收。模拟器可以补平台冒烟，但不能替代实体机和玩家观察。最新结果与全部剩余工作以工作室事项 state/evidence 为准，不从本说明推导发布状态。

2026-10-25历史Android candidate-02为Development/X86_64/Built-in，APK SHA-256 `e91be899ce643cc29a1216574f87d45ca795c5c6acce414aa0139e20b594e2fd`；同一离线API34模拟器完成三次启动预检及8张稳定画面审阅。反馈层重叠消失，中英日设置和Home/Content来源返回通过。KEYCODE_BACK、旧启动页/边缘背景及加载中的空白/旧文案仍是后续项；此结果不等于完整移动验收或正式发布。构建使用当前checkout及预存字体/设置dirty，相关原始字节保持不变。

## 2026-10-26 实施接口（当前验证见事项证据）

当前完整Unity验证：EditMode **641/641**、PlayMode **79/79**，均0失败/跳过；33对PNG/JSON。另不启用截图导出的收藏3项通过，固定尺寸命中测试与独立短屏测试均保留。包含8项字号和6项显式读屏程序路径；不能据此关闭原生TalkBack、实际48dp、ARM64/URP、性能和最终审美门槛。Android当前候选另行记证，不复用上一APK的绿色结果。

- 启动入口消费同一PageShell，关闭旧入口及遮挡页面的Side Canvas；启动失败可重试，首次内容准备保持原业务流程。短屏收藏/内容/开包与确认Sheet使用滚动布局，固定底栏之外的关键操作可达。
- `MobileTypography`按显式基础字阶乘系统倍率，倍率往返不叠乘；`MobileTypographyProfiles`是各USS字体角色的契约。输入、下拉临时菜单及虚拟行有独立绑定/回收，不全局修改其他页面。
- 中文/日文字体子集随文案重建；保留已有字符和横排字形特性，仅去掉未使用的`vert/vrt2`竖排替代，以维持256KiB预算。已有动态TMP字体资产无需重建。
- `MobileAccessibilityPageBindings`为启动、主页、设置、首次准备显式注册语义，`MobileAccessibilitySession`管理唯一屏幕/模态所有者；焦点不触发业务操作，取消复用原业务路径，销毁撤销注册。API26以下不发布原生层级，不更改应用最低版本。
- 复杂输入、下拉选项及收藏/内容/图鉴的虚拟数据浏览尚未完成读屏适配。程序侧语义测试、Android实际字体、TalkBack、真机性能及用户审美分别验收，不互相替代。

异步本地化：`CardUiText`借用Locale/表异步句柄，未就绪先给可读回退，完成后通过TextChanged原位刷新；旧语言回调和临时回退缓存被隔离。消费者不得由文字更新重选卡包、重建收藏或替换确认回调。启动ScrollView使用公开contentContainer上的显式类，不依赖Unity内部层级；长竖屏居中、短屏2倍字号保留顶部和动作可达。内容ListView首次设置itemsSource不重复Rebuild，同包重复绑定复用operation；不同包解绑和活动队列生命周期保持原规则。

2026-10-26当前Android候选SHA-256 `3c3ee8db146be4b45387ff7263ed0247740d006fb187b7383c290adf2860f9a1`：API34离线模拟器已验证稳态启动居中、统一背景、退出确认、设置来源返回及实际2倍系统字号，之后恢复默认字号。短促ADB输入曾未触发，持续按压才得到确认画面；不据此认证真机手势。早期启动截图仍空白，20秒应用级CPU采样的大部分内核样本无法细分，根因与目标配置性能仍未关闭。1530项构建输入恢复后无差异，预存dirty未纳入本批提交；原始候选和取证保留于TestResults/UIRefresh/oct26，完整范围见工作室事项。
