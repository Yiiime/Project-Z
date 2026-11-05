# Player Walk Cycle v1 — 2026-11-03

## 交付状态
8 张独立透明 PNG、4×2 Sprite Sheet 和原地循环预览已生成；新的八帧 Walk 已接入现有 Player。保留原 Idle / Jump 与位移驱动方式。
2026-11-04 解锁后，已对最终 03 / 07 原画完成 Street 实际 Play 复查：左右行走、全部八帧、起停与转向、碰墙停止、平地跳跃与落地。工程接入可用；下文仍列明原画脚轨迹、重心与旧 Idle / Jump 的差异，不将本次运行检查等同于动画美术最终验收。

## 文件
本目录：/Users/bytedance/Desktop/unity_z/ArtSource/Candidates/WalkCycle/2026-11-03/
- player_walk_v1_00.png … player_walk_v1_07.png：独立帧，1024×2048、8-bit RGBA。
- player_walk_v1_sheet.png：4096×4096、透明、4 列 2 行；上排 00–03，下排 04–07。
- player_walk_v1_preview.gif：独立原地循环预览（缩小显示，不作为高清原画）。完整周期约 490 ms，对应当前速度与步幅。
- generation-provenance.json：内置 imagegen 的所有首轮提示词、选用输出、局部修正和弃用版本记录。
- import-settings.json：精确画布、平移量、有效像素高度、哈希、PPU、Pivot 和步幅。
- Validation/：实际 Unity Play 观察记录、运行截图和检查前的本地存档备份。不是测试工程或测试脚本。

项目实际使用的图集：
/Users/bytedance/Desktop/unity_z/Assets/Seabreeze/Resources/Art/player_walk_v1_sheet.png

## 基准与制作
唯一角色身份基准：
/Users/bytedance/Desktop/unity_z/ArtSource/Candidates/PlayerReference/2026-11-03/player-side-reference-v02.png

一致性辅助检查：
/Users/bytedance/Desktop/unity_z/Assets/Seabreeze/Resources/Art/player_walk.png
/Users/bytedance/Desktop/unity_z/Assets/Seabreeze/Resources/Art/characters.png

使用内置 imagegen 逐帧绘制，随后局部修正摆臂、近远腿遮挡、支撑脚后撤距离。没有放大旧 Sprite 冒充新原画，没有修改 v02 标准参考图。黑短发、蓝灰夹克、浅内搭、深裤、浅鞋和向右侧视保持一致，没有新配饰。

生成原帧约 887×1774；最终有效人物高度按 alpha ≥ 128 计为 1537–1570 px。源图像素只平移到共同的 1024×2048 画布；没有逐帧缩放、紧裁切或重绘式脚本处理。图集为逐像素拼装。PNG 保留生成器原始 alpha。

对齐采用头部轮廓水平轴与落地鞋底，不采用包含摆动四肢的全身包围框中心。统一水平轴 x=512；鞋底实色基准 y=1888（图像上方为原点）。少量极低 alpha 的边缘像素可超出实色边界，未把它们用于基准计算。

## 帧顺序
| 编号 | 动作阶段 | 主要支撑与前景关系 |
|---|---|---|
| 00 | 左脚接触 | 近侧左腿前伸，近手后摆 |
| 01 | 下沉承重 | 左腿在前承重，右腿开始收回 |
| 02 | 经过 | 左直腿支撑，远侧右腿通过 |
| 03 | 前摆 / 上升阶段 | 左腿支撑，远侧右腿前摆 |
| 04 | 右脚接触 | 远侧右腿前伸，近手前摆 |
| 05 | 下沉承重 | 右腿在前承重，近侧左腿收回 |
| 06 | 经过 | 右直腿支撑，近侧左弯腿在前景通过 |
| 07 | 前摆 / 上升阶段 | 右腿支撑，近侧左腿向前摆，近手后摆 |

## Unity 导入与播放
- Texture Type: Sprite / Multiple。
- 固定切片：1024×2048，全矩形 Full Rect；不根据 alpha 自动裁切。
- 名称：player_walk_v1_00 … player_walk_v1_07。
- 所有 Pivot：Normalized (0.5, 0.078125)，即每格左下坐标 (512,160)。
- PPU：206.578947 = 1570 / 7.6。
- Bilinear、无 mipmap、Alpha Is Transparency、Uncompressed、Clamp、NPOT None，Max Size 8192（源图为4096，无上采样）。
- 相同命名重导入时保留 Sprite ID。
- 现有渲染比例不变：CharacterVisual playerScale = 2.6 / 7.6，再乘外层 1.17；标准人物高度约 3.042 世界单位。
- 完整八帧循环步幅：2.05 世界单位；由实际移动距离推进相位。现有移动速度仍为 4.2，满速约 2.05 个循环/秒、16.4 次换帧/秒。
- 起步与转向从经过帧相位 0.25 衔接。新八帧行走取消额外 rig bob 与行走拉伸，角色根 Transform / Rigidbody 不受 Sprite 切换影响。
- 朝向仅使用 SpriteRenderer.flipX。
- 原 Art/player_walk 的 walk_0 继续用于 Idle，walk_2 用于 Jump。新图切片数不是8时，回退旧四帧路径。
- 当前项目原本使用 C# Sprite 序列，没有 Animator / Walk AnimationClip；本轮复用该结构，没有新增 Animator 或 Root Motion。

