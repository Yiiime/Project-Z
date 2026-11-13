# 清晨与傍晚地图重新验收 · 2026-11-14

本轮确认范围为 **Morning 清晨 + Evening 傍晚晚霞**。复用 PR #66，已把最新 main `ff82f9eb` 合入该分支；保留 main 已有的 32 张傍晚图，接入 32 张清晨图。本轮没有重新生成图片或修改地图碰撞、路线、NPC、UI、剧情和服务。

## 静态与资源包

- [逐文件清单](asset-manifest.json)与[静态审计](asset-audit.md)：64 张 PNG 完整解码、来源 SHA256、尺寸、meta 与 GUID 检查通过，覆盖 32 个启用场景。
- `node tools/team/project-check.mjs` 通过；`node unity-sideview/Tools/check-source.mjs` 通过（6626 asset files / 3399 GUIDs / 32 build scenes）。未新建或执行额外测试套件。
- 本轮只做两个格式修正：IslandMorning 目录 meta 去尾空格，旧验收 CSV 转 LF。资源 GUID、图片字节与脚线参数不变；`git diff --check origin/main` 通过。
- 本地资源接入包 `Z-清晨与傍晚地图-20261114.zip` 共 144 项、143,993,935 字节。CRC 和逐项源文件字节比较通过。SHA256：`93001d7acd6ca25f8a8e8592178a1ad7ccb582607974f77e8132ecd29bb85a0e`。
- ZIP 包含两套运行图片、meta、Layout、两个 Atmosphere 脚本及来源/静态审计说明；不是 App 或完整 Unity 工程，未提交 ZIP 到 Git。本目录额外提供本轮运行证据。

## 本轮 Unity 实际运行

Unity 6000.3.27f1，现有主工程、独立预览分支，编辑器编译无错误。运行采用隔离存档，从首页继续到隔离进度，再通过现有 `WorldDirector.TravelToMapPoint` 实际加载每个场景。未改场景文件，没有创建测试脚本或测试工程。

32 场景各执行 **午后 → 清晨 → 傍晚 → 夜晚 → 清晨**，共 160 次采样。明细见 [runtime-matrix.json](runtime-matrix.json)，摘要见 [runtime-summary.json](runtime-summary.json)。

- 清晨全部匹配 `Art/IslandMorning/<PlaceId>`；傍晚全部匹配 `Art/IslandEvening/<PlaceId>`。
- 午后及夜晚恢复原有基础图，无上一时段变体残留。
- 所有采样的世界宽度保持一致；第二次清晨的中心、尺寸漂移均为 0。
- 居民街、公共街、码头、步道保持清晨 PNG 原始比例；指定脚线与原逻辑地面的最大误差为 `2.234e-7` 世界单位。
- 采样帧从 14030 推进到 17599，检查在正常运行帧间进行，并非静态 Inspector 读数。
- 七处重点地图的两时段画面重新捕获，1920×1080，共 14 张；截图前同时刷新 Clock 和 HUD 通知。复查中央角色落点、画面覆盖、建筑及室内地面，未发现新增拉伸、缺图或黑边。
- 游戏代码未观察到运行异常。一次截图命令因输出路径位于工程外被 Pipeline 拒绝，随后改在忽略的 Builds 目录输出；该工具错误不计为游戏运行错误。编辑器保留既有 Metal/角色 source~ 目录相关警告。
- 第一次矩阵完成后，补截图时发现 Play 已结束，故重新进入隔离进度完成截图，不把中断期间的状态计为采样。结束后停止 Play 并清除存档覆盖；4 个真实存档文件 SHA256 全部未变，997 个预先存在的修改文件字节全部未变。

## 重点画面

| 地点 | 清晨 | 傍晚 |
| --- | --- | --- |
| 居民街 | [Morning](ResidentialCenter-morning.png) | [Evening](ResidentialCenter-evening.png) |
| 公共街 | [Morning](Street-morning.png) | [Evening](Street-evening.png) |
| 码头 | [Morning](Harbor-morning.png) | [Evening](Harbor-evening.png) |
| 海滨步道 | [Morning](Boardwalk-morning.png) | [Evening](Boardwalk-evening.png) |
| 咖啡馆 | [Morning](Cafe-morning.png) | [Evening](Cafe-evening.png) |
| 森林深处 | [Morning](ForestPath2-morning.png) | [Evening](ForestPath2-evening.png) |
| 灯塔 | [Morning](Lighthouse-morning.png) | [Evening](Lighthouse-evening.png) |

## 限制

清晨长街原图只有约 2172px 宽，覆盖大场景时近景偏软；没有放大冒充高清资源。四张全景的构图与原图存在差异，脚线和世界宽度已校准，但本轮没有重新逐个手动验证所有门口与边界交互。截图保留本机现有角色/UI，左上角休息计数换行、灯塔石墙草装饰等现象未在地图范围内扩改。

本轮运行使用现有本地工程，含未提交的模型/UI 导入状态；PR 在干净交付仓库制作，未纳入这些内容。验收覆盖地图加载、时段恢复与重点画面，不等于 AI 服务、完整剧情、全部入口或独立 App 构建验收。深夜 Night 沿用已有基础图染色，不属于本次新地图资源。

## 后续授权：移除旧物件并合并

用户追加要求后，已移除街道自行车、独立告示立牌和长椅明信片，并同步清理两处场景生成入口；详情与最终画面见 [物件清理验收](prop-cleanup.md)。上文 160 次地图采样属于移除前的资源复核阶段。
