# 第一天影片与客房素材

制作日期：2026-11-12。范围仅为抵岛、第一夜烟花、玩家客房与片尾，不替换已有地图美术。

## 已交付文件

| 文件 | 规格 | 内容 |
| --- | --- | --- |
| `Assets/StreamingAssets/FirstDay/arrival.mp4` | 1920×1080，30 fps，26 秒，H.264 + AAC | 码头／海滨步道／抵达，缓慢镜头移动、字幕、海风与远汽笛 |
| `Assets/StreamingAssets/FirstDay/fireworks.mp4` | 1920×1080，30 fps，25 秒，H.264 + AAC | 古树广场夜景、八组程序化烟花、重力拖尾、字幕与远处爆响 |
| `Assets/Seabreeze/Resources/FirstDay/player_room.png` | 1672×941 PNG | 独立海边客房夜景，床左、门右、海窗、行李 |
| `square_night_plate.png` | 1672×941 PNG | 烟花影片的独立夜景底板，未覆盖 Square.png |
| `Assets/Seabreeze/Scripts/UI/FirstDayCinema.cs` | Unity C# | 本地影片播放、重试／跳过、46 秒滚动片尾 |

两部影片是**有限动画场景短片**：绘制背景 + 摄影机运动／程序化烟花 + 字幕和声音，不是完整人物表演动画或 AI 生成真人视频。场景图没有被宣称为分层角色动画。

## 可复现制作

`build_films.py` 是生产配方而非测试脚本。需要 Python 3 标准库及带 `drawtext` 的 FFmpeg 9；可通过 `FFMPEG=/path/to/ffmpeg python3 ArtSource/FirstDay/build_films.py` 指定制作机二进制。`--fireworks-only` 只重建烟花影片。Unity 运行不依赖 Python／FFmpeg／任何新增插件。

背景来源：当前项目 `Assets/Seabreeze/Art/Island/Harbor.png`、`Boardwalk.png`；烟花夜景与客房使用内置 imagegen，以现有 Square.png / ZBedroom.png 作为风格和场景参考。原图全部保留。影片中的摄影机曲线、烟花粒子弹道、字幕、海风／汽笛／爆响合成由本配方原创编排。影片不含 BGM；游戏 MusicDirector 在影片期间提供音乐，避免叠加两首曲子。

## 生成提示记录

客房：`Use case: stylized-concept. Independent seaside guest bedroom at night, Japanese hand-painted anime-film environment, strict 2D side view. Bed on left centered x22%, warm bedside lamp, central window overlooking dark blue sea and tiny lighthouse, desk and chair right, suitcase beside closed entrance at x92%. Horizontal continuous walking floor at y80% from top, structural dark blue platform cross section below. Blue-orange night lighting, no people, no text, no UI. Reference ZBedroom.png for style and game scale only, do not copy exact room.`

广场：`Use case: lighting-weather. Dedicated NIGHT cinematic variant of exact ancient-tree square. Preserve tree silhouette, architecture, benches and town geometry. Clear blue-violet night, navy green leaves, warm amber window lights and streetlamps, legible shadow detail. Open dark sky upper right for later animated fireworks. No painted fireworks, no people, no text additions, no UI. Reference Square.png.`

客房实测布局：床中心 x≈0.215；地板表面 y≈0.789（从顶向下，Unity 自底向上≈0.211）；右门 x≈0.95。渲染尺寸是实际生成的 1672×941，没有把放大图片冒充更高分辨率母版。

## 播放生命周期

在持久对象上添加 `FirstDayCinema`，调用 `Initialize(GameSession)`，再调用 `PlayArrival(Action)`、`PlayFireworks(Action)` 或 `PlayCredits(Action)`。`IsPlaying` 为并发与输入门控；调用方拥有整个故事过场期间的 GameSession 输入锁，本组件不把共享 overlay bool 清零。

影片通过 StreamingAssets URL + VideoPlayer 播放，不需要打包时引用 VideoClip 导入。视频和片尾均使用非缩放时间；进入保存 timeScale，完成或 Cancel 恢复。影片完成回调最多一次；Cancel 不调用完成回调。Esc 只打开确认，不直接误跳过；准备超时和解码错误提供重试／继续故事。片尾含全部指定署名与理念，按住空格加速，或点击「明天见」确认离开。

## 验证边界

生产端已通过 ffprobe 确认两部影片的编码、时长、1920×1080 和 30 fps，并抽取画面检查。Unity 完整路线与真实解码／片尾检查由主任务统一执行；生产端静态检查不能代替 Unity 播放验收。
