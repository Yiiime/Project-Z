# Project Z · 横版主工程

自 2026-11-03 起，本目录为团队主工程。原 `unity/` 为旧 3D 参考工程。

## 打开与开发

1. 安装 Git、Unity Hub 和 **Unity 6000.6.7f1**；克隆整个仓库后，在 Hub 中添加本 `unity-sideview/` 目录。
2. 等待包恢复与资源导入完成，打开 `Assets/_Game/Scenes/App.unity`，按 Play。URP 固定 17.6.0，包版本以 Packages 为准。
3. 团队接入见 [协作说明](../docs/team/START-HERE.md)。先开任务分支；资源及 `.meta` 必须一起提交。场景、Prefab、动画应先认领再修改。

| 内容 | 位置 |
| --- | --- |
| 启动、输入和状态 | `Assets/_Game/Scripts/Core/` |
| 玩家移动 | `Assets/_Game/Scripts/Player/` |
| 地点切换、镜头与视差 | `Assets/_Game/Scripts/World/` |
| 对话与交互 | `Assets/_Game/Scripts/Dialogue/`、`Interaction/`、`UI/` |
| 地点场景 | `Assets/_Game/Scenes/Locations/` |
| 美术、动画 | `Assets/_Game/Art/`、`Animations/` |

## 检查边界

仓库根目录运行 `tools/team/z check`：协作规则测试、横版资源与 GUID 完整性、构建场景引用、C# 语法检查。语法检查复用仓库旧工程中的 Roslyn 工具，不运行旧 3D 玩法测试。需要 Node.js 和 .NET 8。

本次上传保持本地已跟踪工程内容；没有迁移 Library、Temp、Logs、UserSettings、构建产物或个人编辑器配置。没有升级包，也没有替换旧工程资源。上传检查不代表其他成员的 Unity 首次导入、编译或运行验收。素材现有说明和字体许可随资源保留；其余素材的来源与团队分发权限需要制作成员继续补充记录。

来源：本地 `z_from_moon`，提交 `63ab0c2`。当前无独立发布包。
