# Z · Live2D 对话接入

2026-11-03。此目录包含真实 Cubism 模型及项目适配组件。模型来自用户提供的 `Z_Unity表情验收包_20261103.zip`；用户已确认将原澪角色改为 Z，保留内部稳定 ID `npc_mio`。阿遥与陈伯没有绑定此模型。

## 实际资源与版本

- SDK：官方 **Cubism 5 SDK for Unity URP R6_1**，tag `5-r.6.1`，组件 commit `a52eba14fbbd5dbc0f3db4f8c2d612458dd947eb`。
- Native Core：来自同一官方 R6_1 unitypackage，Core 更新记录标明 **06.00.0001**。没有混用用户旧包的 Core 5.1。
- Unity：项目保留 **6000.3.27f1**，URP 保留 **17.3.0**。
- SDK 位置：`Assets/Live2D/Cubism`，官方 `LICENSE.md` 与 Core 许可文件完整保留；未导入 Samples。
- 原模型：`Models/Z/Model.moc3`、`Model.model3.json`、贴图、物理、cdi3、12 个 exp3。
- 新模型 Prefab：`Models/Z/Model.prefab`，由官方 URP importer 从原文件重新生成。
- 项目 Profile：`Profiles/Z.asset`。
- 完整来源、官方包 SHA-256、模型原文件 SHA-256：`IntegrationManifest.json`。

用户验收包中 BiRP R4_2 的旧 Prefab、MaskTexture、生成资产、Demo Runtime/Scene、QA 脚本和 Unity 6.6 编辑器 patch 没有直接导入。R6_1 官方组件已有其更新后的编辑器兼容实现。

## 显示生命周期

`Live2DCharacterView.Initialize(RectTransform viewport)` 在调用方 UI 容器内创建独立 RawImage。

`Show(Live2DModelProfile profile)` 实例化真实 Cubism Prefab，创建独立离屏场景节点和正交相机，将模型及其子物体放入 `Live2DPortrait` 层。相机使用官方 Cubism URP Renderer，并输出带 alpha 的 RenderTexture，RawImage 显示该纹理。世界相机继续使用原来的 URP 2D Renderer。

`SetState(string emotion, bool talking, string motion = null)` 对相同状态去重，允许 UI 每帧调用。表情使用用户提供的 exp3 参数；`Live2DStateDriver` 在 Cubism physics 前按更新顺序应用表情和基础 Idle/Talk 参数。`Hide()` 停用模型节点与相机，不继续渲染隐藏立绘；切换 Profile 和销毁 View 会释放旧实例和 RenderTexture。

`Live2DProjectSetup.Setup()` 是可重复执行的编辑器配置入口：将 Cubism Renderer 追加到当前 pipeline 的 renderer list，保持默认 2D renderer；分配空闲用户层；重导入模型；建立 Z Profile。它不会将全项目切换为 BiRP 或 3D 场景。

## 为 NPC 绑定模型

1. 将模型完整导出目录放进 `Assets/Seabreeze/Live2D/Models/<角色名>/`，保留 `.model3.json` 内引用的相对路径。官方 importer 会生成模型 Prefab。
2. 用 `Create > Seabreeze > Live2D Model Profile` 创建角色 Profile；设置 prefab，填独立 characterId，绑定表情。当前项目已配置 rendererIndex=1、Live2DPortrait 层30。
3. 在 `Assets/Seabreeze/Resources/Content/npc_*.asset` 的 `Live 2D Profile` 字段拖入该 Profile。Z 已绑定 `Profiles/Z.asset`，内部 `npc_mio` 保持不变。
4. 通过 Profile 的 autoFrame / visibleHeight / verticalFocus / cameraCenter / modelPosition / modelScale / uvRect 调整取景，不改对话 UI 坐标。
5. 对话节点可选 `speakerId`、`emotion`、`motion`；speakerId 与当前 NPC ID 相同时才切换 Talk。现有节点不必全部补写；未配置 emotion 时使用 neutral。
6. 若增加作者制作的动作，将 `.motion3.json` 导入的 clips 放入 Animator Controller，按 Profile 配置 Idle / Talk 状态名，再绑定 animatorController。正式动画控制同一参数时，可关闭相应 proceduralIdle / proceduralTalk，避免覆盖。

阿遥、陈伯还需要各自的 `.model3.json`、`.moc3`、所有引用纹理 PNG；建议同时提供 `.physics3.json`、`.exp3.json` 和 Idle/Talk `.motion3.json`。可选 `.pose3.json`、`.cdi3.json` 也应随目录一并提供。也可交付兼容当前官方 URP SDK 的 Unity Prefab 和全部依赖。静态 PNG、PSD 或 Cubism 编辑工程本身不能替代可直接运行的模型导出包。

## 表情与动作

| 叙事元数据 | 原始表情文件 |
|---|---|
| neutral | 回到模型默认表情参数 |
| shy | z_shy.exp3.json |
| sad / aggrieved | z_aggrieved.exp3.json |
| serious | z_serious.exp3.json |
| happy_wave | z_happy_wave.exp3.json |
| happy / smile | smile.exp3.json |
| surprise / angry / troubled / wave / explain / point / blush | 同名 exp3.json |

包中 **没有 motion3.json**。目前 Idle 的呼吸、眨眼与轻微摆动，以及 Talk 的嘴部开合是明确的程序参数动画，不能称为模型作者提供的 Idle/Talk 动作，也不是语音音素驱动或音频口型同步。Profile 保留 `RuntimeAnimatorController` 与 Idle/Talk state 名，可后续接入正式动作。`motion` 参数只有在对应 Animator 状态存在时才触发，缺失状态不会伪造动作文件。

Profile 中的相机中心、自动取景、可见高度、垂直焦点、模型位移缩放、uvRect 与 RT 高度可以调整裁切。默认 RT 高1200、宽度随 UI 容器变化，受分辨率上限约束。原模型贴图/网格较大，移动端性能不属于本次验证范围。

## 核查记录与证据边界

本轮已在 Unity Play 与 macOS 独立应用中实际通过 E 打开 Z 对话并看到真实模型。Play 回读为32参数、40绘制对象；笑眼/抬手、认真表情与Talk/Idle嘴部参数已观察到变化。详细证据和局限见项目 `Docs/Phase2/Verification.md`。本轮没有创建测试工程、测试脚本或执行附件中的QA测试套件；局部实际检查不替代用户最终视觉验收。

## 官方依据

- [官方URP迁移说明](https://docs.live2d.com/en/cubism-sdk-manual/migration-from-birp-to-urp/)：BiRP与URP包不同，URP须配置Cubism Renderer并指定给相机。
- [官方自定义渲染过程](https://docs.live2d.com/en/cubism-sdk-manual/rendering-urp/)：Cubism通过自定义render pass完成绘制。
- [官方R6_1发布记录](https://github.com/Live2D/CubismUnityComponents/releases/tag/5-r.6.1)：本次固定使用的源码版本。
- [官方SDK下载页](https://www.live2d.com/en/sdk/download/unity/)及[官方R6_1包](https://cubism.live2d.com/sdk-unity/bin/CubismSdkForUnity-5-r.6.1.unitypackage)。
