# 主角自然步态候选 · 2026-11-04

独立候选，尚未替换正式 Player。旧版完整保留在 `Assets/Seabreeze/Candidates/SkeletalPlayer/`。
新版 Prefab 复用旧版 `Art/player-rig.psb` 及其 SpriteSkin 网格/权重，不复制或重新绘制角色，不更改膝肘与肩部绑定。

## 预览

Unity 菜单 **Seabreeze → Candidates → Natural Gait → Play Comparison**。
建议通过此菜单进入；菜单停止播放后恢复先前的 Play 启动设置。本轮最终检查该设置为空，因此普通 Play 使用当前打开的场景。正式游戏场景与 BuildSettings 未修改。

- 左：新版自然步态；右：之前的骨骼候选。相同缩放1、同一物理根节点、同一实际位移输入。
- `A/D` 或左右方向键：手动移动；松手收脚；`Space`：自动/手动。
- `1`：候选舒缓速度1.65；`2`：候选快步2.2；`3`：原游戏速度4.2。
- 自动模式：行走3秒、停步1秒，再反向。没有新增转身动作或跳跃IK。
- 新周期步幅1.65，步频=120×实际速度/1.65：三个档位分别约120、160、305步/分钟。
- 原版步幅约2.0，在同速度下约99、132、252步/分钟。两版不强行相位同步，因为步幅不同。
- 4.2档只作原速度约束对照，不称为舒缓自然步行；正常步行推荐先评审1.65档。正式速度未修改。

## 运动设计

- 支撑60%、摆动40%；双支撑窗口合计20%。Near/Far 相差半周期，支撑与摆动使用不同曲线。
- 膝盖几何关键姿势：初触地7.7°、承重20°、中支撑7–11°、离地约40°、早摆动峰值约59°、末摆动逐步伸展。
- 骨盆独立承重曲线：每半周期重复，偏移−0.054..0 world；不再由摆动腿可达范围生成骨盆高度；无横向骨盆平移曲线。
- 摆动段指定踝X与膝角的关键姿势，以实际腿长求踝Y，再连续Hermite插值。末端保留轻量可达性保护。
- 鞋底相对绑定姿态角度：触地+14°，0.12前逐步落平，0.38后转为前掌支撑，0.60离地−32°；摆动中回正。
- Idle 呼吸位于伸直极限下方（−0.007..−0.003），避免起步混合时短暂锁膝。
- 手臂保持与对侧腿协调；幅度8°，肘腕轻微滞后；上身只有克制的姿态变化。

## 接触和生命周期

保留17根蒙皮骨骼，NearFoot/FarFoot各新增HeelContact、ForefootContact两个普通辅助Transform，没有Toe骨或新蒙皮权重。
这些辅助点依照现有鞋底轮廓定位，偏移按原图549.3421 PPU换算。

RigFootContact在Animator后、SpriteSkin前运行：

1. 初触地至足底支撑以脚跟为世界锚点；后支撑交接到前掌锚点。
2. 依据当前鞋底旋转反算踝位置，解原有两骨骼IK。Walk不动态压低骨盆。
3. 摆动由动画提供轨迹，仅保留辅助点的地面安全边界。
4. 停步沿用可取消的顺序收脚，但支撑鞋绕当前脚跟/前掌落平，不绕固定踝点硬转。
5. 镜像只作用FacingRoot；转向时使用上一帧实际姿态，不先强制并脚；ResetMotion仍清除传送前锚点。
6. Walk退出途中重新起步，用归一化相位CrossFade返回当前Walk相位，避免CycleRate变化影响秒制偏移，也避免先进入Idle再跳回固定0.25相位。

## 文件与重建

- `Editor/CandidateMotionBuilder.cs`：关键姿势、骨盆曲线、脚掌滚动、动画生成及辅助点绑定。
- `Scripts/RigFootContact.cs`：脚跟/前掌锚点、两骨IK、收脚和朝向处理。
- `Scripts/RigAnimatorDriver.cs`：实际位移驱动周期、相位读数、中断停步后续接。
- `Animation/PlayerRig_Walk.anim`、`PlayerRig_Idle.anim`、`PlayerRig.controller`：真实Transform动画，未使用整图帧切换。
- `Animation/MotionCalibration.json`：姿势采样与参数记录。
- `Prefabs/PlayerRigCandidate.prefab`：新版候选，引用旧版未改动PSB。
- `NaturalGaitComparison.unity`：仅候选对比场景，不加入正式BuildSettings。
- `CandidatePreviewMotor`、`CandidateComparisonView`、`CandidatePreviewBuilder`：仅对比预览、候选速度切换和菜单生命周期。

重建仅使用本子菜单的 `2. Build Idle and Walk`、`3. Build Comparison Scene`。原CandidateRigBuilder未改动，也不需要重建权重。

## 交付与限制

录像、逐帧PNG、运行时CSV及验证记录位于：
`ArtSource/Candidates/SkeletalPlayer/2026-11-04/NaturalGait/`。
请以 `natural-gait-comparison.mp4`、`restart-continuity.mp4` 和 `validation-summary.json` 为最终结果。
中间Preview-v1、Preview-restart、Preview-restart-final保留为诊断记录，后两者包含已修复的重启相位问题；最终重启采样为Preview-restart-v3。

本轮进行了Unity编译、真实Play Mode相机录制、接触点/相位采样，没有创建或执行测试工程/测试套件。
这些接触读数证明的是辅助点和IK端点稳定性，不等于鞋底每一个像素在所有情况都绝无滑动。

仍有限制：

- 鞋底为一个刚性Foot区，没有前掌弯折；高倍查看可感到硬底鞋推离。
- 末摆动最后约4%存在近伸直保护平台，避免膝反折；不是完整人体模拟。
- 停步仍是候选的顺序收脚，尚无完整的末步缩短/预判刹停；起步约0.12秒释放期间可能有轻微接触漂移。
- 远近腿原始髋点在侧面投影相差约0.12 world，纹理复用，原有裤褶和二维轮廓在屈膝时仍可能显硬；本轮未重做素材或蒙皮。
- 仅平地侧视验证；无坡地、复杂转身、跳跃IK承诺。

运动设计参考：
- AAPM&R: https://now.aapmr.org/biomechanics-normal-gait/
- Unity 2D Animation 13 IK: https://docs.unity3d.com/Packages/com.unity.2d.animation@13.0/manual/2DIK.html
