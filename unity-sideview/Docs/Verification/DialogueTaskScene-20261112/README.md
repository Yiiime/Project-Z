# 对话任务卡与室内外 NPC 切换验证

本次从 `main@259b893` 开始，分支 `task/seanlee111/dialogue-task-scene-1112`。验证跨 2026-11-12 / 13 完成；使用 Unity 6000.3.27f1、URP 17.3。此前首页工作保留在独立草稿 #53，本次没有合入。

## 改动与根因

### 对话任务

旧实现仅过滤待接受任务的 NPC 归属，进行中任务直接读取全局列表；任务侧栏还会改变立绘和对话列的位置与宽度。运行时复现：李庭对话里出现小小晴的 `flowers`（`baseline-task-leak.json/png`）。

现在任务以圆角半透明横向卡片放在聊天内容下方，保留图标、标题、副文案和接受/暂不接受/协作操作，计入原有滚动内容，不改变立绘、对话列或输入区域布局。待接受和进行中状态均要求目录中的 `owner` 与当前 NPC 完全一致；未知任务、无效快照与终态不展示。进行中副文案取决于实际步骤回执与依赖，不伪造完成状态。

异步任务同步回调额外绑定存档实例、对话协调器、NPC 实例和会话 ID，防止关闭、换人、同一人重新打开或读档后，旧请求在新对话里自动续答。

主要实现：`MemoryDialogueView.cs`、新增 `MemoryDialogueView.Tasks.cs`、`MemoryGameController.cs`。

### 场景切换

根因是初始化顺序：场景内旧 NPC 先激活；`SevenNpcSpawner.Update` 在转场期间直接返回；淡入已结束才禁用旧角色并生成/恢复新 NPC。因此玩家会看到“旧位置/旧角色 → 新位置/新角色”的瞬间替换。

修复将目标场景 NPC 替换和位置恢复移到黑屏阶段，并等待 `NpcMovement.IsReady` 后淡入。准备过程幂等；清理时先禁用旧实例再延迟销毁。保留移动锁定和原有场景路由。等待使用实时计时，5 秒异常上限后发出警告并释放转场，避免永久黑屏。

同时补齐 Load/NewGame 的坐标采集保护：状态替换期间不再在 `TravelRequested` 中把旧场景 NPC 位置写入新存档。

主要实现：`SevenNpcSpawner.cs`、`WorldDirector.cs`、`MemoryGameController.cs`。未修改 Player、运动参数、NPC 素材、蒙皮、场景文件、地图面板或任务后端协议。

## 实际验证结果

### Unity 与原生操作

- 运行 `unity recompile --json --project-path …/unity-sideview`：完成，`failed=false`，编译错误和警告列表均为空。
- 在真实 Play Mode 中，以临时内存任务快照覆盖 UI 输入；原生按 E 打开李庭对话，鼠标点击“接受”，生成且仅生成一条 `bar_box` 接受事件（`native-task-accept.json`）。原生 Esc 正常回到咖啡店。
- 待接受和进行中卡片布局相同；精确配置保持 `portraitMax=(.445,.84)`、`columnMin=(.455,.12)`、`column.offsetMax=(0,0)`。JSON 的 Vector2 字符串为 Unity 四舍五入后的显示值。
- 已完成步骤 `storage` 的任务快照显示下一步“实际取回送修的收音机零件”（`task-card-active.json/png`）。
- 同 NPC 重新打开后，旧任务回调不发送回复；当前会话回调正常进入发送状态，作为正向对照（`callback-session-fence.json`）。

最终卡片画面：

![待接受任务卡](task-card-final.png)

### 同一任务快照的 NPC 隔离

| 当前 NPC | 显示任务 |
|---|---|
| 李庭 `liting` | `bar_box` |
| 阿星 `axing` | `generator` |
| 小小晴 `xiaoxiaoqing` | `flowers` |
| 梨园 `liyuan` | `map_blank`、`map_trial` |
| Z `z` | 无 |

空快照、损坏 JSON 和 completed 状态均为零张卡片。详见 `task-isolation-matrix.json`。

### 转场逐帧观测

采集真实 Play Mode 的帧号、遮罩 alpha、NPC 激活状态、位置和就绪状态；“可见”定义为遮罩 `alpha < 1`。计数为本次采样结果，不代表所有机器固定帧数。

| 场景流程 | 采样数 | 可见旧 NPC 帧数 | 可见且未就绪 NPC 帧数 |
|---|---:|---:|---:|
| 修改前 Street → Cafe | 340 | 16 | 0 |
| 修改后 Street → Cafe | 323 | 0 | 0 |
| 修改后 Street → Cafe → Street → Cafe | 571 | 0 | 0 |

往返检查先让李庭自主移动，再短暂保持姿势记录位置。离开前 X 为 `-2.641759`，回来首次可见 X 为 `-2.641759`，恢复误差 `0`。证据：`baseline-transition.json`、`fixed-transition.json`、`fixed-roundtrip.json` 及淡入截图。

## 验证边界与保护

- 上述任务快照和气泡文字为离线验收输入；验证的是实际 Unity 展示、原生点击、事件生成和会话隔离。没有验证线上 AI 生成任务或服务器端完整任务完成链。
- 针对性验收覆盖 Street/Cafe 往返，并非穷举所有地点、所有日程或网络异常。
- Load/NewGame 防回写经过调用顺序检查，本次没有另外进行完整新游戏/读档流程验收。
- 未创建测试工程、测试脚本或运行测试套件。逐帧数据由临时 Editor eval 委托采集，采集结束即解除。
- 使用 `UserSettings/DialogueTaskScene-20261112/` 隔离存档。结束后退出 Play 并清除存档覆盖配置；真实存档 SHA256 前后一致：`210b8d23cd6094255360f9bfb93a4da0735032fb601567e6e579576930a10c9d`。
- 不以本次编译结果声称所有运行时警告均消失；导入与验收期间已有 Metal 警告。本次结果仅针对上述两个缺陷。

## 设计依据适配

沿用项目研究的 K01（准备与行动分离）和 K04（NPC 状态与因果）：任务卡只展示既有任务状态，按钮仍走原有接受/协作协议；NPC 在可见前完成准备。保持 K05/K10/K12 所强调的前置条件、回执与状态区分，不新增奖励、时间消耗或任务完成规则。本次属于展示与生命周期修复，不扩大游戏机制。
