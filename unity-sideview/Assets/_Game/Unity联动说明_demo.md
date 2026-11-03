# 《来自月亮的Z》Unity 项目联动说明（demo 验证）

项目路径：`~/Desktop/z_from_moon`（Unity **6000.6.7f1**，URP 2D 模板，已 git init）

## 一、已有骨架（codex 之前搭好的，可直接复用）

`Assets/_Game/` 下已经有一套干净、职责分离的核心代码：

- `Scripts/Player/`：PlayerController / PlayerMotor（Rigidbody2D 横移）/ PlayerView（翻转 + 驱动 Animator 的 Speed/IsMoving）
- `Scripts/Interaction/`：Interactable + PlayerInteractor（靠近交互，actionLabel 默认"交谈"）
- `Scripts/Dialogue/`：DialogueController / DialogueView / DialogueDefinition（本地对话数据）
- `Scripts/Core/`：GameState、GameSession、TimeSlot（天/时段）、GameStateService（flag）
- `Scripts/World/SceneContext.cs`：地点 id / 显示名 / **initialSpawn 出生锚点**
- Prefabs：Player、NPC(IslanderPlaceholder)、SeasideSign；场景 App.unity（持久）+ SeasidePlaceholder.unity
- 已装包：2d.animation、**2d.psdimporter**、2d.aseprite、inputsystem、URP、TextMeshPro（含思源黑体 SDF）

结论：**移动、交互、对话、时间状态的地基都在**，demo 阶段不用重写，只要把场景美术接进去。

## 二、本次已拷入的资产

- 码头 11 分层：`Assets/_Game/Art/Locations/dock/loc_dock_*.png`
- Z 走路序列帧表（2×4）：`Assets/_Game/Art/Characters/Z/z_walk_sheet.png`

## 三、一键生成码头视差场景（Editor 菜单）

新增脚本：
- `Assets/_Game/Scripts/Editor/DockParallaxBuilder.cs`（菜单 **Z From Moon → Build Dock Parallax Demo**）
- `Assets/_Game/Scripts/ParallaxPlaceholder.cs`（运行时按相机位移做视差）

在 Unity 打开项目后（Unity 当前正开着，编译会自动进行）：

1. 等右下角编译转圈结束；
2. 点菜单 **Z From Moon → Build Dock Parallax Demo**；
3. 自动生成并保存 `Assets/_Game/Scenes/DockParallax.unity`：
   - 11 个 SpriteRenderer 按底→顶 sortingOrder 0..30 排好；
   - 正交相机 orthographicSize=5.4（对应 1080px/100PPU）；
   - 每层自动设为 Sprite(2D/UI)、PPU=100、关 mipmap；
   - 每层挂 ParallaxPlaceholder，视差系数 0.1（远云）→1.0（地面）→1.1（前景）；
   - 画面有一条**洋红 Quad「GroundAnchor_Y_calibrate_me」**。

## 四、必做的两步手工校准（demo 也建议做）

1. **对齐脚底线**：在场景视图把洋红条移动/对齐到栈道木板上沿；记下它的世界 y（当前初值 -3.6）。这个 y 就是角色出生点的高度。
2. **放 Player**：把 `Prefabs/Player/Player.prefab` 拖进场景，position 设为 `(0, <脚底线y>, 0)`；给它配一个 Sorting Layer/order 在 props(7) 之上、foreground(20) 之下（建议 order=15）。
   - 现阶段 Player 是占位方块；Z 走路帧要等切好 8 帧并做 Animator 后再换（见下）。
3. 进 Play，A/D 移动，观察：远云慢、地面同步、前景桩柱反向快移——视差成立即说明管线通了。

> 因项目此刻正被 Unity 编辑器占用，我无法用命令行批处理编译；脚本由你打开的编辑器自动编译。若 Console 报红，把第一条报错发我即可。

## 五、Z 走路帧接入（切 2×4，下一步可做）

`z_walk_sheet.png` 是 3939×2625，8 帧 2 行×4 列，单格约 985×1312。

- 在 Unity 选中该图：Texture Type=Sprite，Sprite Mode=**Multiple** → Sprite Editor → Slice → Grid By Cell Size，cell `985×1312`，Pivot Bottom-Center；
- 生成 `z_walk_00..07`；
- 新建 Animator `ZWalk.controller`（8 帧循环，约 10–12fps）+ idle 先用第 1 帧静止；
- 替换 Player visualRoot 的 Sprite 序列，PlayerView 已经会用 Speed/IsMoving 驱动，无需改代码。
- 朝向靠 PlayerView 的 flipX（已实现）。

## 六、与现有玩法联动（demo 路径）

- 在码头场景里放一个 NPC prefab（先用 IslanderPlaceholder），位置栈道中间；
- Interactable 的 interactionPoint / promptAnchor 配好，responseText 写一句台词；
- 玩家 A/D 走过去 → PlayerInteractor 显示"E 交谈" → 按 E 弹 DialogueView；
- 这就复刻了你参考图"海边小街 E 交谈"的最小闭环（美术换成码头而已）。

## 七、已知限制（沿用美术简报）

- 分层是程序化色键，house/props/foreground 边缘有绿溢色，demo 可接受，上线前 PS 去边；
- fx_glow 是黑底，正式用时把 SpriteRenderer 材质换成 URP 2D 的 Additive/Unlight，并降 opacity；
- sky 仅 607 高，Builder 已把它放顶部 y=2.36，下半由 sea 接；位置数值进场景再微调。
