# 《来自月亮的 Z》项目指引

本文件适用于整个仓库。以用户当前明确要求为准；研究资料提供设计依据，不自动成为已批准需求。

## 团队协作（每个任务都遵守）

- 后续主工程是 `unity/`（Unity 6000.3.27f1，URP 17.3.0）。`game/` 是旧 Godot Demo，只作参考和历史记录，除非任务明确要求，不要修改。
- 新手入口：[docs/team/START-HERE.md](docs/team/START-HERE.md)。成员优先双击 `打开协作面板.command`；命令行使用 `tools/team/z`。不要直接在 `main` 修改、提交或推送。
- 开工：在协作面板选择 1～2 个区域并开工，或运行 `tools/team/z start <英文短名> --goal "<一句目标>" --area <区域ID>`。系统会从最新 `main` 开分支、推送空的开工提交，并自动创建带区域标签的草稿 PR。
- 范围：区域与路径以 `tools/team/areas.json` 为准。只修改用户所选区域的路径；需要改其他路径时，先停下来问用户，说明原因和文件。机器人发现区域外修改时会补标签提醒，不会让检查失败。
- 暂停：离开、换任务或结束本轮前，必须运行 `tools/team/z save --message "<这次做了什么>" --handoff "<已完成、未完成、下一步>"` 并确认已推送。PR 保持草稿；草稿不会自动合并。
- 交付：先检查 `git diff`，涉及画面、操作或流程时在 Unity 试玩；运行 `tools/team/z ship`。脚本会同步最新 `main`、运行检查、推送，并把草稿 PR 转为可合并。
- 合并：GitHub 机器人一次只处理一个可合并 PR，先合入最新 `main` 再检查；`Project Z / Ready` 通过后自动 squash 合并。不要绕过 main 保护、强推、删除他人分支或手动关闭别人的 PR。
- 冲突提醒：面板和机器人只提醒，不锁文件。🟡 表示普通文件重叠；🔴 表示 `.unity`、`.prefab`、`.controller`、`.anim`、`.blend`、`.fbx`、图片等难合并文件重叠。看到红色提醒时，先和对方沟通并确认谁先合并；不要逐行硬合并二进制或 Unity 序列化文件。
- 冲突或检查失败：运行 `tools/team/z sync`，在任务分支处理；修复后运行 `tools/team/z check` 和 `tools/team/z save`，准备好再 `tools/team/z ship`。无法判断对方意图时停下并说明需要谁确认。
- 接手：只有已搁置任务可以接手。在原分支继续时补充接手说明；从最新 `main` 重新开始时，原 PR 和分支保留。
- 不提交 `Library/`、`Temp/`、`Logs/`、`UserSettings/`、`Builds/`、`TestResults/`、真实存档、`.env`、API Key 或个人 token。资源与 `.meta` 一起提交；单个文件超过 50 MB 时先找维护者。
- `.github/`、`tools/team/` 和 `unity/Tools/check.sh` 是协作规则，修改后需维护者手动确认合并。
- 验收：报告运行过的命令和结果。云端快速检查覆盖 C# 核心规则、桥接服务、Unity C# 语法和资源引用，不等于 Unity 编辑器编译、运行、画面或手感验收；涉及这些内容时，在 Unity 中试玩并写清结果。

## Unity 开发入口

- 运行：用 Unity Hub 打开 `unity/`，场景 `Assets/ProjectZ/Scenes/Boot.unity`。详细说明见 [unity/README.md](unity/README.md)。
- 核心规则在 `unity/Assets/ProjectZ/Core/`；角色、移动、镜头和 UI 在 `Runtime/`；事件在 `Resources/Content/episodes.json`。扩展前读 [接口与扩展](unity/Documentation/integration.md)。
- 模型只能表达游戏已确认且当前可访问的事实。AI 回复不得凭空生成权限、物品、关系或已完成事件；保留存档、角色、请求 ID 和版本隔离。

## 设计依据与《动物迷城》研究

- 当前玩法入口：[游戏设计总案 v2](docs/游戏设计总案-v2.md)。旧 Godot Demo 的 D1、像素地图和直播方案是历史实现证据，不自动约束 Unity 3D 新方向。
- 涉及玩法、日程、资源、成长、NPC、关系、权限、道具、探索、检定、任务或存档时，先读[研究入口](research/动物迷城/README.md)和[开发参考导航](docs/开发参考导航.md)，只读与任务相关的章节。方案或 PR 中写明参考的机制卡（K01—K12）或研究章节：保留什么、如何适配、为什么偏离。
- 实质改变游戏机制时，更新相关设计文档和[机制采用记录](docs/机制采用记录.md)：研究依据 → 本项目规则 → 状态 → 实现位置 → 验证证据。
- 原作事实、研究推断、Demo 建议和本项目规则分开记录。不得把社区代码、数据表或反编译结果称为原游戏完整源码。
- 实现与验收关注可观察的因果：前置条件、玩家动作、代价、世界状态变化、失败与补救。按钮、数值条或动画存在不能替代玩法验证。
