# Live2D 接入与替换 Z

本轮 P0/P1 已更新为 8 类情绪、起播同步、独立口型、主动视线及动作调度。详细能力、预览入口和验收边界见 [迭代记录](LIVE2D-ITERATION.md)。新旧条目以迭代记录为准。

## 现在接入了什么

现有网页直接渲染官方 Haru 模型，不需要安装 VTube Studio、采集摄像头或推流。角色旁仍是原有弹幕、每日运势和幸运抽奖。角色语言沿用 Z 的设定，示例的外形属于 Haru；获得 Z 的模型文件后替换配置即可。

选用 PixiJS 6.5.10、pixi-live2d-display 0.4.0、官方 Cubism Core `/05/` 兼容系列，以及 CubismWebSamples 4-r.5 的 Haru。它们适合这个现有原生网页的最小接入，不需改成另一个前端框架。该组合以当前 Haru 实际验收为准；未来使用 Cubism 5.3 新特性的模型，应重新验证并升级运行层，不能直接假定旧适配器兼容。

原始依据：[适配器官方指南](https://github.com/guansss/pixi-live2d-display/wiki/Complete-Guide)、[Live2D Web SDK](https://docs.live2d.com/en/cubism-sdk-manual/cubism-sdk-for-web/)、[Cubism 5.3 兼容变更](https://docs.live2d.com/en/cubism-sdk-manual/compatibility-with-cubism-5-3/)。

## 动作如何驱动

- 模型自身负责眨眼、呼吸、待机与物理效果，鼠标位置驱动轻微视线变化。点击角色会触发打招呼。
- Z 的发言携带情绪与动作标记，适配器映射到模型表情和动作。没有对应动作时保留待机，不阻塞文字与语音。
- Vivi 的实际音频能量驱动 `ParamMouthOpenY`；静音文字使用自然开合节奏。模型自带语音已移除，避免与 Vivi 重叠。
- 暂停或页面隐藏停止角色更新。轻柔动画减少摆动；切换原画销毁画布和模型资源。超时、WebGL 不可用或图形上下文丢失时回退原画，可在设置中重试。
- 画面采用无人夜景、模型、桌面前景三层，保留底部通栏字幕和右侧占卜面板。

## 接收分享包后启用

1. 阅读 [SDK 专有协议](https://www.live2d.com/eula/live2d-proprietary-software-license-agreement_en.html)、[SDK 开放协议](https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html)、[示例素材协议](https://www.live2d.com/eula/live2d-free-material-license-agreement_en.html) 和 [模型条款](https://www.live2d.com/en/learn/sample/model-terms/)，确认自己的组织与用途符合要求。
2. 执行 `node scripts/vendor-live2d.mjs --accept-live2d-license`。这个参数表示执行者已经同意协议，脚本不会代替执行者作决定。
3. 执行 `npm run dev`，用本地网址打开，设置中的「角色形象」选择「Live2D 示例 · Haru」。

脚本固定第三方版本与模型提交，获取 Haru 所需 22 项资源，移除示例声音引用，并保留来源、文件大小、哈希和许可证。Core 只从官方远程地址加载，脚本取其内容计算校验信息但不保存运行库文件。分享目录不含 `ui/live2d/`；直接打开 `demo.html` 体验原画版本。

Core 的 `/05/` 地址仍可能被官方更新。若官方更新导致完整性校验失败，应核对变化、验证兼容，再更新配置的 `coreIntegrity`；不能只为绕过错误去掉校验。

## 替换成 Z 的专属模型

至少需要 `.model3.json`、`.moc3` 与全部贴图；建议同时交付 `.physics3.json`、表情、待机和打招呼动作。`model3.json` 必须引用正确的相对路径，参数组应包含眨眼与嘴型。编辑器工程 `.cmo3` 不能直接作为网页运行模型。

只有一张 Z 的平面图还不能得到有变形网格和动作参数的 Live2D 模型。制作流程是拆分人物图层、在 Cubism 中建立变形和参数、设置物理与动作，最后导出运行文件。[官方素材拆分说明](https://docs.live2d.com/en/cubism-editor-manual/divide-the-material/) · [运行模型导出说明](https://docs.live2d.com/en/cubism-editor-manual/export-moc3-motion3-files/)

拿到文件后：

1. 放到 `ui/live2d/models/Z/`，保留模型内部目录结构。
2. 在 `ui/live2d-config.js` 修改 `modelUrl`、`label`、`layout`，以及 `parameters`、`moods`、`gestures` 的对应名称。
3. 执行 `npm run build`，检查脸部没有被占卜面板遮住、桌面遮挡自然、语音嘴型正确、暂停和切换形象正常。
4. 确认拥有 Z 模型及相关素材的部署/分发权后，再纳入对应发布或分享包。

## 文件与验证

`ui/live2d-stage.js` 管理画布和角色；`ui/live2d-config.js` 集中模型差异。`scripts/vendor-live2d.mjs` 获取经过确认的资源；`scripts/live2d-assets.mjs` 将专用公开目录加入构建白名单；`worker/public-assets.js` 提供模型二进制和缓存响应，不能读取服务器任意文件或密钥。

`npm test` 包括加载失败/取消、生命周期、嘴型和静态资源隔离的离线回归，不调用付费服务。真实外观、WebGL、动作和音画同步需在浏览器验收。
