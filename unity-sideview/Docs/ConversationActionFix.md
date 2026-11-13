# 玩家身份、直播收话对象与持续同行（本地分支）

本轮在 `local/live-delivery-expression-20261112` 开发，不推送，不改前端布局、时间规则或任务玩法。沿用八角色稳定 ID、人设、delivery 和逐角色 expression 枚举。

## 问题与修复

| 问题 | 已查到的原因 | 本轮改动 |
| --- | --- | --- |
| 正义把玩家叫成 Z | 原请求的 speakerId 已经是 player；生成上下文没有充分区分当前收话人与既有 Z 关系，后续历史误称又强化了错误 | 输入明确 counterpart/player.is_z；角色提示和记忆提取、反思指明 player 与 z 独立。明显直接误称会拒绝并重生成一次。历史原话保留为原话，不升级为玩家身份 |
| 点名 Z 却没人回答 | 收话对象与实际回复关联缺失；真实旧场次连续 agent_invalid_reply 后停在重试上限 | 消息保存 speakerId、recipientIds、pendingRecipientIds，回复保存 replyTo；Z 只处理分给自己的 inbox，NPC 只核销自己实际回复的消息。点名岛民优先调度。最多六次重试，把具体校验/审阅原因带入下一轮，新消息可恢复 |
| 答应去码头却没同行 | 模型只选择第一段 move，后续行动被其他决策抢占；旧 dock 实际绑定 Street 咖啡街 | escort_player 保存最终目的地与持续状态；物理控制器持有行动，玩家聊天暂停，结束恢复，等待玩家不超时。位置绑定改为正式地图；复用 Content/IslandMap 真实出口、入口和场景加载 |

播放未开启时，不向模型提供可播放歌曲示例，不能把唱歌工具失败伪装为已答复。表情枚举、工具权限、台词审阅继续严格校验。模型仍有生成失败的可能，失败不会把未发台词写成已说过。

## 同行链路

1. Unity 从正式地图生成可达目的地 `companionRoutes`，随玩家请求发送；后端校验节点和起点，模型不能自己编路线。
2. NPC 根据人设、记忆与当前意愿填写 `info.travel_decision`：accept / decline / postpone / continue / cancel。接受必须匹配本轮 `escort_player` 动作，拒绝不触发移动。
3. 台词实际送达后提交 proposal，后端建立 pending action。wait 不会覆盖已有 pending 结果。行动期间不能被 A2A 抢占。
4. 玩家结束聊天后跟随行走；沿途靠近可再次聊天。NPC 保留实体碰撞、地面检测，普通岛民不挡住已确认同行的通行。
5. 过场要求玩家和 NPC 都靠近同一相邻出口，并在新场景匹配其真实 arrival；远距离地图跳转不会把 NPC 传送到终点。走散会记失败原因，允许随后重新邀请。
6. `action_transition` 保存实际场景、入口和坐标；它不是到达。`action_progress` 只在实际到达路线节点后推进。全部节点完成后 `action_result` 才可 succeeded；被取消后的迟到回执不能恢复行动。
7. Unity 存档保留角色物理位置和同行标记；已到达角色在本时段保留实际位置，之后日程规则照常。后台 action/info 不在前端展开显示。

## 接口增量

- 玩家请求：`companionRoutes: string[][]`；角色输入：`interaction.counterpart: {actor_id,name,role}`。
- 模型：`action={type:"escort_player",target:"dock",action_id:"escort_dock",parameters:{companion_id:"player",route:["main_street","dock"]},description:"…"}`；`info.travel_decision={choice:"accept",destination_id:"dock"}`。
- 回执：`action_transition {actionId,location,locationId,positionX,arrivalId}`；`action_progress {actionId,routeIndex,locationId}`；`action_result {actionId,succeeded,locationId,reasonCode,text}`。均使用现有持久事件队列、序号与幂等校验，不由模型签发。
- 直播 `viewer.message.payload` 可选 `recipientIds` 与 `replyToMessageId`，不传时依 @、句首/句尾称呼和上轮对象推断。角色 ID 是 z、zhengyi 等，c01 等美术 ID 不用作消息身份。
- 发布事件、`host.present`、`audience.present` 都保留 `recipientIds/replyTo`；evidenceRefs 单独保留，事实依据不等于回复对象。

## 验证

验证使用临时目录与独立服务，不读取或覆盖体验包真实存档。后端回归覆盖身份纠错、引用 Z 不误拦、同行拒绝/取消/重放、提前成功拒绝、A2A 占用、定向回复、多人分别核销和重试恢复。Unity Play Mode 使用确定性模型夹具，但角色移动、地图加载、持久化和 HTTP 回执均走真实代码。真实模型调用单独验证生成与审阅，不把夹具当作模型效果。

- `node --test tests/*.test.mjs`（MemoryLab）：74/74 通过。
- `node --test tests/*.test.mjs`（live-service）：152/152 通过。
- `node Tools/check-source.mjs`：通过资源与场景静态检查。
- `Seabreeze.Editor.ConversationActionVerification.Begin`：Unity 6000.3.27f1 编译和 Play Mode 17 项通过。玩家按游戏帧时间以正常速度移动，Z 保持同行并通过 Street → Harbor 的真实出口；中途聊天、暂停恢复、逐节点回执、到达后读档、实体阻挡失败均验证。未遍历全岛每一扇门或所有角色的每条路线。
- 真实 DeepSeek：正义原句复验保持玩家身份，Z 与正义邀请生成合法 escort；直播点名 Z 的提问和唱歌请求都在两次生成内得到通过审阅的回复。声音未启用时说明当前不能开唱，不冒充已唱完。早期试跑仍出现过 emotion 字段生成失败并在一次修复后放弃，未放宽枚举、未把失败伪装成成功；不承诺模型永不出错。

TTS、RTC、ASR 本机尚未配置，本轮不声称真实语音通过。通用任务物品执行器不属于这次同行修复。

Mac 独立包构建成功（0 errors、27 warnings；构建报告没有分类明细）。独立服务端口 4364 / 4194 启动和健康检查通过。入口为桌面「Project-Z-对话同行修复体验/启动游戏.command」，新存档与旧体验包隔离。独立包未再逐项重跑上述 17 项交互；这些交互是在同源码 Unity Play Mode 中验证。
