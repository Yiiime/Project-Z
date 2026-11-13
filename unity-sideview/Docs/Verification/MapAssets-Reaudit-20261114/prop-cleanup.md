# 地图旧物件移除 · 2026-11-14

用户在清晨与傍晚资源复核后明确要求提交并合并 PR #66，同时移除地图自行车、立牌和信。

## 实际修改

- `Street.unity` 删除 `bicycle`（旧自行车）、`noticeboard`（街角告示板）、`postcard`（长椅上的明信片）的完整 Prefab 实例，包括 WorldProp 和交互组件。场景差异只含 12 个对象及父节点 3 条引用，共 297 行删除；无 dangling fileID。
- `ProjectBuilder.cs` 停止生成三个 Marker。
- `IslandMapBuilder.cs` 在已有清理流程按三个精确 ID 删除完整旧实例，并移除两处旧物件重定位，防止重建后恢复。
- 保留导航路牌、上下 E 路口、建筑入口、长椅、邮筒、NPC、背景图内商铺菜单牌及建筑标识。未编辑图片、公共 Prefab、剧情或服务。

## Unity 实际检查

现有 Unity 6000.3.27f1 编辑器重新编译通过，0 编译错误。使用隔离存档，从首页进入街道，在清晨与傍晚分别访问旧自行车 x=-4、旧立牌 x=-2.6166666、旧明信片 x=11.716666 三处。

- 6 个运行采样中，场景交互列表和当前交互目标均没有三个已移除 ID。附近 NPC 或长椅仍可显示自己的 E 提示。
- 检查六张正常运行截图，三件程序绘制旧物件与 glint 消失；地图背景、角色和导航保留。
- 通过现有 WorldDirector 进行 Street → Cafe → Street 场景往返，两次切换均成功，返回后物件和交互没有重生。
- [8 个运行采样](prop-cleanup-runtime.json)的帧号从 28405 推进至 28704。
- 截图：[清晨旧立牌位置](prop-cleanup-morning-noticeboard.png)、[傍晚旧明信片位置](prop-cleanup-evening-postcard.png)。其余四张原始截图保留在本地忽略目录 `Builds/MapPropCleanup-20261114/`。
- Unity Console 当前 groundTruth 为 0 errors、2 warnings；工具历史缓存的一条错误是此前工程外截图路径被拒绝，不是本次游戏异常。
- `node tools/team/project-check.mjs`、`node unity-sideview/Tools/check-source.mjs`、`git diff --check` 通过。未创建测试脚本或测试工程。
- 检查结束停止 Play、清除隔离存档覆盖。主工程原有未提交内容只叠加本次三个对象删除，PR 从干净交付仓库生成。

## 范围与限制

当前 FirstDay / Memory AI 主线不依赖这三个地图 ID。历史离线模式（MemoryRuntimeConfig.Enabled=false）的“旧明信片”支线会失去地图拾取入口；其旧剧情数据和 flags 未改，当前主线不使用该入口。本次未重新验证完整第一天或 AI 服务。

旧地图验收截图是移除前基准，以上两张 prop-cleanup 截图是最终清理状态。此前交付的 ZIP 是地图图片接入包，图片字节未改；本次场景与编辑器清理以 PR / main 中的 Unity 工程为准。
