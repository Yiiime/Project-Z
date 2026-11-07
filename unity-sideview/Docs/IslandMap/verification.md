# 新版地图验证 — 2026-11-07

本记录只针对 `z地图` 当前 29 张素材。此前旧 3×5 素材的检查结果不作为本版证据。

| 验证 | 结果 | 证据与范围 |
| --- | --- | --- |
| 素材完整性 | 29/29 一致 | 原文件与 Unity PNG 副本 SHA-256 相同；16 室外、13 室内，`art-sources.json` |
| Unity 编辑器编译 | 0 errors / 0 warnings | Unity 6000.3.27f1，最终代码编译；URP 17.3.0 |
| 有向路由 | 66/66 PASS | `route-validation.csv`；20 条 A/D 自动切换、46 条 E；抵达目标、入口落点、物理模拟和输入恢复 |
| 连续行走 | PASS | 不松开 D、正常 timeScale=1：Harbor → Street → Square → EastCrossing；没有中途定位或按 E |
| 暂停与对话 | PASS | 暂停/对话时停在自动边界不切换；关闭后 OnTriggerStay 允许继续通行 |
| 跳跃越过边界 | PASS | Boardwalk → Restaurant；实测跳高约 1.404 world，未从触发区上方漏出 |
| 封闭末端 | PASS | 沙滩右端持续向右仍被边界约束，未掉出地图 |
| 运行中读档 | PASS | 跨地图、同地图两种情况都回到 Beach x=3；普通旅行仍使用默认入口，未误用存档坐标 |
| Play 重新启动恢复 | PASS | 从 Street 编辑场景启动，隔离存档恢复 ResidentialCenter x=7，输入解锁、无转场残留；`cold-start-validation.json` |
| 补充生命周期检查 | 13/13 PASS | `transition-validation.csv`，包括临时输入配置已恢复 |
| 静态资源检查 | PASS | `node Tools/check-source.mjs`：资产及 .meta、唯一 GUID、30 个 Build Settings 场景、无本机包依赖 |
| 全项目 C# 语法 | 262 files / 0 errors | `Z_DOTNET=… bash Tools/check.sh`；单独列出，不替代编辑器编译 |
| 运行画面 | 30 地点 | `preview-*.png`（29 新图 + 原灯塔）；已查看完整联系表，另有居民长街正常视口截图 |

## 方法与修复

路由检查在实际 Play Mode 中，以内存协程通过现有 Input System 发送 A/D 或 E。每条独立路线会先定位到源路口附近，随后用游戏输入触发切换；因此不能宣称整张地图全程手工步行已验收。连续跨三段街道另行验证，途中没有定位。

首次走查发现码头下行 E 点与自动右边界重叠：现已内移，生成器新增安全间距校验，最终 66 路线全部重跑通过。读档同时修复旧场景坐标在转场中覆盖新存档的问题；未修改核心存档格式或 GameSession。

补充输入检查曾受到编辑器失焦影响；随后使用项目既有验证方式，临时克隆 InputSettings 并允许后台输入后重跑。克隆及原配置采用 DontSave 保留以防切场景时被卸载，最终移除虚拟键盘并恢复原设置。临时检查逻辑未保存为项目脚本，生产输入配置未更改。

所有运行读写使用 `/tmp/seabreeze-current-map-20261107` 隔离存档；退出 Play 后已清除环境覆盖，不修改玩家真实存档。截图是 Unity 实际帧；RenderTexture 截图仅校正 Metal 的上下方向，未改画面内容。没有创建额外测试工程，也没有运行无关测试套件。

## 保持的边界

- Player.prefab、PlayerController、角色动画、NPC/剧情代码、GameUI/IslandMapView 均未改变。
- 世界地名 HUD 和地图菜单沿用现有实现，按用户确认留给 PR #37；此处不宣称新地图面板已完成。
- 没有连接角色记忆后端验收，因此截图保留原有“记忆服务未连接”提示。
- 新室内是可进入和返回的空间，不等于新增屋主剧情或日程。照相馆员工门、沙滩镜像字、码头未画出的纵向路口见 README 限制。
