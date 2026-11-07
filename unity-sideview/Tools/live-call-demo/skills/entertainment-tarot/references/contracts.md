# 输入输出、选牌算法与状态协议

本文件是宿主接入设计，不是已有工具清单。程序准备牌面，人或主播选择位置；模型不生成牌面随机结果。一次塔罗任务只对应一个已确定的问题和牌阵。

## 1. 输入边界

| 字段 | 内容 |
| --- | --- |
| `request_id` | 同一次开局操作的稳定幂等 ID；创建重试沿用 |
| `task_id` | 服务创建后返回；创建前为 null，不由模型编造 |
| `session_id`、`session_phase` | 直播会话标识；phase 为 live/closing/ended |
| `question` | `{origin, owner_id, message_id, original_text, effective_text, confirmed, context_revision}`；origin 为 host/viewer；主播自主发起可无 message_id |
| `public_facts` | `{fact_id, text, source_ref, attribution}` 数组；只含与问题相关且可公开的信息，保留“提问者自述”等归属 |
| `spread` | `{spread_id, positions, reveal_mode}`；positions 含有序 position_id、label、meaning；reveal_mode 为 single_immediate/all_after_selection |
| `deck_config` | `{deck_id, deck_version, meaning_set_id, meaning_version, reversals_enabled}`；版本在本任务内固定 |
| `runtime` | 已接入能力、等待截止时间、提醒预算、任务状态、state_version、已播进度与已处理事件；期限由宿主计时，不由模型猜测 |
| `candidate_view` | 当前已显示候选：round_id、position_id、candidate_set_id、display_event_id、三个候选的 opaque candidate_id 与位置标签；**无牌名/正逆位/牌图** |
| `selected_slots` | 已选位置及其锁定状态；揭示前不含 card_id、牌名或正逆位 |
| `revealed_cards` | 已实际展示的 `{position_id, card_id, name, orientation, display_event_id, snapshot_id}`；orientation 为 upright/reversed |
| `meaning_entries` | 仅已揭示牌面的资料条目，字段见 interpretation.md |
| `event` | 当前真实回调，含 event_id、task_id、事件类型、相关版本及结果，不是模型自报成功 |

必须可确定问题来源和 owner_id；viewer 由宿主身份系统绑定，不能信任浏览器提交的任意身份字段。缺少配置或能力时返回 unavailable，不擅自补齐未知牌组和工具结果。

问题未被实质改写时，明确的原始点问可直接视为已确认；实质改写须收到认可再置 confirmed=true。背景补充只增加 context_revision，不改变原问题、牌阵或牌面；更换主题/牌阵需新 task_id。

主 Agent 可见输入不是后台完整任务快照。服务端隐藏字段包括：完整牌堆及顺序、候选到 card_id 的映射、全部正逆位、随机状态、未揭示的已选牌。不得通过日志、图片 URL、DOM 属性、候选 ID 编码或 NPC 上下文泄露。若宿主误传隐藏结果，暂停该轮选择并报告信息隔离错误，不能假装仍在盲选或悄悄重洗；处理方式由宿主明确告知并决定。

## 2. 牌阵与牌堆约定

默认牌阵：

- `single_focus`：一个牌位 `focus`，含义为“本次值得关注的一个角度”；single_immediate。
- `three_reflection`：依次为 `situation` 当前处境、`attention` 值得留意的因素、`action` 可尝试的行动；all_after_selection。

每轮均是三个候选选一张，三个候选不能指向同一张实体牌；最终已选牌不重复。宿主牌组必须具备唯一 card_id、显示名称、牌面资源/文字展示及对应牌义资料。不是必须绑定某一供应商或卡图风格；若要借用特定牌组的象征解释，牌义资料必须与该牌组相符。

### 推荐的首版确定性流程

