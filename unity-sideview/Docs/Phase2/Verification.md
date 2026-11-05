# Phase 2 · 对话界面与 Live2D 实际检查记录

日期：2026-11-03（Asia/Shanghai）  
项目：`/Users/bytedance/Desktop/unity_z`  
环境：Unity **6000.3.27f1**，Universal RP **17.3**。

本文记录本轮主代理已经取得的编译、Unity Play 模式参数回读及原生 CUA 交互证据。文档整理过程中没有重复执行检查，也没有将第一阶段结果算作本轮验证。

## 编译与运行状态

- 本轮脚本编译完成：**0 error、0 warning**。
- Play 模式 Console：**0 error**。
- 第一组截图期间另出现两种 Metal `load/store memoryless` 警告，共 **4 条**；后续开场截图又出现同类成对警告。这与编译警告统计是不同阶段，不能表述为“整个检查过程零警告”。
- 未新增测试脚本，未运行测试套件。第一阶段的 **118 项检查**不属于本轮通过数量。

## Live2D 模型与参数响应

以下为 Unity Play 模式中实际加载的 Cubism 模型及参数回读，不是静态立绘模拟，也不代表已经自然走遍全部剧情表情节点。

| 检查 | 实际结果 |
| --- | --- |
| Z 模型加载 | **32 个 parameters、40 个 drawables** |
| `happy_wave` | `ParamEyeSmile ≈ 1`，`ParamPoseWave ≈ 1` |
| `serious` | Brow 参数约 **-0.65**，MouthVariant 参数约 **-1** |
| `neutral` + Talk | `MouthOpenY = 0.174945` |
| Idle | `MouthOpenY = 0` |

参数证据说明模型能够响应表情与 Talk／Idle 状态；本记录不将这些数值解释为已经验证真实语音驱动的唇形同步。

对应截图：

- [Z · happy_wave](dialogue-z-happy.png)
- [Z · serious](dialogue-z-serious.png)

## 原生交互检查

| 操作与范围 | 实际观察 | 证据边界 |
| --- | --- | --- |
| 在场景中靠近 Z，原生键盘按 **E** | 成功打开新的对话界面 | 从实际场景交互入口进入 |
| Z 会话中按住 **D 700 ms** | 玩家位置保持 **(-0.557, -0.02)** | 证明该次会话期间移动被阻塞 |
| 使用 **E** 完成当前会话 | 正常退出对话界面 | 完成的是本次打开的会话，不是整条主线 |
| 退出后再次按 **D** | 玩家移动到 **x = 2.23** | 证明本次退出后移动恢复 |
| 原生鼠标点击陈伯三个选项中的第 **3** 条 | `choice_invite = True` | **选择节点由 Editor API 预置**；鼠标选择与状态写入真实发生，但不能称为从开局自然走到该分支 |
| 会话中按 **Esc** 返回 | 返回探索，保留书签 | 已观察到挂起与书签保留；不等于已穷尽所有跨地点、跨时段恢复组合 |
| **M** 打开地图，鼠标点击咖啡馆 | `place = Cafe`、`transition = false`、`inputBlocked = false`、`timeScale = 1` | 实际场景切换完成且控制状态恢复 |
| **J** 打开日志 | 日志界面实际显示 | 本轮原生界面检查 |
| **Tab** 打开手机 | 手机界面实际显示 | 本轮原生界面检查 |
| **Esc** 打开暂停菜单 | 暂停界面实际显示 | 本轮原生界面检查 |
| 原生移动到阿遥附近，按 **E** | 成功打开新的 NPC 对话界面；显示模型尚未绑定的提示 | 阿遥没有绑定 Live2D，本次没有用假立绘冒充已绑定动态模型 |

本轮原生键盘与鼠标交互已经可用，不存在需要用户解锁后才能继续检查的阻碍。

对应截图：

- [原生 E 进入 Z 会话](dialogue-z-native-e.png)
- [陈伯三选项界面](dialogue-choices-chen.png)
- [原生地图界面](map-native.png)
- [原生日志界面](journal-native.png)

## Metal 截图警告

截图期间的两种 `load/store memoryless` 文案与 Unity 官方问题 **UUM-124793** 记录相关，可作为后续排查线索：[Unity Issue Tracker](https://issuetracker.unity.com/issues/20961)。

当前只能确认文案相关，**尚未确证本项目警告的根因就是该官方问题**，也没有将警告描述为已修复。已取得上述截图和交互结果，Console 未记录运行错误。

## 本记录尚未覆盖或尚待完成的项目

- **未遍历全部剧情及三个结局**，不将局部会话和预置选项检查写成第二阶段全流程通关。
- **最终 macOS Universal 构建成功**，0错误、0警告，详见 [构建报告](build-report.txt)。独立应用已经检查新对话与Z模型；没有遍历独立应用全部剧情。
- 检查前存档备份位于 [editor-save-before-check.json](editor-save-before-check.json)。**检查结束后已停止 Play，并逐字节恢复该备份到编辑器存档及其 .bak；检查期间进度另存 editor-save-after-check.json。**
- 已完成的源码审查、编译、Play 参数回读、原生操作与用户最终体验验收属于不同证据层级；本记录不将前几项替代用户对视觉效果与体验的认可。


## 独立应用补充检查与收尾

第一次本轮 macOS Universal 构建成功（见 build-report-first.txt），原生启动应用、用 E 读完开场、D 走近 Z，再按 E：实际显示 Cubism 的 Z 模型、happy_wave 表情和 Talk 嘴部动画，Esc 返回成功。该次 Player.log 确认 Core 6.0.1 加载，未检出 Exception、Error 或 DllNotFound。

随后发现开场玩家姿态误用 Jump，已在禁用物理模拟之前刷新地面检测。修复后编译0错误/0警告，Play 开场回读 `intro=intro; grounded=True; pose=Idle; phase=Reading`，截图见 [开场站立姿态修正](intro-idle-corrected.png)。最终包包含此修正；构建报告见 build-report.txt。

独立应用检查后通过应用菜单正常退出；原独立版存档备份为 standalone-save-before-check.json，已恢复原文件与 .bak，检查进度单独保留。编辑器与独立版存档互不覆盖。

最终构建完成时间18:05，耗时41.08秒，230841767 bytes，0错误/0警告。CLI等待在30秒超时，但Unity构建继续执行并产生成功BuildReport；以完整报告和Editor构建完成记录为结果。编辑器最终停止在Street。
