# 清晨与傍晚地图静态复核 · 2026-11-14

本轮针对已合入 main `ff82f9eb` 的交付分支 `task/seanlee111/morning-map-art-1113`，复核提交 `8d112c326b5d0385eed0934cd18894d2b0ff0d3f` 的磁盘文件。范围为 Morning 清晨与 Evening 傍晚晚霞；Night 深夜不属于本次资源交付。

64 张运行 PNG 全部通过本次完整解码、SHA-256、尺寸、来源清单、导入设置及场景覆盖复核。机器可读逐文件记录见 [asset-manifest.json](asset-manifest.json)。本报告只记录本轮静态事实，不引用历史 Unity 截图或旧运行报告作为本次验收；Unity 编译、Play Mode、角色落点及场景往返由独立运行验收记录说明。

## 当前文件与来源

- `IslandMorning` 与 `IslandEvening` 各 32 张，共 64 张 RGB PNG，每张都有对应 `.png.meta`，合计图片字节数 147,116,865。
- 两组地点 ID 均与当前 `EditorBuildSettings.asset` 的 32 个启用场景完全相等，无缺图、多图或孤立 PNG meta。
- 使用 Pillow 13.0.0，逐图执行 `Image.verify()`，重新打开后执行 `Image.load()` 完整解码，64/64 通过；不是仅凭 PNG 头部判断文件有效。
- 每张运行图的 SHA-256 和尺寸均与对应 `Docs/Art/Morning-20261113/manifest.json` 或 `Docs/Art/Evening-20261113/manifest.json` 的运行文件字段相符，64/64 通过。
- 对原始 `1113地图-早晨.zip` 执行 CRC 检查，通过。ZIP 内 27 张源图中的 26 张逐字节对应 28 个清晨地点；4 张补充图与本机已有生成归档和来源清单哈希相同。本轮未生成或修改图片。
- `室内9-Z父母家 1.png.png` 是唯一未绑定源图；27 张 ZIP 文件全部能归入已映射或明确未绑定两类。当前启用场景没有该房型，不按名称将其替换给其他地点。
- 白天 `LiTingHome/ZHome` 与 `ZhouHome/ZBedroom` 两组重复图的 SHA-256 本次重新计算后相同，因此两组清晨共享来源有当前底图依据；独立运行文件和 GUID 均保留。

## 导入与 GUID

64 张运行 PNG 的 meta 除 `guid` 与 `spriteID` 外完全一致：Sprite Single，PPU 120，居中 pivot，关闭 mipmap、Read/Write、NPOT 缩放及 fallback physics shape，sRGB 开启，最大纹理尺寸 8192，Bilinear、Clamp，默认平台压缩质量 100，无平台 override。清晨最大宽度 2172，傍晚最大宽度 4921，所有文件尺寸均低于配置上限。

本次检查 64 张 PNG、两个目录、Layout 与两个 Atmosphere 脚本共 69 份 meta，GUID 69/69 唯一；再对整个 `unity-sideview/Assets` 的 meta GUID 扫描，未发现任何目标 GUID 冲突。PNG 原图为不带 alpha 的 RGB。

这些是序列化导入设置和磁盘数据，不能替代 Unity 的平台纹理转换、实际 GPU 格式与显存使用检查。

## 四张清晨全景的 Layout

| 地点 | 当前白天 PNG | 清晨 PNG | 宽高比变化 | floorFromBottom | 脚线距顶 px |
| --- | --- | --- | --- | --- | --- |
| ResidentialCenter | 8300×1518 | 2172×724 | -45.13% | 0.3508287293 | 470 |
| Street | 6168×1489 | 2171×724 | -27.61% | 0.2762430939 | 524 |
| Harbor | 4651×1492 | 2171×724 | -3.81% | 0.2002762431 | 579 |
| Boardwalk | 4788×1466 | 2172×724 | -8.15% | 0.2209944751 | 564 |

`Layout.json` 恰有上述 4 个有效、无重复地点，所有值在 0–1 内；与来源清单中的 display 字段一致，换算脚线分别为 470/524/579/564 像素。

