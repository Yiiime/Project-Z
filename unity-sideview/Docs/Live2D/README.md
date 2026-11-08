# 岛内对话角色接入

1. 在 `Assets/Seabreeze/Resources/Memory/characters.json` 找到角色已有的 `id`。Z 使用 `z`。把该角色的 `portrait` 设置为 Resources 加载键，例如 `Memory/Portraits/Z`。
2. 将角色模型、图集、表情、动作及其依赖放进 `Assets/Seabreeze/Live2D/Models/<角色>/`。用本工程的 Cubism 导入。提交资源及同名 `.meta`，保留已有 GUID。超过 50 MB 的资产按团队约定与维护者核对 Git LFS。
3. 在 `Resources/Memory/Portraits/` 创建 Live2D Model Profile。将 `characterId` 填为同一个 ID，指定 Prefab、表情、默认态、待机与口型参数。用角色自己的模型实测各参数。贴图导入上限与对话框渲染高度分别设置。
4. 若需要聊天触发表情，核对后端允许的表情键、Profile 表情或 Animator 状态、以及从回复到画面的调用。Z 已接五种表情和四个动作。其他角色需要各自的回复映射。逐项验回复时的口型、可见表情、结束回待机，再试换角色和退出。

[Schema](character-portrait.schema.json) 定义交接 JSON 的字段。可以从本工程导出的 [Z 样例](z.example.json) 和[李庭 v02-r5 样例](liting.example.json) 核对模型、表情、口型参数及资源引用。游戏运行时使用角色名册和 Unity Profile。提交给伙伴时可用以下命令核对样例。Python 需要 PyYAML 和 jsonschema 4.18 或更高版本。

```sh
python3 unity-sideview/Tools/Live2D/validate_portrait.py --project-root . --actor z --check-example unity-sideview/Docs/Live2D/z.example.json
python3 unity-sideview/Tools/Live2D/validate_portrait.py --project-root . --actor liting --check-example unity-sideview/Docs/Live2D/liting.example.json
```

其他角色将 `--actor` 换成自己的 ID，用 `--print-json` 导出交接记录。静态校验覆盖资源声明与引用。表情表现和自然复位请用实际玩家输入框在构建包里验收。

## 对话 UI 如何接立绘

1. 在 `MemoryDialogueView.BuildPlayerView()` 中给人物留一个矩形容器（`RectTransform`），在容器上保留 `RectMask2D`。将容器传给 `Live2DCharacterView.Initialize(viewport)`。组件会创建铺满容器、允许鼠标点击穿过的 `RawImage`。UI 负责决定容器位置和大小。
2. 保留对话事件。`OpenPlayer(actorId)` 从当前角色名册加载 Profile。`ShowLine` 根据回复的说话人和表情键驱动立绘，`FinishTyping` 在文字显示完后停止口型并短暂保持表情。退出、换人时沿用 `HidePlayer` 和下一次 `OpenPlayer` 的清理与加载顺序，避免把上一位的脸留在新对话里。
3. `Live2DCharacterView.ResizeTarget()` 按容器宽高比调整渲染目标和摄像机。移动或等比例缩放容器可沿用取景。框变窄时，两侧可见范围也会缩小。请用目标角色检查头顶、肩膀、挥手和脸旁特效。需要调整时，到该角色 Profile 修改 `visibleHeight`（取景高度）、`verticalFocus`（上下焦点）或 `cameraCenter`（偏移）。`textureHeight` 单独控制立绘渲染清晰度。

### 普通聊天和任务栏布局

锚点以父容器左下角为 `(0,0)`，右上角为 `(1,1)`。下列尺寸使用 Canvas 参考单位。当前参考分辨率为 1920 × 1080。

| 状态 | 立绘框 | 对话列 | 玩家任务面板 |
| --- | --- | --- | --- |
| 普通聊天 | 从 `(.025,.015)` 到 `(.51,.95)` | 从 `(.545,.11)` 到 `(.925,.87)`，两端偏移归零 | 隐藏 |
| 任务栏展开 | 左下锚点不动，右上改为 `(.36,.95)` | 从 `(.385,.11)` 到 `(1,.87)`，右边缘内收 488 | 锚定右上，尺寸 430 × 152，偏移 `(-26,-150)` |

在 `BuildPlayerView()` 设置初始布局，并同步更新 `MemoryDialogueView.Update()` 中 `showTasks` 的两个状态。已有待处理任务或进行中的任务时，界面会切换布局。任务栏收起时恢复普通锚点与偏移。每次切换后保留 `RefreshHistoryLayout()`，让文本重新换行、滚动区保持在最近一条消息。若把入场动画与新布局放在同一节点，还要核对 `SetPose()` 写入的 `anchoredPosition` 和 `localScale`。

**改版验收。** 在普通窗口和较窄窗口中，各走一次普通聊天 → 展开任务栏 → 收起任务栏。核对脸部、动作、文字和发送按钮的边界，确认对话记录可滚动。用正常输入框分别与两位角色聊一句，检查回复时动嘴、说完回待机。再切回首位角色并退出，检查标题、历史记录和立绘都对应当前对话。PR 附上普通布局、任务栏布局的分辨率与实机截图。

参见[对话面板和布局切换](../../Assets/Seabreeze/Scripts/Memory/MemoryDialogueView.cs)、[角色渲染与取景](../../Assets/Seabreeze/Live2D/Runtime/Live2DCharacterView.cs)。

### 复现双角色试玩

按 [macOS 构建步骤](../../Tools/PortraitDemo/README.md)准备环境。构建双角色版本时，把命令里的 `-zPortraitDemo` 换成 `-litingZPortraitDemo`，并为这次构建指定新的空目录。打包后，在 `.app` 同级目录双击 `直接与李庭和Z聊天.command`。角色切换和输入方法见[双角色试玩说明](../../Tools/PortraitDemo/李庭与Z试玩说明.md)。

要复测李庭 → Z → 李庭，从打包目录运行以下命令。它使用本机配置的聊天模型，通过游戏输入框发送三句话，并将截图和 `liting-z-chat-report.json` 写入临时目录。

```sh
./直接与李庭和Z聊天.command -litingZChatCheck "$TMPDIR/liting-z-chat"
```