仅手动重导入入口：
Seabreeze.Editor.SpriteSheetImporter.ImportProductionWalk(1024, 2048, 206.578947f, new UnityEngine.Vector2(.5f, .078125f));

## 核心修改范围
1. Assets/Seabreeze/Scripts/World/CharacterVisual.cs：加载八帧序列、保留旧 Idle/Jump 和回退、实际位移相位、固定渲染变换；默认周期步幅2.05，避免重建Prefab后丢失标定。
2. Assets/Seabreeze/Editor/SpriteSheetImporter.cs：新增显式固定4×2导入入口，旧 ImportPlayerWalk 行为保持。
3. Assets/Seabreeze/Resources/Prefabs/Player.prefab：仅将 playerStrideLength 从2.8调整为2.05。
4. Assets/Seabreeze/Resources/Art/player_walk_v1_sheet.png 与 .meta：新增图集与导入配置。

PlayerController.cs、UI、NPC、剧情、对话、Live2D 与场景配置未修改。没有新增依赖、测试工程或测试脚本。

## 最终版本运行复查 — 2026-11-04
- 记录目录：Validation/2026-11-04-FinalStreet/；结构化结果见 verification-summary.json。
- native-input.csv：35.00 秒、1819 个渲染帧样本；全部八个新 Sprite 均实际出现。原生 A/D 完成左右行走和转向，起步使用经过帧 02，停步返回旧 walk_0。
- 地面根节点 y 为 -0.02000282 至 -0.02000034（仅浮点/物理求解微差）；rig x/y 恒0、根 x 缩放恒1、外层视觉 x 缩放恒1.17。没有逐帧改变根节点或切换负缩放。
- jump-wall-input.csv：2065 个样本；抵达 x≈15.745 的街道边界后，持续按 D 仍有111个 Idle 样本，说明步态按实际位移停止。该文件只记录跳跃尾段，不作为完整起跳证据。
- jump-street.csv：通过已有 Up 跳跃绑定，2044 个样本中37个为 Jump，使用旧 walk_2；高度从约 -0.020 上升到1.402648，再落回约 -0.020，最终 Grounded=True、Idle/walk_0。完整截图为 jump-street.png。
- 另外在街道最右侧跳跃时能落在低边界碰撞体顶部，见 jump-native.csv；属于现有场景碰撞表现，本轮没有扩展修改场景或 PlayerController。
- street-playthrough.gif：1200×675、64帧、5.43秒，按实际截图间隔合成。截图开销使采样约12Hz，预览适合看比例和整体步态，不代表编辑器正常帧率，也不能据此断言零滑步。
- 1080p 场景中主角约245px高，和约250px的 NPC 接近；角色与街道比例协调，手脚轮廓完整。
- 结束时 Play=False、Street 未修改；恢复原 Game View 布局和本次检查前最新存档。存档 SHA-256：73cc8879efb949c36535c0fa53b79de9f2940b55a9033ab22da94f134d6acd7d。
- 最终复查 Console error=0，compilationFailed=False；未运行测试套件。

## 先前运行证据与边界（03 / 07 最后修正前）
- Unity 6000.3.27f1，URP 17.3；Editor 编译通过，Console error=0。曾出现 Metal 的 memoryless depth store 警告，没有据此修改渲染管线。
- 新资源读取8张 Sprite，尺寸与 Pivot 一致。
- 原生 D 键持续行走的 native-walk-active.csv：549 个渲染帧样本，八种新 Walk Sprite 均出现；最大物理速度4.2。
- 该段 x 范围 -9.745002 至6.983528；地面物理 y 恒为-0.0200001。rig x/y 均为0；根 x 缩放恒1，外层视觉 x 缩放恒1.17；没有反向位置跳动，停步恢复walk_0。
- 原生 A 键反向移动与 Space 跳跃已执行；Jump 仍使用旧姿态。此后对03/07两张素材做了支撑腿修正，须区分这两部分证据。
- 旧 player_walk.png、characters.png、v02 的 SHA-256 与检查前一致，未覆盖。
- 检查会临时进入 Play；停止后恢复检查前存档，避免推进用户进度。

## 仍需注意
- 这是一套已接入的 v1，不能称为逐像素脚锁定。8 帧是离散绘制，支撑脚各相位的后移间距仍有小差异，满速仍可能出现轻微脚底滑动。
- 原画已有微小重心高度差；03/07上升阶段的髋部升高不够明确，后续可在动画修型时统一重心曲线。没有用根节点抖动补偿。
- Idle / Jump 保留旧原画，和新版 Walk 在头发细节、衣褶与轮廓上仍有轻微差别。停步可以正确切回，但不是专门制作的收步过渡动画。
- 个别手指、鞋带、发梢边缘有少量彩色 alpha 残留，游戏正常尺寸下较弱，高清放大可见。
- 当前物理速度不变；缩短周期距离减少滑步会提高步频，因此是较轻快的步行。若设计目标进一步偏向舒缓，应一起确认世界移动速度与步幅，不宜只放慢动画造成滑步。
- 下一轮优先做支撑脚轨迹/重心曲线的逐帧修型与实机标定；随后统一旧Idle的线条和轮廓，再考虑新增动作。

