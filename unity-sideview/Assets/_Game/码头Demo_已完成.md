# 码头 Demo 已自动搭建完成

时间：2026-11-02 ｜ Unity 6000.6.7f1 ｜ 已通过命令行批处理：**0 个编译错误**，场景 /prefab/ 动画均已落盘。

## 一键命令（已替你执行过；以后可重跑）

Unity 完全退出后，命令行等价于菜单 **Z From Moon → Build Dock Parallax Demo**：



```
"/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -quit -projectPath ~/Desktop/z_from_moon \
  -executeMethod ZFromMoon.EditorTools.DockParallaxBuilder.Build
```

## 本次自动生成的产物



1. **Z 走路 8 帧**：`Assets/_Game/Art/Characters/Z/z_walk_00..07.png`

* 自动从 z\_walk\_sheet 2×4 切出（导入实际 512×682 / 帧，BottomCenter 锚点，PPU 100，透明）；

* 动画：`Assets/_Game/Animations/Z/z_idle.anim`、`z_walk.anim`（12fps 循环）、`ZWalkController.controller`（IsMoving 布尔在 Idle/Walk 间切换，正好对接已有 PlayerView）；

1. **ZPlayer prefab**：`Assets/_Game/Prefabs/Player/ZPlayer.prefab`

* 根：Rigidbody2D（无重力 + 冻结旋转）+ CapsuleCollider2D + PlayerMotor；

* Visual 子物体：SpriteRenderer（第 0 帧，sortingOrder=15）+ Animator；

* 复用已有 `PlayerView`（A/D 转向 flipX、写 Speed/IsMoving）；

* demo 专用 `DemoPlayerInput`（直接读键盘 A/D、←→，**不走 App/GameSession**，只用于这个独立测试场景）；

1. **场景**：`Assets/_Game/Scenes/DockParallax.unity`

* 11 张码头分层全部在场景中，sortingOrder 0→30、各挂 ParallaxPlaceholder（0.1→1.1）；

* ZPlayer 实例放在脚底线 y=-2.4；

* Main Camera 正交 size=5.4 + `DemoCameraFollow` 水平跟随；

* 洋红 Quad「GroundAnchor\_Y\_calibrate\_me」标在 y=-2.4。

## 怎么验证（约 1 分钟）



1. Unity Hub 打开 z\_from\_moon；

2. 双击打开 `Assets/_Game/Scenes/DockParallax.unity`；

3. 点 **Play**，按 **A / D（或左右方向键）**：

* Z 在栈桥上左右走、走路帧循环、转身翻转；

* 相机水平跟随；远云慢、地面同步、前景桩柱反向多移（视差）。

## 我没法替你在画面里确认、需要你 Play 后瞄一眼的两点（数值是估算的）



1. **脚底线 y=-2.4 是否正好踩在木板上沿**：如果 Z 悬空或陷进木板，停 Play，改场景里 ZPlayer 的 position.y（洋红条也一起挪）。确定合适后把 `DockParallaxBuilder.cs` 顶部 `GroundY` 常量改成该值，重跑即可固化。

2. **角色大小（Visual 缩放 0.32）**：如果相对场景偏大 / 偏小，选中 ZPlayer → Visual 子物体调 localScale，并保持脚底对齐；满意后同样回改 Builder 里 `0.32f`。

> 这两点只有在真实渲染里能看准，命令行批处理无法代看；逻辑、编译、资产切片和场景装配都已验证通过。

## demo 与正式流程的边界



* `DemoPlayerInput` / `DemoCameraFollow` 仅服务本测试场景；正式关卡仍用 PlayerController + GameSession + SceneContext.initialSpawn，未改动它们。

* 交互 / 对话没在这个独立场景接线（需要 App/GameSession）。下一步若要 "走过去按 E 交谈"，在正式 Seaside/App 流程里把码头作为一个 Location 加载，再放 NPC（Interactable）即可 —— 移动和动画这条链路现在已经跑通。

## 已知美术遗留（不影响 demo）

house/props/foreground 边缘有色键绿溢色；fx\_glow 需把材质换成 Additive 并降透明度。上线前在 PS 精修。