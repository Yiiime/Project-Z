# 岛内对话角色接入

1. 在 `Assets/Seabreeze/Resources/Memory/characters.json` 找到角色已有的 `id`。Z 使用 `z`。把该角色的 `portrait` 设置为 Resources 加载键，例如 `Memory/Portraits/Z`。
2. 将角色模型、图集、表情、动作及其依赖放进 `Assets/Seabreeze/Live2D/Models/<角色>/`。用本工程的 Cubism 导入。提交资源及同名 `.meta`，保留已有 GUID。超过 50 MB 的资产按团队约定与维护者核对 Git LFS。
3. 在 `Resources/Memory/Portraits/` 创建 Live2D Model Profile。将 `characterId` 填为同一个 ID，指定 Prefab、表情、默认态、待机与口型参数。用角色自己的模型实测各参数。贴图导入上限与对话框渲染高度分别设置。
4. 若需要聊天触发表情，核对后端允许的表情键、Profile 表情或 Animator 状态、以及从回复到画面的调用。Z 已接五种表情和四个动作。其他角色需要各自的回复映射。逐项验回复时的口型、可见表情、结束回待机，再试换角色和退出。

[Schema](character-portrait.schema.json) 定义交接 JSON 的字段。[Z 样例](z.example.json) 从本工程导出。游戏运行时使用角色名册和 Unity Profile。提交给伙伴时可用以下命令核对样例。Python 需要 PyYAML 和 jsonschema 4.18 或更高版本。

```sh
python3 unity-sideview/Tools/Live2D/validate_portrait.py --project-root . --actor z --check-example unity-sideview/Docs/Live2D/z.example.json
```

其他角色将 `--actor` 换成自己的 ID，用 `--print-json` 导出交接记录。静态校验覆盖资源声明与引用。表情表现和自然复位请用实际玩家输入框在构建包里验收。
