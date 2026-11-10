# 直播中的 Z Live2D

直播动态形象使用 `demos/z-live2d-r5/Assets/ZDialogueDemo/` 的 Z 原始模型；直播服务、音轨、连麦及才艺业务继续使用主工程现有实现。主工程保持 Unity 6000.3.27f1、URP 17.3.0 和已有 Cubism SDK。

## 还原与运行

首次打开主工程时，`Editor/ZLiveModelSetup.cs` 调用现有 Cubism 导入器，从 `Art/Z/Model.model3.json` 生成模型缓存和 `Resources/ZLiveModel.prefab`。也可运行菜单 **Seabreeze > Prepare Live Z Model**。构建前会检查并补齐生成资源。

生成的 `.asset`、模型 Prefab 及其 `.meta` 不入库：其中 Cubism 缓存约 60 MB，超过仓库单文件限制。它们全部在同一次本地导入中生成，没有场景或其他提交资产直接引用这些生成文件的 GUID；运行时通过 `Resources.Load("ZLiveModel")` 加载。原始模型、贴图、JSON 和其 `.meta` 随源码保存。

从 `Street.unity` 的手机进入直播，启动服务的方式见 `Tools/live-service/README.md`。角色设置可在 Z Live2D 与原画之间切换。

## 表演与声音

- `CharacterPresenter` 负责模型、独立相机、透明 RenderTexture、音频采样和输入。`ZPerformanceController` 是独立 Demo 表演层的主工程副本，移除了 Demo 对话客户端／固定时长分段依赖，使用直播的非缩放时钟。
- `ZLivePerformanceMap` 将现有直播情绪和动作语义映射到 Z。`happy/warm` → 微笑；`thinking/sad` → 困扰；`shy` → 脸红；`surprised` → 惊讶；`serious` 使用中性脸与严肃身体动作。挥手、解释、指向是模型现有三种静态手势；头身动作单独连续变化。无对应手势的指令回退无手势。
- 现有直播 Agent 的情绪词表不在本次修改范围；运行时也接受 Z 的 angry、cry、bawl、sigh、gloom 标签，供显式表演指令和验收使用，不能据此认为当前 Agent 会自动选出全部标签。
- 口型继续读取 `LiveAudioPlayer` 的 AudioSource；最后在 Cubism 更新序号 1001 写嘴部开合，避免表情控制器覆盖声音。说话、唱歌共用此链路；叹气时保留眼部素材，说话期间让出嘴部覆盖。
- 特效组件随直播实例创建和销毁，不使用全局场景查找或常驻全屏覆盖。

## 验收入口

`tools/team/z check` 检查源码；Unity 菜单 **Seabreeze > Verify Live Z Integration** 运行 Street 场景中的 NativeLiveHost、模型和音轨流程，输出至 `Temp/ZLiveVerification`，可用 `Z_LIVE_QA_DIR` 指定位置。测试使用合成音频、隔离的连接失败地址和替代回执，不读取真实服务凭据，不调用供应商。它不等同于真人麦克风、真实 TTS 或真实歌曲验收。

可用 `ZLive.EditorTools.ZLiveVerification.Build` 构建开发验收应用；运行时传 `--z-live2d-verify=/绝对路径` 执行同一组测试。验收组件仅在 Editor 或 Development Build 中编译，且只有显式调用才运行。

## 来源

直播现在优先实例化主游戏角色资源 `Resources/Memory/Portraits/Z` 的 prefab，并读取同一份表情配置；sad、shy、serious、happy + wave 分别采用主游戏的委屈、害羞、认真和开心挥手脸形，手势时序和音频口型由直播驱动。主游戏模型不可用时才加载 `Resources/ZLiveModel`。

原直播回退素材与三份表演／特效代码来自仓库 R5 Demo，基线 `3fa7210`。原始模型文件不作绘制修改；仅适配运行时生命周期、参数调度、相机与特效投影。SDK 继续使用仓库既有版本，不另行分发 SDK 原包。原 Demo 和其他协作者分支保持原样。

## 本次验证（2026-11-06）

- `tools/team/z check` 通过；247 个 C# 文件语法检查无错误。Unity 6000.3.27f1 实际编译及 macOS 开发应用构建通过。
- 移走全部 24 个本地生成资产／meta 后，原始资产静态检查通过，Unity 重建模型并再次成功构建独立应用。
- 最终独立 Player 的 42 项行为检查全部通过，另记录 1 项 Canvas 渲染诊断：10 种表情组合、三类手势、非缩放时钟、暂停恢复、合成语音与合成唱段经现有播放路径驱动口型、取消、关闭释放及重新进入。
- 实际 Player 截图检查了模型构图、挥手、哭泣泪线及黑化阴影。编辑器批处理中的 uGUI 特效截图不作为最终画面依据；最终以独立 Player 为准。
- 13 个原始模型文件与 Demo 来源 SHA-256 完全一致。Haru 自动重导入、字体动态缓存和构建设置的副作用已恢复，不纳入提交。
- 离线验收使用不可连接的回环地址，界面显示连接失败／暂停是预期；音轨回执由测试替代。未验证真实供应商 TTS、真人连麦或新歌生成。Unity Editor Search 索引的既有异常单独记录，未计为游戏运行结果。

本任务按用户要求保持草稿，不运行 `tools/team/z ship`，不合入 main。