1. 创建任务时，程序将可用牌堆随机排列一次；若启用逆位，同时给每张牌固定方向并保存。随机过程不能根据问题内容、玩家画像或希望得到的答案加权。
2. 准备一轮时，从剩余牌堆头部取三张；创建不暴露 card_id 的候选 ID，固定左/中/右位置、round_id 和 candidate_set_id。**保存成功后**才允许客户端显示牌背。
3. 同一组重显直接读取记录，不重新随机。三个背面样式一致，不给某个位置添加暗示结果优劣的动画或提示。
4. 选择成功后，选中的牌从本任务剩余牌堆移除，绑定当前牌位；未选中的两张按原候选顺序追加到剩余牌堆末尾，方向不变。下一轮再从头部取三张，不重新洗整个牌堆。
5. 剩余牌不足三个时停止并返回配置/状态错误，不能临时复制牌凑数。以上算法可由宿主替换，但必须在开始前固定、版本化，并保持盲选、无重复和重试不变的约束。

三张牌阵只在全部牌位锁定后生成可揭示快照。揭示前即使后台已经知道第一张，也不传给主 Agent。快照包含 snapshot_id、task_id、问题/牌阵版本、逐牌位 card_id 和 orientation；一经生成不可修改。

## 3. 选择与权限协议

客户端或主播控制器提交：

```json
{
  "task_id": "fixture-tarot-01",
  "round_id": "fixture-round-1",
  "candidate_set_id": "fixture-set-1",
  "candidate_id": "fixture-choice-b",
  "expected_state_version": 3,
  "selection_request_id": "fixture-click-001"
}
```

调用方身份从受信会话读取，不在上述数据里自称 actor_id。主播选择走 host 控制器；观众选择走 owner_id 对应的登录/游戏会话。主 Agent 无权为 viewer 任务发 choose_host。

提交时服务端在同一原子操作中校验：任务有效、当前轮次和候选组匹配、候选已展示、该 actor 是指定抽牌者、尚未锁定、版本正确、候选属于当前集合。随后锁定选择、更新剩余牌堆及 state_version。并发选择仅第一笔有效提交成功。

- 同 selection_request_id 重试：返回原回执，不再次消耗牌。
- 已锁定后的另一点击：返回 already_selected 和安全的锁定状态，不更换结果。
- 非 owner 点击：返回 unauthorized_selection，不改变任何牌堆状态。
- 旧 round/set/version 点击：返回 stale_selection；刷新当前视图，不映射成下一轮同位置。
- 客户端只显示“正在确认”直到真实回执；不能先宣称成功再让模型补结果。

## 4. 状态与恢复

| 状态 | 允许的下一步 |
| --- | --- |
| clarifying | 确认问题、牌阵与规则，尚未创建隐藏牌堆 |
| preparing | 幂等创建牌堆/准备本轮候选；结果未知先 query |
| candidates_ready | 请求显示已持久化的候选，等待显示回执 |
| awaiting_selection | 主播盲选或等待 owner 点击，不能解释牌面 |
| selection_locked | 未选齐则准备下一轮；选齐进入 ready_to_reveal |
| ready_to_reveal | 读取固定快照并请求展示，不再允许改选 |
| revealing | 等待完整展示回执，失败恢复原快照；未展示牌不解读 |
| revealed | 已全部展示，可读取对应牌义并准备解读 |
| interpreting | 主 Agent 自然解读，记录实际已播进度 |
| completed | 可以就原问题补充讨论；新一轮需新请求 |
| paused / cancelled / failed | paused 恢复同一任务；cancelled/failed 不继续发布迟到选择或解读 |

等待超时由运行时配置为继续等待、暂停或取消，绝不能自动替观众选牌。paused 不推进牌堆；恢复时使用原候选。cancelled 后收到晚到点击则拒绝。

