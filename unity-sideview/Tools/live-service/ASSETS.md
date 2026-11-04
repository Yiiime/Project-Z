# 来源与许可

本次代码来自用户提供的 Z.zip：

- `01_直播玩法基础框架/z-live-unity`：原生客户端和会话执行协议。
- `01_直播玩法基础框架/z-live-public`：最新主 Agent、连麦、观众、绘画和唱歌调度。
- `01_直播玩法基础框架/z-live-demo`：持久唱歌队列、Vivi 生成适配及已审核《晚安》唱段。
- `02_主Agent与调度/交付包/z-call-companion-v1`：语音识别协议。
- `04_玩法策略与人设/唱歌玩法`：歌曲资料库、版本与片段状态计算。

`../../Assets/Seabreeze/UI/ZLive` 包含直播小屋背景、卡牌、中文字体和 Haru 展示模型，保留随附许可证。Haru 是 Live2D 官方示例模型；Z 的专属角色模型可通过 Resources/Haru 替换并适配参数映射。

`../../Assets/Seabreeze/UI/Live2D/Cubism` 保留 SDK 的 LICENSE 与 NOTICE。Cubism Core 的许可位于该目录 `Plugins/LICENSE.md`。SDK 兼容主游戏 Unity 6000.3.27f1 的配置保留在工程中。

应用代码沿用本目录 LICENSE。角色设计和原始插画保留原权利归属。歌曲音频及试听范围的原始验收依据位于 `seed-review.json`；当前只覆盖其中记录的唱段。

来源哈希记录输入版本；本工程的原生适配包括序列化牌堆、导出 Agent 输入校验、原生播放回执和暂停期间角色时间驱动。