当前 `IslandAtmosphere.cs` 与旧灯塔使用的 `SceneAtmosphere.cs` 都调用 `TimeOfDayMapArtwork`。该类在 Morning 加载 `Art/IslandMorning/<PlaceId>`，从 `Art/IslandMorning/Layout` 读取上述全景脚线，保持原世界宽度并令 `scale.y = scale.x`，再将背景脚线对齐原逻辑地面。其余清晨地图与傍晚按原矩形适配；非 Morning/Evening 时段或变体缺失时先恢复原图及变换，继续现有氛围行为。以上为代码静态核对，未在本审计执行 Unity。

四张图的宽高比差异是需要该 Layout 的原因。全局脚线及外矩形对齐不能证明图内局部门口、人物鞋底、摄像机空边或时段往返正确；这些仍属于运行画面验收。

## 32 地点文件尺寸

以下均为磁盘 PNG 尺寸；完整 SHA-256、meta SHA-256 和 GUID 在 JSON 清单中。

| 地点 | Morning | Evening |
| --- | --- | --- |
| BarInterior | 1672×941 | 1672×941 |
| Beach | 1672×941 | 1672×941 |
| BeachEntrance | 1716×917 | 1716×917 |
| Boardwalk | 2172×724 | 2939×900 |
| CabinInterior | 1672×941 | 1672×940 |
| Cafe | 1672×941 | 1672×941 |
| CaptainHome | 1672×941 | 1672×941 |
| EastCrossing | 1772×887 | 1771×888 |
| FlowerShop | 1672×941 | 1672×941 |
| Forest | 1678×937 | 1679×937 |
| ForestCabin | 1800×874 | 1800×874 |
| ForestPath1 | 1789×879 | 1789×879 |
| ForestPath2 | 1774×887 | 1774×887 |
| GeneralStore | 1672×941 | 1672×941 |
| Harbor | 2171×724 | 2171×700 |
| LiTingHome | 1672×941 | 1672×941 |
| Lighthouse | 1672×941 | 1672×941 |
| PhotoStudio | 1672×941 | 1672×941 |
| PlayerHome | 1672×941 | 1672×941 |
| Research | 1672×941 | 1672×940 |
| ResearchInterior | 1672×941 | 1672×940 |
| ResidentialCenter | 2172×724 | 4921×900 |
| RestaurantInterior | 1672×940 | 1672×940 |
| Seafront | 2106×747 | 2106×747 |
| Square | 1583×994 | 1583×994 |
| Street | 2171×724 | 3728×900 |
| Summit | 1672×941 | 1672×941 |
| VisitorCenter | 1672×941 | 1672×941 |
| ZBedroom | 1672×941 | 1672×941 |
| ZHome | 1672×941 | 1672×941 |
| ZhouHome | 1672×941 | 1672×941 |
| ZiqingHome | 1672×941 | 1672×941 |

## 仅格式规范化

本轮清理 `Assets/Seabreeze/Resources/Art/IslandMorning.meta` 中 3 个空值字段后的行尾空格，并将既有 `Docs/Verification/Morning-20261113/period-matrix.csv` 的 161 个 CRLF 换为 LF。目录 GUID 保持不变；CSV 解析后的 161 行（含表头）及全部字段值保持不变。两项均只规范化文本格式，没有修改 PNG、Layout 参数或运行代码。

本轮清单同步记录规范化后的目录 meta 哈希及两份文件前后哈希。旧 CSV 不放入资源包，其既有采样内容不作为本轮运行结论。包内 meta 和本轮审计文件已同步为规范化后的版本。

## 资源接入包边界

桌面交付 ZIP 名为 `Z-清晨与傍晚地图-20261114.zip`，不提交到 Git。包内保留 `unity-sideview/...` 路径，包含 64 张运行 PNG 与 meta、两个目录 meta、Layout 与 meta、两个 Atmosphere 脚本与 meta、Morning/Evening 来源说明和 manifest、傍晚生成来源记录、两份本轮静态审计文件及包内 README，共 144 项。

这是应用于已更新至上述 main 基线的 Project-Z 的资源接入包，不是可运行 App，也不包含完整工程依赖。覆盖同名资源时须一起保留 meta 和原 GUID。Night 专用新图、个人配置、密钥、Library/Temp/Logs/Builds、生成缓存、原始 ArtSource 和历史 Unity 截图均不在包内。包内既有来源文档记录的是 2026-11-13 的制作背景，其历史验收叙述不构成本轮运行证明。
