# 验证证据索引

这些是开发过程中的实际观察和自动化运行检查，不代表玩家体验已经经过用户验收。

- `runtime-verification.json`：Unity 实际 Play Mode 内单个整合检查流程。最新结果118通过/0失败，覆盖物理移动、跳跃落地、长椅通行、键盘菜单、E就近交互、场景切换、三种结局、NPC日程、晚到恢复、日志与存档回读。
- `resume-verification.txt`：实际 Stop → Play，恢复咖啡馆、x=-1.25、radio 对话节点。
- `mouse-ui-verification.txt`：原生鼠标操作对话继续、地图入口和地点按钮后，实际到达Street，弹窗关闭，Time.timeScale=1。
- `compile-guard.txt`：播放期间修改脚本后重新编译，编辑器自动保存并退出播放。用于避免未初始化的脚本热重载会话。
- `ui-regression.txt`：故意让临时存档路径不可写时不出现虚假成功提示；在结局后增加其他日志，结局回看仍保留正确正文。临时故障条件在同一次调用的finally中已撤销，游戏已重新开始。
- `playmode-console.json`：整合运行期间的控制台快照。
- `build-report.txt`：Unity BuildPipeline 的独立应用构建结果、大小、耗时与警告。
- `standalone-smoke.txt`：macOS独立应用的原生键盘/鼠标检查记录；这是基础启动与交互复查，三种完整结局的覆盖来自上述Play Mode流程。
- `standalone-player.log`：独立应用实机检查后的运行日志副本。
- `street-final.png`、`cafe-final.png`、`lighthouse-gathering.png`：Unity实际运行截图，包含Canvas UI，非概念图。

早期验证失败、缺少画面的截图仅用于记录开发过程；最终运行图以交付记录列出的文件为准。最早一次物理采样检查108通过/2失败，修正检查起点与峰值采样后113通过/0失败，新增实际NPC实例与平台通行检查后117通过/0失败。

`unfocused-gameview-attempt.json` 记录了切换到独立应用再返回后，模拟键盘被编辑器焦点路由拦截的一轮检查。验证器已改为克隆 InputSettings，仅在模拟检查期间将事件送入 Game View，销毁时恢复项目原设置；独立应用及常规 Play 的输入设置不变。原生键盘/鼠标另行检查，不用自动化事件替代实际输入证据。

编辑器执行 ScreenCapture 时，Metal 后端曾报告 `Ignoring depth surface store action as it is memoryless`；其与截图调用同时出现。独立应用构建报告单独记录，不能将编辑器历史警告视为当前应用异常。

最终整合检查在2026-11-03 15:02（Asia/Shanghai）完成：118通过/0失败；包含从地面跳上与绘景对齐的长椅座面。检查结束后回读确认Input System恢复PointersAndKeyboardsRespectGameViewFocus / ResetAndDisableNonBackgroundDevices。
