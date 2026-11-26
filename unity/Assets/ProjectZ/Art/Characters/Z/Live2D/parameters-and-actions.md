# Z Live2D 参数与动作参考

本文只描述资产和参考控制器的映射。参数的实际最小值、最大值和默认值以导入后的模型参数为准；不要把下面的参数名当成可以写入任意数值的公共 API。

## 模型参数白名单

Model.cdi3.json 当前列出 32 个参数：

| 类别 | 参数 |
| --- | --- |
| 头部连续动作 | ParamAngleX、ParamAngleY、ParamAngleZ |
| 身体连续动作 | ParamBodyAngleX、ParamBodyAngleY、ParamBodyAngleZ |
| 呼吸与眨眼 | ParamBreath、ParamEyeLOpen、ParamEyeROpen |
| 表情与视线 | ParamEyeSmile、ParamEyeBallX、ParamEyeBallY、ParamBrowForm、ParamMouthVariant、ParamCheek |
| 嘴部 | ParamMouthOpenY、ParamMouthForm |
| 姿势 | ParamPosePoint、ParamPoseExplain、ParamPoseWave |
| 头发 | ParamHairSideL、ParamHairSideR、ParamHairBang、ParamHairBack、ParamHairPitch、ParamHairSideL2、ParamHairSideR2、ParamHairBack2、ParamHairSideL3、ParamHairSideR3、ParamHairBack3 |
| 物理 | ParamDangle1 |

头发、物理和嘴部参数不等于参考控制器会主动写入它们。程序化动作主要写头部和身体连续参数；眨眼、呼吸、视线、表情和姿势分层处理。

## 表情与姿势

| 标签 | 文件 | 主要参数 | 说明 |
| --- | --- | --- | --- |
| neutral | 无 | 表情白名单回默认 | 中性表情 |
| smile | smile.exp3.json | ParamEyeSmile | 微笑 |
| surprise | surprise.exp3.json | ParamMouthVariant | 惊讶嘴型 |
| angry | angry.exp3.json | ParamBrowForm | 眉形负向 |
| troubled | troubled.exp3.json | ParamBrowForm | 眉形正向 |
| blush | blush.exp3.json | ParamCheek | 脸颊 |
| none | 无 | ParamPoseWave、ParamPoseExplain、ParamPosePoint 回默认 | 清除姿势 |
| wave | wave.exp3.json | ParamPoseWave | 挥手姿势切换 |
| explain | explain.exp3.json | ParamPoseExplain | 解释姿势切换 |
| point | point.exp3.json | ParamPosePoint | 指向姿势切换 |

## 程序化动作

参考控制器支持 Idle、Nod、Shake、Think、Listen、Happy。它们是代码计算的参数曲线，不是 motion3 动画，也不代表完整手臂动作。

| 预设 | 主要表现 | 预览默认表情/视线 |
| --- | --- | --- |
| Idle | 呼吸和小幅漂移 | neutral / player |
| Nod | 头部、身体 Y 轴点头 | smile / player |
| Shake | 头部、身体 X 轴摇头 | troubled / player |
| Think | Z/Y 轴偏转和身体侧倾 | troubled / away |
| Listen | 轻微侧倾和倾听 | neutral / player |
| Happy | 头部、身体回应摆动 | smile / player |

对话标签到动作的固定优先级从上到下取第一条：

~~~text
gesture=wave        → Happy
gesture=explain     → Nod
gesture=point       → Listen
expression=troubled → Think
expression=angry    → Shake
expression=smile    → Happy
其他                 → Nod
~~~

## 可选 LLM 协议

LLM 可以返回台词和轻量表演意图，但不会直接返回模型参数或逐帧动作：

~~~json
{
  "segments": [
    {"text":"见到你很开心。","expression":"smile","gesture":"wave","gaze":"player"}
  ],
  "memory_updates": []
}
~~~

可选标签是 expression：neutral/smile/surprise/angry/troubled/blush，gesture：none/wave/explain/point，gaze：player/away。Unity 或自己的客户端负责校验、复位和平滑。仅使用模型、表情和动作不需要 Key；需要 LLM 文字对话时，组员要在自己的后端配置 API Key。

## 最小调用形态

下面是参考控制器的本地调用意图，具体组件引用和生命周期需要按接收方工程调整。每段示例应按需独立调用；不要在同一回调里连续调用预览和停止：

~~~csharp
performance.PreviewMotion(ZMotionPreset.Nod);
~~~

停止预览时另行调用：

~~~csharp
performance.StopMotionPreview();
~~~

其他参数控制示例：

~~~csharp
performance.SetExpression(ZFaceExpression.Smile);
performance.SetGesture(ZGesture.Wave);
performance.SetGaze(ZGaze.Player);
performance.SetMotionAmplitudeMultiplier(1.25f);
performance.SetMotionSpeedMultiplier(0.85f);
~~~

动作幅度和速度倍率都限制在 0.25–2.0。动作幅度仍应通过模型自身参数范围计算，不能假定所有角色都使用相同角度范围。接入完成后至少检查静态画面、表情复位、姿势清除、动作切换、停止回中和参数不越界。
