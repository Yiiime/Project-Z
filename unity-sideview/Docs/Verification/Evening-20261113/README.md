# 傍晚地图接入验收 · 2026-11-13

## 交付与保留

- #53 已由远端合并；本轮另经 #60 补齐较晚提交的动态首页、300×44 BGM 按钮、按 NPC 隔离的任务卡与黑屏期间 NPC 准备修复。夕景分支基于包含 #60 的 main `6939902`。
- 本轮只增加 32 张 `Resources/Art/IslandEvening` 贴图及配套 meta，修改 `IslandAtmosphere.cs`、`SceneAtmosphere.cs` 的时段贴图选择。不改 Scene、Prefab、移动、剧情、对话或 UI。
- PNG 原生尺寸及生成/拼接来源见 `../../Art/Evening-20261113/`。地图几何不重新生成，背景按原显示矩形贴合。

## Unity 实际运行

Unity 6000.3.27f1、URP 17.3、macOS Metal。使用独立的验证存档目录，真实存档不参与运行写入；没有创建测试工程或测试脚本文件。

1. Unity CLI `recompile`：完成，0 个编译错误、0 个编译警告。
2. 实际 Play Mode 首先显示动态首页；通过首页继续入口进入隔离的探索存档。左下 BGM 为短版布局。
3. 经现有 `WorldDirector.TravelToMapPoint`，实际加载全部 32 个启用场景。每个地点读取上午→午后→傍晚→夜晚→上午共 5 次状态，全部只有傍晚的 Sprite 路径指向 `Art/IslandEvening/`。160 条记录见 [runtime-matrix.json](runtime-matrix.json)。这是运行态读值，不是离线几何试算。
4. 贴图世界中心变化最大 0；世界显示尺寸变化最大 `0.0000038147`，为浮点计算误差。没有修改碰撞体、出生点或出口。
5. 等待转场结束和 HUD 刷新后，第二轮拍摄全部 32 个傍晚实际 Game View。此目录保留两张总览及五张重点原尺寸截图；全部截图保留在本机同名验收目录。
6. 逐图检查源图与生成图：码头黑边已移除；居民街、公共街、海滨步道接缝已由 imagegen 局部修补并拼合。咖啡馆、游客中心、船长家的运行截图中鞋底处于地板带内，没有明显悬空或落在石墙立面上。
7. 本次运行段 Console 没有新增 Error。仍出现项目既有的 Metal memoryless depth load/store 提示与 CastMotion `source~` 空目录 meta 提示；未将其归为本次美术变更，也未修改无关渲染或角色导入配置。

## 画面索引

两张总览均按从左到右、从上到下阅读：

- [总览 1](contact-1.png)：Street、Cafe、Lighthouse、ResidentialCenter；Forest、ForestCabin、Research、Summit；Harbor、Square、EastCrossing、Boardwalk；Seafront、Beach、ForestPath1、ForestPath2。
- [总览 2](contact-2.png)：BeachEntrance、CaptainHome、LiTingHome、ZiqingHome；ZHome、ZhouHome、VisitorCenter、FlowerShop；PhotoStudio、CabinInterior、ResearchInterior、BarInterior；RestaurantInterior、ZBedroom、GeneralStore、PlayerHome。

## 限制

生成式重绘并非逐像素换色：招牌小字、窗内物品、树叶、砖缝等有少量细节变化。部分室内前沿地板线有目测约 1.5%–2.5% 画高差异，已核对上述角色实际站立位置，但未逐个门口执行手动 E 交互。本轮 32 场景转场验证使用现有旅行接口，不能替代每条出口路线的完整玩家流程回归。没有新增灯光骨架、碰撞或人物，也没有更改上午/午后/夜晚的视觉规则。

独立生成主要为约 1.6K 宽；三张超宽图分块拼合后高 900，不能按拼合宽度宣称原生 8K。后续若面向高分辨率发行，优先进行超宽地图人工修字和同构图高清绘制。
