# 多轮示例与验收

所有 request/task/candidate ID 及状态均为测试夹具。下文的指定牌是为了验证流程而注入的测试结果，不是真实随机抽牌记录，也不是生产模式可以指定想要牌面的依据。

## 1. 观众问题，单张三选一

玩家 A：“最近换工作有点纠结，想看看我可以先调整什么。”

1. Z 简短确认问题，介绍单张玩法；程序固定本轮牌堆及三个候选。
2. 组件真实显示三个相同牌背。A 有选择权限，其他观众只能看。
3. Z：“你从这三张里选一张，我们把它当作一个思考角度。”
4. A 点击中间，服务校验并锁定。A 再点右边不改变结果。
5. 翻牌显示测试夹具中的节制正位；显示成功回执后，主 Agent 才接收牌义并解读。
6. 如果 A 只说“纠结”，主 Agent 不编造压力来源，可问：“你纠结的主要是新工作的机会，还是离开当前岗位的成本？”

等待选择的安全输入片段：

```json
{
  "question": {"origin": "viewer", "owner_id": "fixture-player-a", "message_id": "fixture-msg-1", "original_text": "换工作时我可以先调整什么？", "effective_text": "换工作时我可以先调整什么？", "confirmed": true, "context_revision": 0},
  "candidate_view": {
    "round_id": "fixture-round-1",
    "position_id": "focus",
    "candidate_set_id": "fixture-set-1",
    "display_event_id": "fixture-display-backs-1",
    "candidates": [
      {"candidate_id": "fixture-choice-a", "label": "左"},
      {"candidate_id": "fixture-choice-b", "label": "中"},
      {"candidate_id": "fixture-choice-c", "label": "右"}
    ]
  },
  "selected_slots": [],
  "revealed_cards": []
}
```

这是局部示例，完整输入还需带 contracts.md 定义的请求、任务、运行时及配置。候选输入中没有 card_id、orientation、牌名或牌图地址。普通弹幕“帮我选个好的”不赋予主播代选权限；本版应继续邀请 A 选择或取消。

## 2. 主播自己的问题

已确认经历卡：“今天 Z 花了很久准备画展，休息比计划少。”Z 提出：“今天安排得有点满，我想看看明天怎么调整节奏。”

使用 host 来源，owner_id 为 Z。候选真实展示后，主 Agent 可以选择中间：

```json
{
  "request_id": "fixture-host-request-1",
  "task_id": "fixture-host-task-1",
  "action": "choose_host",
  "reason_code": null,
  "speech": "我选中间这张。",
  "data": {
    "round_id": "fixture-host-round-1",
    "candidate_set_id": "fixture-host-set-1",
    "candidate_id": "fixture-host-choice-b",
    "expected_state_version": 3,
    "selection_request_id": "fixture-host-select-1"
  }
}
```

这是选择意图，不是选牌成功回执。主 Agent 选择时不能知道背后的牌，也不能输出“中间是节制，正好适合我”。观众点击不能修改 host 选择。

## 3. 三张牌阵，分三次选，选齐再翻

| 时点 | 前台 | 后台事实/限制 |
| --- | --- | --- |
| 第 1 轮 | 显示三张牌背，提问者选一张放入“当前处境” | 位置已锁定，但牌面仍隐藏 |
| 第 2 轮 | 新一组三张牌背，选一张放入“值得留意的因素” | 第一张不能再次成为候选；上轮未选两张按约定回到余牌末尾 |
| 第 3 轮 | 再选一张放入“可尝试的行动” | 最终三张 card_id 必须不同 |
| 选齐 | 三个牌位一起翻开 | 从同一不可变 snapshot 显示，方向和位置不可改 |
| 展示回执齐全 | Z 逐张结合处境解释，再给整体思考 | 不能在第 1 轮就说出第 2/3 张牌或让前一张解读影响后续选择 |

若只有两张图片加载成功，不能声称三张都看到了；重发同一快照，或用原牌的文字显示补全。不得重抽第三张“解决”图片问题。

## 4. 解读必须可追溯

测试前提：观众自述 F1“白天上班”，F2“晚上做作品集很累”；固定快照显示节制正位，focus 牌位；宿主已提供正式登记的调和意象条目 fixture-meaning-temperance-u-v1。

