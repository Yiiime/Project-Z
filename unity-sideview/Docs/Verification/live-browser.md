# 外部浏览器直播接入验证（2026-11-04）

## 当前方案：路线3，无转场动画

Tab → 手机 →「Z 的直播 · 进入聊天小屋」调用系统默认浏览器打开 https://z-moon-island-live.daiziqing2.chatgpt.site/ 。普通 HTTPS URL 不携带 embed、bridge、session 或用户数据，不等待 WebView 握手。

Unity 显示返回面板，暂停游戏时间、移动与交互，并保存/暂停 AudioListener 状态。切回游戏不会自动恢复；点击「继续探索」或在 Unity 内按 Esc 后恢复原位置、游戏输入、时间和此前音频暂停状态。可点击「重新打开网页」重试，或「复制网址」手动打开。

Application.OpenURL 不返回网页加载结果。因此界面只表示“已请求打开”，不声称已加载成功；网络错误、浏览器阻止等情况由浏览器展示。调用抛出异常时保留重试和返回入口。

外部浏览器属于独立应用。Unity 无法自动关闭标签页或停止其声音；返回面板明确提醒先关闭直播网页。没有自动唤回游戏、技能状态同步、关系奖励或存档写入。窗口重新聚焦也不等同于直播已结束。

## 依赖与网页

Unity 6000.3.27f1、现有 uGUI/Input System 和系统浏览器即可。无需 Vuplex 或其他 WebView 插件，移除了本任务原有适配器及不再适用的桥接测试。主 agent、skills、Live2D 和歌曲继续运行于原网页及服务端，未拷贝进 Unity 仓库。

先前上线的网页第24版支持可选 WebView 协议；普通 URL 不启用该模式，本轮无须再次发布网页。以后在同一网址发布网页内容，重新打开网页即可使用；已有标签页须刷新，且需考虑网站缓存。Unity 入口改动仍需更新游戏。

## 验证

- `tools/team/z check` 通过：38 个 C# 文件，0 语法错误。
- Unity 6000.3.27f1 真实编译及 Play Mode：12通过 / 0失败。
- 测试通过替代 URL 启动器验证：暂停游戏与声音、普通 URL、重复进入不重复打开、主动重试、返回恢复、保留原位置、保留原音频暂停状态、异常可恢复和游戏事实/存档不变。
- 测试存档位于忽略的 TestResults/ExternalLive 随机目录；未改真实存档。替代启动器测试不等于默认浏览器实际启动验收。
- 编辑器日志仍有 UnityEditor.Search 索引异常（非业务脚本）；不宣称控制台零异常。
- 实际系统跳转已验证：隔离预览以 `-z-test-external-browser` 参数调用手机按钮的 onClick，Application.OpenURL 打开系统默认 Chrome，看到原直播页、Live2D 和“声音已静音”。关闭本次测试标签后，Unity 返回面板保持显示；键盘 Esc 关闭返回面板，D 键实测角色可以继续移动。
- 桌面自动化工具的原生鼠标点击返回 noWindowsAvailable；因此未宣称鼠标端到端点击通过。按钮回调实际执行、浏览器显示和键盘返回已验证。独立打包版和 Windows 尚未验收，PR 保持草稿。
- 调试以 -noaudio 启动 Unity。网页源码默认关闭语音，测试不主动开启语音或演唱。

命令（仓库根目录）：

```sh
tools/team/z check
"/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app/Contents/MacOS/Unity" -batchmode -noaudio -projectPath "$PWD/unity-sideview" -executeMethod Seabreeze.Editor.ExternalLiveVerification.RunBatch -logFile /tmp/z-external-live-tests.log
```

手动验收：手机点击直播 → 浏览器显示原直播页 → 游戏保持暂停 → 关闭网页并切回游戏 → 继续探索 → 角色保持原位置并可以移动。网页内各才艺由现有网页负责，本轮不宣称完成全技能回归。

研究边界：保留 K03/K04 的真实经历与反馈边界；仅接入页面入口，不把访问网页、切回窗口当成完成才艺或获得奖励。
