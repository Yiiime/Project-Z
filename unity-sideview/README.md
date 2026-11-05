# 海风捎来的信 · Seabreeze

原创 2D 海滨小镇叙事探索游戏原型。Unity 6.3 LTS / Universal 2D。

2026-11-03 第二阶段已沿用原剧情重构对话与UI：NPC交谈进入独立全屏界面，左侧为可绑定的真实Live2D，右侧为逐字文本和选项；Esc可返回场景并保留本时段未读进度。原角色澪按本轮指示更名为Z，内部剧情ID保持不变。Z使用提供的模型，阿遥与陈伯等待各自模型。

## 打开与运行

1. 在 Unity Hub 添加本目录，使用 **6000.3.27f1** 打开。
2. 打开 `Assets/Seabreeze/Scenes/Street.unity`，点击 Play。
3. 第一次导入需要解析 Packages 并生成中文字体缓存。所有游戏内容均在项目内，不需要模型 API、登录或网络服务。

本次提交是 Unity 源码工程，不包含旧的本地 Builds 应用。首次克隆后先执行 `git lfs pull` 还原 Z 模型，再用匹配版本 Unity 打开。正式主角使用本机8帧序列行走；骨骼动画仅保留为独立候选。

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
| 从对话返回场景 | Esc（保留未读进度，不提交尚未选择的选项） |
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

更完整的操作路线见 `Docs/PlayableGuide.md`，阶段记录见 `Docs/DevelopmentLog.md`。本轮对话与 UI 的实现说明和实际检查见 `Docs/Phase2/Implementation.md`、`Docs/Phase2/Verification.md`；第一阶段历史记录在 `Docs/Verification/README.md`。模型绑定说明见 `Assets/Seabreeze/Live2D/README.md`。
