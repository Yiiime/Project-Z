# 灯塔首页运行验证 · 2026-11-12

## 范围与结果

用户选择 Firewatch 的构图方向后实现原创海岛首页。仅修改首页布局、主视觉、局部动效与菜单反馈；入口生命周期复用既有 GameBootstrap，没有修改地图、正式 Player、NPC、直播业务或剧情。

- Unity 6000.3.27f1；最终 `unity recompile --json --project-path <unity-sideview>` 返回 `success=true`、`compilationFailed=false`，0 error / 0 warning。
- 本轮修改文件 `git diff --check` 通过。原工作树已有字体与模型导入差异不在本次提交中。
- 未新建测试项目、测试脚本，未运行测试套件。通过已安装 Unity CLI 的即时 Script Eval 检查运行对象、调用 UI 按钮回调与读取状态；这不是完整原生键盘鼠标验收。

## 实际运行检查

| 检查 | 结果 |
| --- | --- |
| 首页等待 | IsAtTitle=true，timeScale=0，输入锁定，LiveHost=0，Memory 未初始化；独立 Fresh 目录未生成存档 |
| 原生鼠标打开设置 | 锁屏前实际点击设置后观察到设置面板；最终补丁后未完成完整鼠标复查 |
| 弹窗焦点恢复 | 打开设置后将 EventSystem 选中清空，下一帧恢复到模态内部 Music setting；不会落入背景开始按钮 |
| 减少动效 | 关闭动效后 shader 时钟持续保持 307.2172，重新开启恢复；首页 timeScale 仍为0 |
| 新游戏 | 从独立 Fresh 存档的按钮回调进入：title=false、phase=Live、place=Harbor、LiveHost=1、Memory 初始化；直播期间输入锁定 |
| 有存档首页 | 独立 Continue 存档 day=2、place=PlayerHome、phase=Complete；显示继续旅程，LiveHost=0 |
| 重新开始取消 | 弹窗默认选中 Cancel；取消后仍停首页，独立存档 SHA256 与进入前一致 |
| 继续旅程 | 按钮回调后恢复 PlayerHome 场景、第2天、Complete、LiveHost=0、timeScale=1、输入恢复 |
| 宽高比 | 1920×1080、1920×1200、2560×1080实际 Game View 截图检查；灯塔顶端和文字菜单完整，超宽采用居中16:9构图与深蓝侧边 |
| 超宽动效边界 | 最终给 composition 添加 RectMask2D，运行对象确认存在，避免绘制海光越入侧边遮幅；模态框在裁切层外 |

开始和继续通过既有 UI 回调驱动，不能据此宣称真实鼠标、Enter、方向键整条链路已最终通过。Mac 后半程处于锁屏，已请求用户解锁，原生复查待补。直播后端/AI 对话不在本轮验收范围；进入直播界面不代表线上服务可用。

最终 Console 当前 groundTruth 为 consoleErrors=0、consoleWarnings=2（既有 memoryless depth 警告）；CLI 历史累积错误不清零，包含既有 NativeRtcAudioReference。未将历史计数冒充新编译错误，也不宣称全项目零运行警告。

## 证据

- `title-with-save.png`：最终补丁后运行页面，包含继续旅程。
- `title-16x9.png`、`title-16x10.png`、`title-21x9.png`：对应分辨率。宽屏画面已使用保留完整灯塔的构图。
- `title-motion.mp4`：1920×1080、8.03秒、静音。Unity 实际正常时间推进约8秒，120次帧末截图以实际时间间隔编码为30fps；没有加速游戏或生成假动画。采集在最后的超宽裁切补丁前完成，该补丁仅改变显示边界。
- `start-enters-live.png`：新游戏进入现有直播页面。

验收使用 UserSettings 下独立目录；结束后清除 Z_MEMORY_SAVE_ROOT、恢复原 Game View 分辨率选择。真实用户存档前后 SHA256 一致，未重置或覆盖。视觉方向已确认，成品美术仍待用户反馈。
