# Z Live2D 资产交接

这里是 Z 半身 Live2D 的可复用运行资产和接入参考，来源是此前 Z_Unity_Demo_20261103 的模型目录提取。原始模型和纹理字节未改；当前目录不是完整 Demo，也不包含对话服务、Unity 场景、生成后的 Prefab、Cubism SDK 或 API Key。

## 先看结论

- 只使用模型、纹理、表情和姿势不需要 API Key，也不需要联网。
- 如果要接 LLM 文字对话，需要组员在自己的后端配置 DeepSeek 或其他兼容 API 的 Key。Key 不随资产交付，也不要写入 Unity 客户端。
- 当前提供的是 Cubism 运行资产：`Model.moc3`、`Model.model3.json`、`Model.cdi3.json`、`Model.physics3.json`、纹理和 8 个 `exp3` 文件。
- 当前没有 `cmo3`、`can3`、`motion3`、PSD、原画、音频或可直接打开的 Unity Prefab。`moc3` 是运行资产，不是可编辑工程文件。
- 这套 2D 半身模型和仓库已有的 `Z_ArtSlice.fbx` 是两条资产链，不能互相替换。
- `texture_00.png` 为 4096×4096；接收方导入时应检查自己的纹理导入上限是否为 4096，避免缩小或透明通道变化。
- 本次不新增素材授权；资产沿用项目现有素材授权记录。Cubism SDK 请由接收方从官方渠道获取并按自身版本安装。

## 文件结构

~~~text
Live2D/
├── Model/                         Cubism 原始运行资产
│   ├── Model.model3.json          入口，引用 moc3、纹理、物理、显示信息和表情
│   ├── Model.moc3                 模型运行文件
│   ├── Model.cdi3.json            参数白名单和显示信息
│   ├── Model.physics3.json        物理设置
│   ├── texture_00.png             模型纹理
│   └── *.exp3.json                5 个表情、3 个姿势
├── Reference/                     原样参考源码，不会自动编译
└── parameters-and-actions.md      参数、表情和动作映射
~~~

## 接入顺序

1. 使用和自己工程渲染管线匹配的官方 Cubism Unity SDK。团队主工程当前是 Unity `6000.3.27f1`、URP `17.3.0`；此前独立 Demo 在 Unity `6000.6.7f1`、Built-in 上验证。主工程集成尚未验证。
2. 将整个 `Model/` 目录及对应 `.meta` 一起复制到工程的 `Assets` 下，不要只挑 `moc3` 或单个纹理。
3. 在 SDK 支持的模型导入流程中，以 `Model.model3.json` 作为入口生成或导入模型资源和 Prefab。官方导入说明见 [Cubism SDK for Unity：Import SDK](https://docs.live2d.com/en/cubism-sdk-tutorials/getting-started/)；具体菜单名称随 SDK 版本确认，不要假设把 JSON 拖进场景就会自动完成全部绑定。
4. 先检查静态渲染、透明通道和纹理，再逐个检查 `smile`、`surprise`、`angry`、`troubled`、`blush` 表情，最后检查 `wave`、`explain`、`point` 姿势。
5. 需要程序化动作时，阅读 `parameters-and-actions.md`，再把控制器接到模型的参数更新顺序中。不要让 Animator、ExpressionController 和自定义控制器同时写同一个参数。
6. 只有需要文字对话时，才接自己的 JSON/HTTP 后端。可参考 [DeepSeek 接入说明](../Live2D_DeepSeek接入说明.md)；那份说明描述协议，不代表本主工程已经接上服务。

## 接入时需要自己补的内容

- Cubism SDK 版本和对应的 Unity 导入设置。
- 模型 Prefab、场景引用、更新控制器和渲染材质。
- 表情和动作按钮、输入 UI、网络客户端或游戏状态适配器。
- 如果启用 LLM：服务地址、模型名、Key 的服务端存储、存档 ID 和错误处理。

不要把 API Key 放进 `.unity`、Prefab、ScriptableObject、客户端环境变量或提交记录。只在自己的后端注入。

## 已知边界

- 5 个表情和 3 个姿势来自 `exp3`。姿势文件是参数切换，不是完整的肩、肘、腕连续挥手。
- `Idle`、`Nod`、`Shake`、`Think`、`Listen`、`Happy` 是参考控制器里的程序化动作预设，不是 `motion3` 文件。
- 参考控制器会在运行时暂时停用旧 Animator，组件禁用时恢复原状态。接入前检查项目里是否已有其他脚本写入同一参数。
- 组员需在自己的 Unity 版本、渲染管线和 Prefab 上重新做画面与动作验收。
