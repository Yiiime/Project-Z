# 资产与依赖来源

- 场景原始参考：用户提供的 `美术资产参考/` 及本次两张附件，仅作为风格/构图参考或本地编辑输入，原始文件未覆盖。
- 新增背景与人物：内置 image_gen 生成/编辑，实际提示与结果清单见 `ArtSource/`。它们是独立栅格资产，NPC、碰撞、UI不烘焙在背景中。
- 中文字体：Google Fonts 的 Noto Sans SC，SIL Open Font License 1.1。原始字体来自 `https://github.com/google/fonts/tree/main/ofl/notosanssc`。使用 fontTools 将字体实例化为 Regular 400，未修改字形设计；许可证保留在 `Assets/Seabreeze/Resources/Fonts/OFL.txt`。
- Universal 2D 设置与 TMP Essential Resources：本机 Unity 官方安装包/包管理器随附资源，按 Unity 许可使用。
- 代码原生 UI 图标、基础角色木偶后备素材、独立小物件、海面微光、海浪/和弦/脚步声：本项目程序生成。
- Unity Pipeline：官方本地编辑器自动化工具，只服务本地开发验证。游戏运行无需连接该工具或任何模型服务。

技术实现查阅本机已安装包的源代码与官方资料：
- Unity 6 Rigidbody2D.linearVelocity API：`https://docs.unity.com/en-us/engine/6000.5/script-reference/unityengine/rigidbody2d/linearvelocity`
- Universal 2D 官方指南：`https://learn.unity.com/article/2d-in-unity-tech-art-essentials`
- Sprite Editor Data Provider API：本机 `com.unity.2d.sprite` 包 `Documentation~/DataProvider.md`。
