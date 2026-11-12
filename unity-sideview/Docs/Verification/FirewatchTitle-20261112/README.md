# 灯塔首页运行验证 · 2026-11-12

## 范围与结果

用户选择 Firewatch 的构图方向后实现原创海岛首页。仅修改首页布局、主视觉、局部动效与菜单反馈；入口生命周期复用既有 GameBootstrap，没有修改地图、正式 Player、NPC、直播业务或剧情。

- Unity 6000.3.27f1；最终 `unity recompile --json --project-path <unity-sideview>` 返回 `success=true`、`compilationFailed=false`，0 error / 0 warning。
- 本轮修改文件 `git diff --check` 通过。原工作树已有字体与模型导入差异不在本次提交中。
- 未新建测试项目、测试脚本，未运行测试套件。首轮通过 Unity CLI 即时 Script Eval 检查运行对象与 UI 回调；用户解锁后，已补充下文明确列出的原生键盘鼠标操作。两类证据分别记录。

## 实际运行检查

| 检查 | 结果 |
| --- | --- |
| 首页等待 | IsAtTitle=true，timeScale=0，输入锁定，LiveHost=0，Memory 未初始化；独立 Fresh 目录未生成存档 |
| 原生鼠标打开设置 | 最终代码下点击设置，实际显示设置面板并选中音乐；解锁后复查通过 |
| 弹窗焦点恢复 | 打开设置后将 EventSystem 选中清空，下一帧恢复到模态内部 Music setting；不会落入背景开始按钮 |
| 减少动效 | 关闭动效后 shader 时钟持续保持 307.2172，重新开启恢复；首页 timeScale 仍为0 |
| 新游戏 | 从独立 Fresh 存档的按钮回调进入：title=false、phase=Live、place=Harbor、LiveHost=1、Memory 初始化；直播期间输入锁定 |
| 有存档首页 | 独立 Continue 存档 day=2、place=PlayerHome、phase=Complete；显示继续旅程，LiveHost=0 |
| 重新开始取消 | 弹窗默认选中 Cancel；取消后仍停首页，独立存档 SHA256 与进入前一致 |
| 继续旅程 | 按钮回调后恢复 PlayerHome 场景、第2天、Complete、LiveHost=0、timeScale=1、输入恢复 |
| 宽高比 | 1920×1080、1920×1200、2560×1080实际 Game View 截图检查；灯塔顶端和文字菜单完整，超宽采用居中16:9构图与深蓝侧边 |
| 超宽动效边界 | 最终给 composition 添加 RectMask2D，运行对象确认存在，避免绘制海光越入侧边遮幅；模态框在裁切层外 |

### 解锁后的原生操作补验

以下使用真实 Unity 窗口、原生鼠标点击和键盘按键完成；UI 回调未由 Script Eval 触发，CLI 仅用于隔离存档、重启 Play、读取状态和保存截图。按键保持120ms以跨越渲染帧。

1. 鼠标点击设置，点卡片空白处后仍可操作；方向键切换到动效，Enter 将动效从开切换到关。音乐开关也能通过 Enter 切换，结束前均恢复开启。
2. 方向键选择如何游玩，Enter 打开说明；Esc 回设置，再 Esc 回首页，焦点回到设置。
3. 首页 Esc 打开退出确认，默认选中返回；Enter 仅关闭确认，Play 保持运行。
4. 鼠标点击开始游戏打开覆盖确认；点击遮罩空白后 Enter 仍执行取消，无穿透。取消前后独立存档 SHA256 一致。
5. 首页方向键选中继续旅程并 Enter，观察加载提示与淡出；实际进入 PlayerHome，读回 day=2、phase=Complete、IsAtTitle=false、输入恢复、timeScale=1。
6. 重新启动独立存档，鼠标点击开始，右方向键选确认重新开始；连续两次 Enter 后实际进入 Z 的直播界面。读回 day=1、phase=Live、StartPageView=0、NativeLiveHost=1、GameBootstrap=1，未重复创建入口。

原生首页菜单、设置、确认取消、Continue 与 New Game 入口已通过上述范围的验收。本次没有实际退出 Unity 编辑器，也没有进行独立构建/手柄验收。直播后端/AI 对话不在本轮验收范围；画面仍提示服务未连接。

最终 Console 当前 groundTruth 为 consoleErrors=0、consoleWarnings=2（既有 memoryless depth 警告）；CLI 历史累积错误不清零，包含既有 NativeRtcAudioReference。未将历史计数冒充新编译错误，也不宣称全项目零运行警告。

## 证据

- `title-with-save.png`：最终补丁后运行页面，包含继续旅程。
- `title-16x9.png`、`title-16x10.png`、`title-21x9.png`：对应分辨率。宽屏画面已使用保留完整灯塔的构图。
- `title-motion.mp4`：1920×1080、8.03秒、静音。Unity 实际正常时间推进约8秒，120次帧末截图以实际时间间隔编码为30fps；没有加速游戏或生成假动画。采集在最后的超宽裁切补丁前完成，该补丁仅改变显示边界。
- `start-enters-live.png`：首轮回调驱动进入现有直播页面。
- `native-continue.png`：解锁后由原生方向键与 Enter 进入保存的海边小屋。
- `native-start-live.png`：解锁后由原生鼠标、右方向键与 Enter 确认重新开始进入直播。

验收使用 UserSettings 下独立目录；结束后清除 Z_MEMORY_SAVE_ROOT、恢复原 Game View 分辨率选择。真实用户存档前后 SHA256 一致，未重置或覆盖。视觉方向已确认，成品美术仍待用户反馈。

原生补验未修改游戏代码。真实用户存档校验值与补验前相同；清除隔离目录环境覆盖后，已重新运行并停留正常首页。继续后海边小屋背景仍是夜景、HUD为上午，这是当前房间美术/时段的既有差异，本次首页验收未扩展修改。
