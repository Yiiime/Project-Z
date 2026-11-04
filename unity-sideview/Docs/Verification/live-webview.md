# 网页直播接入验证（2026-11-04）

目标：在横版主工程的手机中打开现有 Z 直播网页，关闭后回到原位置继续探索。保持 agent 自主调度才艺；本轮不接入游戏事实、关系、物品或时间结算。

## 当前接线

- Tab → 手机 →「Z 的直播 · 进入聊天小屋」。复用 GameUI overlay：冻结世界时间、阻止移动/跳跃/交互与地图/手机快捷键；网页拥有文本输入和输入法状态下的 Escape。
- Unity「关闭直播」始终可用；网页左上返回、非输入法状态下 Escape 发出退出通知。关闭立即销毁容器，即使 JS 无响应也会释放渲染与声音。
- `LiveWebViewBridge` 通过可选 SDK 适配器加载 Vuplex CanvasWebViewPrefab；无 SDK 的成员仍可编译、探索，并看到可关闭的安装提示。
- 验证版固定静音：网页起始关闭语音，桌面 WebView AudioSource 在导航前设为 mute。需要验证语音/唱歌时可在网页开启执行，音频仍由原生层静音。启动编辑器另外使用 `-noaudio`，游戏环境音也不外放。
- 窗口失去焦点发送暂停，重新聚焦解除宿主暂停；网页自身暂停状态独立保留。
- 原网页右侧在线观众、热度入口仅在嵌入模式隐藏。
- 固定线上地址：https://z-moon-island-live.daiziqing2.chatgpt.site/ 。Editor / Development Build 可传 `-z-live-preview http://127.0.0.1:4175/`；只允许本机 HTTP 预览覆盖。

## 接口 v1

所有消息携带 `channel: project-z-live`、`version: 1`、每次打开生成的 `sessionId`。sessionId 仅用于过滤过期消息，不是登录或鉴权令牌，不能证明游戏事实。

- URL：`?embed=unity&bridge=1&session=<随机32位十六进制>`，不含密钥、存档或用户私密内容。
- 网页 → Unity：`ready`、`closeRequested`、`closed`、`state`、`skillState`。
- Unity → 网页：`start`、`visibility`（paused / muted）、`close`。
- `skillState` 只有 kind / taskId / phase，不上传聊天正文。Unity 仅记录最近执行状态，不自动推进世界状态。
- 网页加载时先冻结调度，收到匹配的 start 后启动。退出取消请求并销毁 engine / speech / Live2D。
- 加载失败、初始化失败或45秒未握手，释放容器并保留可关闭的错误面板。

## 依赖与分发

Unity 6000.3.27f1、URP 17.3.0、现有 uGUI 2.0.0 / Input System。Vuplex 正式版支持 Windows/macOS，但 **14 天试用版仅支持 Windows，不支持 macOS**。官方说明 macOS 因代码签名要求不提供试用版：https://store.vuplex.com/free-trials/?product=Unity.WebView.Standalone 。不能用 Windows 试用包在 Mac 编辑器里完成真实页面验证。

当前已下载 v4.15.2 Windows 试用包（有效期至 2026-11-18），归档内 273 个资产路径均位于 Assets/Vuplex，只有 Windows 原生运行库。未把这个包导入 Mac 主工程。已对照其中 C# 源码核对 CanvasWebViewPrefab.Instantiate / WaitUntilInitialized / AudioSourceEnabled / AudioSource / PixelDensity、消息事件与销毁接口；这只能确认 API 形状，不能代替运行验收。

试用仅用于评估，试用账号、到期授权和大型原生插件不提交。SDK 的 Assets/Vuplex 目录与 .meta 已忽略。正式分发须换成符合团队使用范围的授权版本。适配器当前验收目标是桌面 Mono，IL2CPP 尚未验收。

网页侧修改在独立 z-live-public 源码的 ui/app.js 中。在线版本必须部署该通信改动后才会完成握手；仅更新 Unity 不会自动发布网站。

## 已完成证据

- `tools/team/z check`：初轮静态检查通过。
- Unity 6000.3.27f1 编译并进入真实 Play Mode；`Seabreeze.Editor.LiveWebViewVerification.RunBatch` 11通过 / 0失败。
- 测试使用替代容器，验证暂停和恢复、导航前静音、会话隔离、仅观察技能结果、原位置保持、关闭清理、加载中关闭、缺 SDK 提示和存档不变。结果位于忽略目录 `TestResults/LiveWebView/lifecycle.json`。
- 测试存档使用独立随机目录，未改真实存档。首次编辑器导入出现 UnityEditor.Search 索引异常（非业务脚本堆栈）；未据此宣称控制台零异常。
- 网页本地构建通过；6项桥接测试通过；已有 conversation-regression 与 talent-regression 通过。
- 已有 public-demo-regression 在“Cookie 不应续期”的旧断言失败。该断言与当前服务续期逻辑不一致，本轮未修改访客鉴权。

命令示例（仓库根目录）：

```sh
tools/team/z check
# 指向独立网页源码的实际位置，不把它复制进 Unity：
Z_LIVE_WEB_SOURCE=/absolute/path/z-live-public/ui/app.js node --test unity-sideview/Tests/LiveWebView/bridge.test.mjs
# 真编辑器、隔离测试存档、全程静音：
"/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity" -batchmode -noaudio -projectPath "$PWD/unity-sideview" -executeMethod Seabreeze.Editor.LiveWebViewVerification.RunBatch -logFile /tmp/z-live-lifecycle.log
```

线上网页第24版已于2026-11-04发布，源提交 `bad9916a7fa9aa53c5a60ffcc75a02d5a550445e`。

## 尚待验证

真实网页握手、中文输入、鼠标跟随、高清画面、各才艺完整执行及退出后的后台清理、桌面构建。不能把上述替代容器测试视为真实 WebView 联调通过。

继续验证有两条 Vuplex 路径，待用户选择：

1. Windows 免费评估：在 Windows 安装 Unity 6000.3.27f1，检出本任务分支，导入官网下载的 v4.15.2 Windows 试用 .unitypackage，打开 Street 场景。保持 `-noaudio` 启动和适配器原生 AudioSource 静音，从 Tab → 手机 → Z 的直播进入；逐项验收后关闭直播，确认原位置、移动和游戏时间恢复。试用包不能作为团队长期依赖。
2. Mac 验证：由用户取得 Vuplex Windows/macOS 正式版后再导入本机，使用 Chromium 后端和 Metal，继续相同验收并构建。当前没有购买授权，不代用户购买。

如果用户选择免费 Mac 替代插件，需要另行核查 WebGL/Live2D、中文输入、音频静音和 Unity 通信支持，再替换 ILiveWebSurface 适配器；不把浏览器预览冒充 Unity 内嵌验收。

研究边界：保留 K03 / K04 的真实经历与反馈边界；本轮只接入显示和生命周期，未接白天经历、关系奖励或节目耗时，不采用旧固定节目设定。
