# Project Z · Unity 基础工程

独立 Unity 3D 工程，用两段共同经历验证内容扩展、角色回应、存档与次日接续。剧情仍在探索；这两个事件是技术样例。

**当前状态：第三版角色精修已接入；38 项自动测试通过。** 以《女神异闻录 3 Reload》为视觉参照，修整 Z 的眼形、发尾与领口；站立互动改用动画片段，加入手指姿态、头眼注视和脚部约束。可先看 [15 秒动作短片](Documentation/Art/Screenshots/v3/z-standing-motion.mp4)。对话中左右方向键环绕，上下方向键拉近或拉远。默认对话来自预写内容；在线 AI 与 Memory Lab 尚未实机联调。见 [角色制作说明](Documentation/Art/character-v3.md) 和 [验收记录](Documentation/verification.md)。

**启动顺序**

1. 双击「开始试玩.command」，或在访达中打开 Builds/Project Z.app。普通试玩不需要打开编辑器。
2. 按 Enter 开始；已有存档时 Enter 会继续。默认使用预写对话，无需模型配置。
3. WASD 移动，左右方向键转镜头；走近海湾或地图桌后按 E，按 1 / 2 选择，回应结束后按 Enter 返回探索。
4. 按 N 结束今天，次日回到相同地点按 E，查看 Z 的后续回应。F1 查看共同经历，Esc 返回。
5. 退出后重新打开，按 Enter 继续，事件和日期会保留。本轮实机流程通过键盘完成；鼠标点击“开始”已由用户确认正常，其他鼠标按钮未逐项验收。

**继续开发**：团队成员先按 [协作上手](../docs/team/START-HERE.md) 接入。在 Unity Hub 的 Projects 中添加并打开本 `unity/` 目录，或双击「打开项目.command」。编辑器固定为 **6000.3.27f1 Apple Silicon**；工程场景为 Assets/ProjectZ/Scenes/Boot.unity，按 Play 运行。URP 与构建设置由「Project Z → Prepare Project」维护，已有构建场景会保留。

脚本默认查找 Unity Hub 的 `/Applications/Unity/Hub/Editor/6000.3.27f1/Unity.app`；其他位置可用 `Z_UNITY_EDITOR` 指定。每台电脑需要自己的适用 Unity 授权。

**代码从哪里扩展**

| 要改什么 | 入口 | 当前边界 |
| --- | --- | --- |
| 新事件、选项和次日台词 | [episodes.json](Assets/ProjectZ/Resources/Content/episodes.json) | 新事件可复用已配置地点；新增地点需在场景中注册 |
| 时间、事件、状态与选择 | [Core](Assets/ProjectZ/Core) | 纯 C#，不依赖 Unity；来源于实际选择的事实才进入事件记录 |
| 移动、镜头、场景与 UI | [Runtime](Assets/ProjectZ/Runtime) | 海湾美术试作；玩家仍为简化轮廓，未制作完整地图 |
| 角色、植被、道具和材质 | [美术说明](Documentation/Art/README.md) | 免费素材来源、Blender 源文件、资源目录与重建方式 |
| Z 的动作与表情 | [ZPerformance.cs](Assets/ProjectZ/Runtime/ZPerformance.cs) | 四段 Animator 动作片段、手指姿态、头眼注视、脚部约束与轻微辅助骨骼跟随 |
| 在线对话 | [character-bridge.mjs](Tools/character-bridge.mjs) | 本机服务；只返回文字、情绪、有限动作与引用，不能修改世界状态 |
| Unity 配置与构建 | [ProjectSetup.cs](Assets/ProjectZ/Editor/ProjectSetup.cs) | URP 17.3.0、桌面窗口、Mono 开发构建 |

**接入在线 AI**

在终端运行 `bash Tools/start-bridge.sh`，按提示输入模型 ID 和 API Key。密钥不回显、不写入项目或存档；停止进程后清除。默认接口为 DeepSeek 官方；使用方舟时先设置 `Z_MODEL_BASE_URL=https://ark.cn-beijing.volces.com/api/v3`，再运行启动脚本。模型 ID 使用账户实际可用的名称。

服务监听本机 4318 端口。进入游戏「设置」开启在线 AI；请求会产生对应模型费用。连接失败会明确报错，可以重试，或手动切换预写对话。关闭在线模式不会自动保存聊天为世界事实。

**验证和构建**

- `bash Tools/check.sh`：21 项 C# 核心测试、13 项接口测试、所有 Unity C# 文件语法检查，以及资源引用检查。需要 .NET 8 SDK 和 Node.js 20.19+；可用 `Z_DOTNET` 指定路径。团队交付时在仓库根目录运行 `tools/team/z check`，GitHub 也会自动运行。
- `bash Tools/check-unity-api.sh`：使用已下载编辑器中的真实 Unity / URP 程序集编译项目 C# 文件，覆盖 API 与类型检查，不运行游戏。
- `bash Tools/unity.sh verify`：在已授权编辑器中检查 URP 准备与 Unity JSON 存档往返。结果写入 TestResults。
- `bash Tools/unity.sh playmode`：4 项真实运行态集成测试，检查中文字体、角色表演、手指变化、动作中断与脚部稳定、FBX 道具尺寸、两处地点通行、存档重载和次日接续。使用独立测试存档。
- `bash Tools/unity.sh art`：重建本轮生成的美术材质、动画片段与 Prefab；定制版本应先复制为独立资产。
- `bash Tools/unity.sh build`：生成 Builds/Project Z.app 开发包；仍需要手工运行检查画面和交互。

运行日志与测试输出位于 TestResults，不纳入版本控制。存档位于 Unity 的本机持久化目录下 Saves/slot-1.json；Mac 默认是 `~/Library/Application Support/ProjectZ/Project Z/Saves/`。写入采用临时文件与原子替换，保留上一份有效备份。

**当前明确限制**

使用按键交互触发次日接续；没有自主巡游、寻路、真实语音、语音口型同步、动作捕捉、布料物理、完整直播玩法或多角色感知。Z 的长裙当前适用于站立对话，走路和坐下前仍需补充绑定与动画。继续游戏恢复事件和日期，玩家从固定出生点开始。关系值只供规则与测试核查。当前 Mac 开发包约 380 MB，包含 Intel 和 Apple Silicon 两种架构；本轮仅在这台 M4 Pro 上运行验证。

与团队整合及 Memory Lab 接入方式见 [接口与扩展](Documentation/integration.md)。Unity 编辑器、下载包、.NET 工具、密钥、存档和缓存均不在本 Git 仓库中。

依赖依据：[Unity 6.3.27f1](https://unity.com/releases/editor/whats-new/6000.3.27f1)、[官方 URP 6.3 分支](https://github.com/Unity-Technologies/Graphics/tree/6000.3/staging/Packages/com.unity.render-pipelines.universal)、[授权资格](https://unity.com/pages/license-compliance)。

中文字体：[Noto Sans CJK](https://github.com/notofonts/noto-cjk)。采用 SIL Open Font License 1.1，完整文本位于 [NotoSansCJK-OFL.txt](Assets/StreamingAssets/NotoSansCJK-OFL.txt)，会随游戏一起分发。