创建超时按 request_id 查询；选牌超时按 task_id + selection_request_id 查询。结果未知期间等待，不能使用新 ID 重试造成新一副牌。后台已锁定而前端丢回执，恢复后展示原选择状态。状态损坏无法恢复时显式报告任务失败，不偷偷重新随机冒充恢复；要重开必须明确建立新任务。

翻牌失败仅重发相同 snapshot_id。图片加载失败可用同一牌名、正逆位及牌位的文字展示；文字展示也要有实际回执。三张牌仅部分可见时等待恢复全部，再进行本版整体解读。

解读生成失败只重写文本；已播过的部分不因重试重复播放。重复展示或播放回执按 event_id/utterance_id 去重。session_phase=ended 时取消未完成输出；单条“晚安”弹幕不等于下播授权事件。

## 5. Skill 输出

生产模式输出 JSON；以下 action 是意图，只有工具回执才能推进状态。主 Agent 使用 speech 自然表达，不向观众读出内部字段。

```json
{
  "request_id": "fixture-request-01",
  "task_id": "fixture-tarot-01",
  "action": "wait_selection",
  "reason_code": "awaiting_owner",
  "speech": "从这三张里选一张，我们看看这个角度。",
  "data": {
    "round_id": "fixture-round-1",
    "candidate_set_id": "fixture-set-1",
    "position_id": "focus"
  }
}
```

六个顶层字段固定存在；reason_code 正常时可为 null；speech 可为 null，表示本次不新增台词。

| action | 用途及 data |
| --- | --- |
| clarify | 需要确认的具体问题或选项；不创建牌堆 |
| prepare_task | confirmed question、owner、spread、deck_config；沿用 request_id |
| query_state | 原 request_id/task_id 和待确认操作 ID；不创建新结果 |
| prepare_candidates | task_id、下一 position_id、当前 state_version；只在获准阶段 |
| show_candidates | 已保存的安全候选视图；不含牌面 |
| choose_host | 当前 round/set/candidate ID、expected_state_version、selection_request_id；仅 host 任务且已显示候选 |
| wait_selection | 观众选择或提交确认期间等待，引用当前轮次 |
| reveal | 原 snapshot_id；只在已选齐时请求展示 |
| wait | 等待工具/显示/语音回执；重复事件无需新增 speech |
| load_meanings | 已展示牌面的 card_id、orientation、固定 meaning_set/version；不能请求未揭示牌 |
| interpret | snapshot_id、context_revision、逐牌依据 anchors、自然解读 speech；只使用已展示结果 |
| follow_up | 原 snapshot_id、补充事实/反馈 ID、更新后的 anchors 与短回应；不更换牌 |
| pause / cancel | 当前任务与真实原因；实际状态由宿主确认 |
| unavailable | 缺失能力/材料或不可继续的错误；没有虚构牌面 |

anchors 只保存可审查的依据字段，不要求输出内部推理过程：`{position_id, card_id, orientation, meaning_ref, fact_refs, hypothesis, suggestion}`。无实际背景可用时 fact_refs 为空，hypothesis 必须标为条件式；与现实无关的牌面说明不强行关联事实。候选或牌面不能由输出 invent，interpret 的 card_id 必须逐一对应 snapshot。

reason_code 示例：awaiting_owner、ambiguous_question、unsupported_spread、missing_capability、state_unknown、already_selected、stale_selection、unauthorized_selection、display_pending、meaning_missing、hidden_information_exposed、cancelled、session_ended。宿主校验失败时保持原状态，模型不能通过再次发同类意图绕过。

## 6. 与直播其他模块的边界

候选牌显示、选牌锁定、牌面实际展示和解读实际播出各有独立事件。只把已公开信息传给模拟弹幕模块；未揭示牌面不进入其上下文。后台生成解释只进入待播队列，不抢占连麦或其他才艺音轨。

本版不实现全体观众投票，不自动代理观众点击，不计算游戏热度。未来增加这些能力时需显式定义新的参与模式和权限，不复用 owner 抽牌流程伪装成观众亲选。
