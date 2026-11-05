# 主角后侧支撑候选 · 2026-11-04

独立于 `SkeletalPlayerNaturalGait` 的改进版本，未替换正式 Player。共享原来的 PSB、17 根蒙皮骨骼、6 个 SpriteSkin 及膝肘肩部权重。旧 NaturalGait 和更早 SkeletalPlayer 都保留。

## 预览

Unity 菜单：**Seabreeze → Candidates → Trailing Support → Play Comparison**。
也可打开本目录 `TrailingSupportComparison.unity` 后 Play。

- 左：新后侧支撑候选；右：未修改的 NaturalGait 基准。
- 相同角色尺度 1、同一物理运动根节点、完全相同的实际位移样本。
- `1`：1.65；`2`：2.2；`3`：原速4.2仅作参考。
- `A/D` 或方向键：手动移动；松手收脚；空格切换自动/手动。
- 自动模式行走3秒、停1秒、反向。不增加复杂转身或跳跃IK。
- 本次主验收速度为1.65和2.2，正常 timeScale=1；录像30fps。

## 曲线调整

保持步幅1.65、支撑60%、前掌接触交接相位.38、原骨盆承重曲线、实际位移驱动和现有手臂/上身曲线。

鞋底角度：0:+14，.04:+6，.12:0，.38:0，.40:-.7，.45:-1.9，.50:-4.5，.55:-10.5，.60:-24，.62:-30，.66:-18，.72:-6，.82:+2，.92:+10，1:+14。

62% 的滚动峰值已处于摆动期。让离地时仍有滚动角速度，并由接触几何解析计算 swingX/Y 起点切线，消除原来离地附近短暂重新伸膝的接缝。60% 支撑末端仍为-24°，没有恢复旧版-32°的承重姿势。

早摆动踝X（相对同侧绑定髋）：.62=-.491，.64=-.477，.68=-.38，.72=-.23。
膝关键姿势：.62=36°，.64=44°，.68=56°，.72=58°；踝Y由腿长与这些姿势求得，连续插值。

末摆动新增踝X .97=.421、.985=.403，膝角 .975=11°、.9875=10.5°、.99375=10°。生成器不再使用旧版 swing reach-floor/floor 钳制来修补原始轨迹；生成前检查4001个原始姿势的可达余量、膝角和鞋底净空。运行时数值保护仍保留，但最终记录没有不可达目标。

## 起停混合修复

实际短停重启发现新旧版本都存在一个旧边界问题：足部尚在释放，Animator已先抬高骨盆，导致两帧目标不可达。仅新版 RigFootContact 修改：

- 记录上帧实际骨盆Y和实际踝位置。
- 缓存当前Animator的踝目标，然后用与脚相同的 Smooth(release) 混合骨盆Y。
- release完成后恢复精确的 authored 骨盆；不改变稳态骨盆曲线、骨盆X或物理根节点。
- 保持世界接触锚点、顺序收脚和原有同相位重启逻辑。

## 实际结果

最终 RuntimeAudit-final 运行采样：

| 速度 | 最大前摆/相位 | 最大后伸/相位 |
|---|---|---|
| 1.65 | 26.345° / 82.16% | 10.367° / 48.15% |
| 2.2 | 26.345° / 82.20% | 10.367° / 48.31% |

原版最大后伸约6.799° / 45.4–45.5%。角度为面向坐标系中大腿相对竖直线的几何角，骨盆旋转增量为0；不是未经校准的医学髋角。

45/50/55/60% 的新版生成姿势：后伸9.55/10.23/8.21/5.86°，膝屈曲7.31/12.56/22.77/30.68°；实际运行附近样本一致，见数据文件。

- 原始曲线最小可达余量约0.002684 world（原有24.8%中期支撑）；摆动约0.002701。
- 最终运行三组记录合计：最小目标余量0.002659 world，最小有符号膝角7.097°，不可达样本0，最大端点误差约1.92e-6 world。
- 稳定支撑点水平变化不超过4.5e-6 world。统计排除出生沉降和起步释放0.2秒；不代表释放过程中每个鞋底像素完全不动。
- 短停重启两次保持原Walk相位。新版无不可达；右侧旧版同输入仍出现两帧负余量，最差-0.006074 world。
- 头部局部横向变化约0.00378 world，与基准一致；没有新增骨盆横向运动。

## 文件与重建

- `Editor/CandidateMotionBuilder.cs`：滚动、摆动轨迹、切线与原始姿势验证。
- `Scripts/RigFootContact.cs`：起步释放期间同步髋与踝；稳态接触机制沿用。
- `Scripts/RigAnimatorDriver.cs`：独立命名空间副本，运动逻辑未改。
- `Animation/PlayerRig_Walk.anim`、`PlayerRig_Idle.anim`、`PlayerRig.controller`、`MotionCalibration.json`：本候选独立动画数据。
- `Prefabs/PlayerRigCandidate.prefab`：本候选，仍引用原PSB。
- `Editor/CandidatePreviewBuilder.cs`、`Scripts/CandidatePreviewMotor.cs`、`CandidateComparisonView.cs`、`TrailingSupportComparison.unity`：独立对比入口。
- `Preview/PreviewFont.asset`：预览独立动态字体缓存，避免新标签自动写入正式共享字体。

重建仅使用 **Trailing Support → 2. Build Idle and Walk / 3. Build Comparison Scene**。不要调用旧候选的重建菜单。

## 交付与限制

证据目录：`ArtSource/Candidates/SkeletalPlayer/2026-11-04/TrailingSupport/`。
以 `trailing-support-comparison-final.mp4`、`restart-interrupted-final.mp4`、`validation-summary.json` 为最终交付。

早摆动需要从更伸展的离地姿势达到相近屈膝峰值，局部屈膝速度比原版更快；本次改善是持续后伸和接缝连续，并非所有动作都变慢。鞋底仍是刚性Foot，没有前掌弯折；原来的二维裤褶与远近腿投影差保留。既有顺序收脚仍有较明显的提脚动作，尚无预判末步缩短。中期支撑余量仍较小，不应未经重新测量就任意增加步幅/抬高骨盆。仅平地和已录制输入验证，不承诺坡地、跳跃或所有输入时序。

本轮没有测试工程、临时测试脚本、测试套件或独立Player Build。Unity编译、真实Play Mode、渲染录像和运行骨骼/接触采样构成验收证据。
