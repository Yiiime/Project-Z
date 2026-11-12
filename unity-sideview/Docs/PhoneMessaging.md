# 手机私聊接入点

2026-11-12。当前交付是数据和服务接入接口，消息页面仍沿用原有布局；没有新增输入框、发送按钮或模拟 AI 回复，也没有启动第二套聊天后端。

## 谁可以聊天

现有 `PhoneDataService` 新增 `ChatContacts` 与 `CanMessage(actorId)`，从当前存档逐个判断八位岛民，不要求对方仍在同一场景。

- 已有 `phone.metPeople` 记录的直接交谈对象。
- 已完整听完广场介绍、进入 `firstDay.greeted` 的角色。
- 已完成码头打招呼的 Z（`WalkToSquare` 及之后），无需等到广场再次认识。
- 仅看见、旁听、不在角色目录中的 ID，都不能借此解锁私聊。
- 新游戏、读档和记忆世界变更使用各自的相识记录，不继承上次游戏的解锁状态。

研究参考 K04「关系成为能力入口」：这里只采用“真实相识解锁新的联系渠道”，不引入关系数值、赠礼或额外成本。介绍完成记录优先于全体角色列表，避免未认识角色提前出现在未来联系人界面。

## 服务方要实现的接口

在 **Unity 主线程**为现有 `PhoneDataService` 注册一个 `IPhoneChatTransport`：

```csharp
phoneData.SetChatTransport(teamChatAdapter);
bool known = phoneData.CanMessage("liting");
PhoneChatResult result = await phoneData.SendMessageAsync("liting", text, cancellationToken);
```

适配器实现 `Task<PhoneChatReply> ReplyAsync(PhoneChatRequest request, CancellationToken cancellation)`。

请求包含 `WorldId / ThreadId / RequestId / ActorId / Text / Day / Period / Place`；`SenderId` 固定为 `player`，`Channel` 固定为 `phone_private_chat`。线程 ID 由世界与角色决定；请求 ID 每次唯一。服务端应按请求 ID 幂等处理，保留同一世界、同一角色的连续记忆，并在请求中提供真实身份验证；客户端的“已认识”检查不是服务端权限校验的替代品。

响应必须回传相同的 `RequestId / WorldId / ActorId` 和真实 AI 文本。服务端负责角色记忆、模型上下文与生成；本地适配器不应将 NPC 私有记忆、未经玩家目睹的事实或管理权限传入手机展示层。当前场景里的 `NpcConversationCoordinator` 依赖实体距离及现场锁定，不能用于远程私聊；本轮没有强行套用其当面交谈请求，也没有臆造服务端 URL。

## 成功、取消和存档

- 每个角色最多一个在途请求；不同角色可以独立请求。输入限制为 1—1000 字符，响应限制 8000 字符。
- 未注册服务返回 `Unavailable`；未知角色返回 `NotMet`；取消、超时、服务失败分别返回状态，绝不填入伪造回复。请求最多等待 45 秒。
- `CancelMessage(actorId)` 或调用方 `CancellationToken` 可以取消。替换适配器、切换存档/记忆世界、销毁组件会取消旧请求。即使服务忽略取消，迟到结果也不能写入新存档。
- 只有匹配的有效 AI 回复到达后，问答双方才一起写入 `state.phone.chatExchanges` 并调用现有 `GameSession.Save()`。失败/取消时不新增有效历史。
- `Delivered` 表示已接收真实适配器结果；`Persisted` 单独表明本地保存是否成功。若保存失败，应保留内存里的已接收回复并重试保存，不应自动重新向 AI 发送同一句话。
- 当前页面不投影 `chatExchanges`，因此本轮不改变消息前端。下一轮页面接线可以直接消费返回结果和当前存档的历史，再决定气泡、输入与未读规则。

## 移除初始提醒

删除 `StoryDirector.RefreshPhone()` 中统一记忆模式的两条固定内容：

- 「小岛通讯」：走近岛民按 E、旁听的操作提醒。
- 「月亮岛」：人物经历与剧情投递的系统说明。

它们原先每次读取都会重新生成，并非持久化来信；删除源头后新旧存档都不会再次出现，不用迁移或清空玩家数据。旧剧情模式的天气、房东及条件触发消息均保留，没有以其他默认消息替代这两条提醒。

## 验收边界

2026-11-12 已执行的 Unity 定点检查：

| 当前存档条件 | 请求结果 | 有效历史 |
| --- | --- | --- |
| 新游戏，目标角色尚未相识 | `NotMet` | 保持 0 |
| 续玩已相识八人的存档，`ChatContacts` 为 8，未配置适配器 | `Unavailable` | 保持 0 |

两条固定提醒已从生成源头移除；现有消息页面不新增联系人列表、输入框或模拟回复。上述检查覆盖解锁与未接服务边界，不代表 AI 服务已联调完成，也不等于完整消息前端验收。

成功回复的真实性、连续记忆、服务端权限及幂等性，须接入团队服务后验收。取消、超时、存档切换及迟到结果保护目前属于已实现／源码核对的规则，本轮未将其声称为真实网络验收结果。码头及广场逐个解锁的完整路线也不由这两项定点检查替代。