```json
{
  "request_id": "fixture-request-01",
  "task_id": "fixture-tarot-01",
  "action": "interpret",
  "reason_code": null,
  "speech": "这张节制在本次关注的角度上，我们可以借它的调和意象看看节奏。你说白天上班，晚上做作品集很累，也许可以先把准备工作拆小，留出休息，再看看一周能稳定推进多少。这不能决定你该不该换工作。你最想先调整哪一部分？",
  "data": {
    "snapshot_id": "fixture-snapshot-01",
    "context_revision": 0,
    "anchors": [
      {
        "position_id": "focus",
        "card_id": "fixture-temperance",
        "orientation": "upright",
        "meaning_ref": "fixture-meaning-temperance-u-v1",
        "fact_refs": ["F1", "F2"],
        "hypothesis": "可从调配精力的角度思考目前准备方式是否可持续。",
        "suggestion": "尝试拆分任务、安排休息，再观察实际推进情况。"
      }
    ]
  }
}
```

这段不能变成“你领导压榨你，离职后一定顺利”。前者既无自述依据，也不是牌面能证明的事。symbolism 和改编来源见 interpretation.md；示例条目 ID 需要宿主真实资料替换，不能作为已存在数据库记录使用。

## 5. 补充背景与新一轮

| 后续输入 | 正确处理 |
| --- | --- |
| “其实我不是时间不够，是不知道作品集该放什么。” | 原 snapshot 不变，context_revision 增加；承认之前关注点需要调整，结合真实岗位要求讨论准备方向 |
| “感觉不像我。” | 接受反馈，可问哪里不贴合或结束；不指责对方不够诚实 |
| “那再帮我看看感情。” | 新问题、新任务，明确说明开始另一个主题 |
| “我想重新抽一次。” | 明确新一轮并新建任务，保留旧记录；不覆盖旧牌，也不许诺重抽更准 |
| “翻牌刚才没显示，重新来一下。” | 重试同一 snapshot 的显示，不误解为重抽 |
| “你知道他是不是喜欢我吗？” | 不凭牌断言内心；可建议转为观察互动与沟通，实质改写先取得认可 |

## 6. 运行时回放验收

| 场景 | 可观察的通过条件 |
| --- | --- |
| viewer 任务等待时主播试图代抽 | 服务拒绝，牌堆和 round 不变；skill 仍等待提问者 |
| 非 owner 点击 | 返回 unauthorized_selection，无状态改变 |
| 两个并发点击不同候选 | 只有一个锁定，另一个返回已选/冲突；不能出现两个结果 |
| 重复发送相同点击 | 返回同一回执，不再次移除牌、不跳到下一轮 |
| 第 1 轮的迟到点击到达第 2 轮 | 返回 stale_selection；不能按“中间”误选第 2 轮 |
| 创建任务超时 | 按原 request_id 查询，没有生成第二副牌堆 |
| 锁定成功但回执丢失 | 按 selection_request_id 恢复原选择，牌面不变 |
| 刷新或重连 | 候选位置、方向、锁定牌位保持一致 |
| 正逆位启用 | 方向在初始持久化时确定，重显/重试不重新抽方向 |
| 图片失败 | 原牌文字展示或原快照重显，不抽新牌 |
| 三张阵未选齐/未全部展示 | 不提前解读未展示牌面 |
| 牌义条目缺失 | 保留原牌并补取固定资料/暂缓，不换牌或编引用 |
| 解读文本重试 | snapshot 不变，已播段落不重复；补充背景可以改变解释但不能改牌 |
| 观众超时不选 | 等待、暂停或明确取消，不能自动选择 |
| 取消后迟到点击/下播后迟到文本 | 不发布、不恢复已终止流程 |
| 模型/客户端收到隐藏牌名、方向、URL 或随机状态 | 判定隔离失败，暂停处理，不能只靠提示继续声称盲选 |
| 生成 NPC 反应 | 只能使用真实公开选牌、翻牌及已播解读，不能使用后台牌堆 |

结构检查只能证明文件可读和示例格式一致。随机分布、持久化、权限、并发、断线恢复及实际展示，需要接入 demo 后用事件回放验证；本交付不宣称已经通过这些工程测试。
