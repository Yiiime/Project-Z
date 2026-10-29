# Unity 主项目

本目录是团队后续开发主工程；仓库 `game/` 是已有 Godot Demo。先读根目录 AGENTS.md 的协作规则和 `docs/team/START-HERE.md`。

- 引擎固定 Unity 6000.3.27f1，URP 17.3.0；用 Unity Hub 打开本目录。
- 入口 `Assets/ProjectZ/Scenes/Boot.unity`。当前两段事件是技术样例，不能当作团队故事线已被替换。
- 快速检查：在仓库根目录运行 `tools/team/z check`；需要 .NET 8 和 Node.js 22。Unity 校验 `bash Tools/unity.sh verify`，运行态测试 `bash Tools/unity.sh playmode`。
- 构建：`bash Tools/unity.sh build`，产物 `Builds/Project Z.app`。云端快速检查不等于 Unity 编译、运行或画面验收。
- 核心规则在 `Assets/ProjectZ/Core/`；角色、移动与 UI 在 `Assets/ProjectZ/Runtime/`；事件在 `Assets/ProjectZ/Resources/Content/episodes.json`。扩展前读 `Documentation/integration.md`。
- 模型只能表达游戏已确认且当前可访问的事实；保留事件、存档、角色、请求 ID 与版本隔离。
- 资源及其 `.meta` 一起移动和提交。主场景、角色 Prefab、动画控制器同一时间只认领一个修改任务。
- 不提交 Library、Temp、Builds、测试输出、真实存档或密钥。素材新增与修改需要保留授权记录。
- 每个任务独立分支；完成后在仓库根目录运行 `tools/team/z ship`。失败时修复自己的分支，不绕过主分支保护。
