# 海风捎来的信 · Seabreeze

原创 2D 海滨小镇叙事探索游戏原型。Unity 6.3 LTS / Universal 2D。

## 打开与运行

1. 在 Unity Hub 添加本目录，使用 **6000.3.27f1** 打开。
2. 打开 `Assets/Seabreeze/Scenes/Street.unity`，点击 Play。
3. 安装 Node 24+ 与 npm。团队模式会随 Play 自动启动 NPC 和直播本机服务；首次在弹出的连接页用有仓库权限的 GitHub 账号登录，之后无需填写模型 Key。需连接可访问 Devbox 的内网。说明见 [团队模型连接](Tools/team-model/README.md)。第一次 Unity 导入还需要解析 Packages 并生成中文字体缓存。

点击 Play 后先进入动态海岛首页。可继续有效存档，或确认后重新开始；首页停留不会推进游戏或自动覆盖存档。新入口、白天对话配色及本轮编辑器验收见 [开始页与对话说明](Docs/StartPageAndDayDialogue.md)。NPC 与 Z 直播文字对话使用团队统一提供的方舟模型；语音、识别及点歌仍需要各自的服务配置。

本机曾构建 `Builds/macOS/Seabreeze.app`（macOS Universal）；构建产物不随源码提交。从仓库克隆后请在 Unity 中构建。

八人记忆试玩：双击 `启动七人记忆试玩.command`（启动器名称为兼容保留），首次用 GitHub 连接团队模型，再进入游戏。当前角色的自主交谈、定向剧情消息、存档隔离与验收说明见 [当前接入说明](Docs/MemoryIntegration.md)。当前模式使用独立试玩存档，不自动发布固定 Day4 公告。

进入后，沿海边街道找那张旧明信片。可以先去咖啡馆，也可以先和街上的人聊天；日志会记下已知线索。地图可以在三个地点间通行，时段通过长椅休息或喝咖啡推进。

## 操作

| 操作 | 键位 |
| --- | --- |
| 左右移动 | A / D，← / → |
| 跳跃 | 空格 / W / ↑ |
| 就近交互 | E |
| 对话继续 | E / 空格 / Enter；也支持鼠标 |
| 对话选择 | 1 / 2 / 3；或鼠标点击 |
| 地图 | M |
| 手机 | Tab |
| 日志 | J |
| 暂停 / 关闭面板 | Esc |
| 收起 / 恢复 HUD | H |

## 项目结构

- `Assets/Seabreeze/Scenes`：海边小街、潮汐咖啡馆、灯塔海堤。
- `Assets/Seabreeze/Scripts/Core`：会话、时钟、存档、剧情和对话。
- `Assets/Seabreeze/Scripts/Data`：ScriptableObject 数据类型。
- `Assets/Seabreeze/Scripts/World`：角色物理、交互、NPC 日程、相机、场景生命周期、氛围。
- `Assets/Seabreeze/Scripts/UI`：HUD、对话、地图、手机、日志、暂停。
- `Assets/Seabreeze/Resources`：背景、字体、角色 Prefab 与叙事资产。
- `Assets/Seabreeze/Editor/ProjectBuilder.cs`：可重复生成场景/资产、构建 macOS 应用的编辑器菜单。
- `Docs`：开发记录、验证结果与已知限制。
- `ArtSource`：新增图像的生成提示与来源记录。

`Seabreeze > Rebuild authored scenes and assets` 会重建本项目三个场景中的预设布局，不适合在手工编辑场景后无备份执行。普通运行不需要执行此菜单。

## 设计边界

这是本次独立的一天短篇，沿用提供的侧视与 UI 美术方向；没有把过去其他项目中未完成的 AI、七天主线或联网功能标记为实现。

背景为独立图像，角色、物理、交互和 UI 是实际运行的 Unity 对象。背景内部建筑与远海仍属同一绘制层，额外前景和海光具有独立微视差；后续正式制作可以用分层素材替换。基础角色动画与音效仍属于原型品质。

## 存档与开发注意事项

游戏自动保存地点、横坐标、日期时段、已读对话节点、剧情选择、物品和日志；暂停菜单也可手动保存。本机已观察到编辑器与macOS独立版使用各自的存档目录：编辑器在 `~/Library/Application Support/Seabreeze Studio/海风捎来的信/`，独立版在 `~/Library/Application Support/com.seabreezestudio.letter/`。恢复开场可在 Esc 菜单选择「重新开始这一天」并确认。

在 Play 期间修改 C# 脚本会自动保存并退出播放，重新按 Play 即可继续；不会尝试恢复已经失效的热重载会话。

更完整的操作路线见 `Docs/PlayableGuide.md`，阶段记录见 `Docs/DevelopmentLog.md`，实际检查证据见 `Docs/Verification/README.md`。

## Z 的原生直播

按 Tab 打开手机，点击「Z 的直播 · 进入聊天小屋」。支持文字和语音连麦、Live2D 表情与口型、对话点歌与演唱；歌曲准备好后由主 Agent 安排演唱。返回或 Esc 恢复探索。先按 [直播服务说明](Tools/live-service/README.md) 启动服务；验证范围见 [原生接入验证](Tools/live-service/VERIFICATION.md)。

原生连麦分支支持单麦克风上/下麦、最多两条对话、RTC/MMPG 声纹保护和 Vivi 流式播放。macOS 依赖安装、独立试玩入口及验收范围见 [原生连麦接入](Tools/live-service/NATIVE-CALL.md)。

## 当前八人 Skill / 记忆运行入口（2026-11-07）

当前运行身份统一为 Z、梨园、李庭、阿星、正义、小小晴、林序、周伯伯。沿用已有横版场景和资源，职业、地图、人设、任务均来自当前Skill。后端启动 `Tools/MemoryLab/start.sh`；默认4330，`/`体验对话与玩家任务，`/memories`查看独立记忆。Action／Info由后端存储并供Unity执行器使用，不在玩家界面展示。详细协议、任务、取消与存档验收见[当前接入说明](Docs/MemoryIntegration.md)。

每位 NPC 的环境、移动、会话、决策和行动过程拥有独立序号与运行日志，随存档恢复。`/runtimes` 是本机逐角色查看入口；上报与恢复规则见[独立运行记录](Docs/NpcRuntimeRecords.md)。

旧七人／十人字样的类名或启动器名称为引用兼容保留，不代表当前人物名单。新运行不自动发布Day4全岛消息，不使用旧剧情关键词迁移规则。首次切换到该协议建议使用新八人体验进度，旧存档需明确迁移。

## 第一日故事（2026-11-12）

统一标题页 → 开始故事 → Z 直播邀请 → 抵岛 → 码头迎接 → 广场八人介绍 → 烟花 → 海边小屋 → 床 E → 第一日片尾。P 拍照进入手机相册，左下角可单独开关 BGM。内容、接口和当前验收边界见 [FirstDayStory](Docs/FirstDayStory.md)。直播服务沿用已有实现，需要本机或团队提供运行配置。

## 本地人设、A2A、记忆与全员直播整合

双击 `启动完整角色体验.command` 启动当前角色服务与直播服务，再运行游戏。Z 是主播，七位岛民全员参与，弹幕沿用岛上个人记忆。普通前端只显示台词和玩法结果；Action/Info 留后台。详见 [本地整合与待补玩法](../docs/NPC统一链路整改-20261108.md) 与 [本轮验证](Docs/Verification/UnifiedCharacters-20261108.md)。
