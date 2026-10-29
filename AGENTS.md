# 《来自月亮的 Z》项目指引

本文件适用于整个仓库。以用户当前明确要求为准；研究资料提供设计依据，不自动成为已批准需求。

## 团队协作（每个任务都遵守）

- 后续主工程是 `unity/`（Unity 6000.3.27f1，URP 17.3.0）。`game/` 是旧 Godot Demo，只作参考和历史记录，除非任务明确要求，不要修改。
- 新手入口：[docs/team/START-HERE.md](docs/team/START-HERE.md)。每次开发都使用 `tools/team/z`，不要直接在 `main` 修改、提交或推送。
- 开工：确认 GitHub Issue（任务卡）写清玩家变化、修改范围和验收；运行 `tools/team/z start <英文短名>`。一个任务只做一件可验收的小事。
- 交付：先检查 `git diff`，写中文提交说明；运行 `tools/team/z ship --title "<中文标题>" --body-file <PR说明文件>`。脚本会同步最新 `main`、运行检查、推送并创建或更新 PR。PR 说明使用 `.github/pull_request_template.md`。
- 合并：GitHub 机器人一次只处理一个 PR，先合入最新 `main` 再检查；`Project Z / Ready` 通过后自动 squash 合并。不要绕过 main 保护、强推、删除他人分支或手动关闭别人的 PR。
- 冲突或检查失败：运行 `tools/team/z sync`，在任务分支处理，再运行 `tools/team/z ship`。只保留双方都需要的改动；无法判断对方意图时停下并说明需要谁确认。
- 共享热点：`Boot.unity`、角色 Prefab、`Z_Standing.controller`、`ProjectSettings/`、`Packages/` 同一时间只认领一个任务；任务卡写明负责人。资源与 `.meta` 一起提交。
- 不提交 `Library/`、`Temp/`、`Logs/`、`UserSettings/`、`Builds/`、`TestResults/`、真实存档、`.env`、API Key 或个人 token。单个文件超过 50 MB 时先找维护者。
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
