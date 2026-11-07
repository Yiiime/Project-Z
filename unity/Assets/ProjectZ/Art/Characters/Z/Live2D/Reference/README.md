# Z Live2D 参考源码

本目录的两个 .cs.txt 文件来自已验证的独立 Demo，保留为接入参考，默认不会参与 Unity 编译。它们依赖 Cubism Unity SDK；ZPerformanceController 依赖 DialogueClient.ChatSegment DTO，不能直接当成主工程即插即用脚本。

## 文件

| 文件 | 用途 | 依赖 |
| --- | --- | --- |
| ZPerformanceController.cs.txt | 表情、姿势、视线、待机和 6 种程序化动作的参考实现 | Cubism SDK、模型参数、DialogueClient.ChatSegment |
| DialogueClient.cs.txt | 本地 HTTP JSON 客户端、回合、ACK 和取消的参考实现 | Unity UnityWebRequest、后端协议 |

## 复用方式

先安装自己工程对应版本的 Cubism SDK，再把需要的参考文件复制到自己的脚本目录，确认命名空间、DTO 和生命周期后恢复 .cs 扩展名。也可以只提取参数映射和回合协议，重新写成主工程的适配器。

如果只需要本地动作预览，可以把两份参考源码一起移入自己的 Scripts 目录，只挂载表演控制器，不启动 DialogueClient，也不需要 API Key 或网络；也可以拆出表演控制器和一个最小 DTO。

如果同时使用 Animator、Cubism ExpressionController 和自定义控制器，先分配每个参数的唯一写入者。独立 Demo 的控制器会暂时停用旧 Animator，但这不是所有项目的通用集成规则。

## 不包含的能力

这里没有完整 Unity 场景、Prefab、SDK、服务端、DeepSeek Key、数据库或 Demo UI。需要文字对话时，组员可以自行实现后端，但应保持以下段落字段：

~~~json
{
  "text": "台词",
  "expression": "smile",
  "gesture": "wave",
  "gaze": "player"
}
~~~

完整 Demo 协议要求 memory_updates 字段存在，没有候选时返回空数组 []；它属于独立 Demo 的服务层能力，不是 Live2D 资产依赖。
