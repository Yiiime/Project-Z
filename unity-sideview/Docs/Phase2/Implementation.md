# 第二阶段 · 对话与 UI 重构

日期：2026-11-03。沿用 Unity 6000.3.27f1、URP 17.3.0、Input System 与原三场景。

## 已有实现与修改范围

原工程由 GameBootstrap 创建 GameSession、WorldDirector 和 GameUI；NPC/物件经 PlayerInteraction → Interactable → StoryDirector → DialogueRunner 驱动。原 GameUI 同时负责底部对话、逐字、选项、HUD、菜单。本轮把对话呈现移到独立组件，其余功能继续复用原实现，没有另建游戏或替换剧情图。

- DialogueRunner：继续管理节点、条件、选择、effects；新增明确来源ID与可恢复书签。
- DialogueFlowController：Entering / Reading / Exiting / Inactive，控制完整会话与输入锁，不占用菜单的overlay布尔量。
- DialogueUIController：全屏背景压暗、左动态角色、右文本与纵向选项、逐字、单句渐换、进入/反向退出动画；物件叙述用同一组件的居中排版。
- Live2DCharacterView / Live2DModelProfile：模型与动作、表情、渲染展示独立配置；NPC通过Profile引用模型Prefab。
- UIStyleConfig / RoundedPanel / UIHoverFeedback：统一深蓝半透明、细边框、圆角、青蓝与暖橙反馈。
- InteractionPromptUI：独立E键徽标、世界位置跟随、淡入淡出与轻微缩放。

## 交互和状态语义

按E后立即锁玩家物理与相机；进入动画完成才接受下一句或选择。会话期间地图、手机、日志、暂停不会覆盖会话。Esc提前返回不会提交未读节点effects，保留来源书签；在其他场景保存后不会自动弹出已经关闭的会话。书签绑定日期时段，避免夜间咖啡会话在上午恢复。陈伯和留言簿共享的明信片决定一经提交，不再提供另一分支。

完成最后一句时先提交effects、清除恢复游标、保存；退出动画结束后才解锁玩家，并更新NPC日程和环境光照。存档version 2兼容旧数据，旧存档可见日志中的澪迁移为Z，内部npc_mio与mio_*保持不变。

## 命名与资源

用户本轮明确选择“将澪改为Z”。显示名、发言者、目标、手机和日志模板已更名，角色原有剧情和内部ID保留。用户zip仅含Z模型；阿遥、陈伯没有专属Live2D模型，因此沿用完整交谈流程，人物区明确显示未绑定状态，没有使用静态立绘或同一张脸代替。

## 视觉参考

参考用户提供的布局图，以及 [Chill with You官方Steam页面](https://store.steampowered.com/app/3548580/Chill_with_You__LoFi_Story/) 的透明深色表面、圆形线性图标、细边框和克制反馈。Reference目录仅存官方截图作为设计研究，不作为游戏资产或运行画面。

## 检查范围

本轮不新增测试工程/测试脚本，不运行现有测试套件。编译和实际交互观察结果在交付前单独记录；上阶段118项结果不作为本轮验证结果。
