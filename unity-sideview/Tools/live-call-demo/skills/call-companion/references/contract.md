# 模型输入输出契约 v1

## 输入

`turnId/eventId` 由宿主生成；context 含 sessionId、callId、guest、persona、memories、event、history、task、storyCards、allowedActions、capabilities、muted。

event.source 为 guest/audience/system，来源不可由模型更改。history 只含真实玩家消息、已播前缀与已确认结果；task 的就绪产物内容在展示前被隐藏。memories 仅为 public+confirmed+当前 guest 的条目。

程序将人设作为结构数据与 system 规则共同送入现有模型；persona 内不放用户可修改的系统规则。当前事件必须是宿主选中的 event，不由模型从整场弹幕重新选人。

## 输出

```json
{
  "turnId": "宿主本轮ID",
  "eventId": "宿主选中事件ID",
  "action": "reply",
  "speech": "你已经为了这件事折腾一整天了。想继续说说，还是先聊点轻松的？",
  "mood": "warm",
  "skill": null,
  "args": null,
  "taskId": null,
  "storyId": null,
  "resumePoint": null,
  "memoryIds": []
}
```

| action | 约束 |
|---|---|
| reply | speech 1—160 字；可邀请技能、澄清、承接反馈 |
| wait | speech=null，不额外发声；此轮事件已处理，不自动重复生成 |
| request_skill | 只能回应 guest；skill 是已启用规范 ID，args 为对象，resumePoint 必填；speech 不宣称成功 |
| present_skill | taskId 必须是 ready 的当前任务；speech=null，由真实播放器/展示器完成后触发收尾 |
| offer_story | storyId 来自可用卡；speech 只作邀请，世界尚未改变 |

mood 为 warm/happy/thinking/serious。所有不使用的字段填 null。memoryIds 只引用当前输入条目。args 是内部业务协议，不直接等同供应商 API：本版仅 singing，可用 query/range；最终由 Web 宿主映射到 prepare_song 等已注册唱歌工具，不能调用未开放才艺。

## 执行保证与局限

运行时校验结构、来源、版本、任务、权限、已启用能力和引用 ID。语义上是否真的获得技能执行意愿、台词是否符合人设、是否错误解读了自述，仍依赖主 Agent 与语义验收；这些不是 JSON schema 能证明的。需要更严格执行确认时，宿主增加明确的“开始技能”按钮，并把授权范围作为真实事件传入。

模型不返回音频 URL、隐藏牌名、世界写入值或客户端控制指令。真正执行走命令接口，见交付包 docs/接入与程序协议.md。
