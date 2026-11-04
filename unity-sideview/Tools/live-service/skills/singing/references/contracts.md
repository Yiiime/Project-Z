# 输入输出与运行接口

以下是本技能的宿主协议设计，不是豆包 API 请求格式，也不表示本地已存在同名工具。接入者将实际工具和回调映射到此协议；没有相应能力时，skill 返回待执行操作或限制，不能编造工具调用结果。

## 输入

每次传入一个请求快照或一个真实事件，以及到目前为止的已确认结果。不需要把所有未来字段预先填满。

| 字段 | 含义与要求 |
| --- | --- |
| `request_id` | 宿主分配的稳定请求 ID；重试沿用，改歌或改范围后使用新修订号 `revision` |
| `revision` | 非负整数；迟到结果必须与当前修订匹配 |
| `requester` | `{id, source, message_id}`；source 为 `player / npc / operator`，身份来自宿主，不从昵称猜测 |
| `request_text` | 原始点歌内容；事件回调时从任务快照携带 |
| `scene` | `{session_id, phase, topic_id}`；phase 为 `live / closing / ended` |
| `constraints` | `{deadline_at, max_duration_ms, retries_remaining, allow_new_performance}`；宿主配置的时限、长度、剩余重试次数及是否允许开始演唱；缺失时不得提交生成/播放 |
| `voice_profile` | `{voice_identity_id, config_revision, display_name, provider, model, api_voice_id, mapping_verified}`；由宿主当前生效的角色音色配置提供。voice_identity_id 标识角色声线，config_revision 标识本次绑定的配置版本；实际模型及歌唱接口 ID 由宿主映射并验证。展示名称不限；尚未接入可为空，但不能据此执行 |
| `capabilities` | 已接入能力及限制：检索、片段定位、歌声生成、清唱支持、音频检查、缓存与播放等；每项区分 `available / unavailable / unverified`，不把未知当可用 |
| `confirmed` | 已确认结果集合，可逐步增加：`lookup`、`song`、`segment`、`task`、`audio`、`checks`、`playback` |
| `event` | 可选真实回调 `{event_id, type, request_id, revision, task_id, audio_id, playback_id, occurred_at, payload}`；按事件阶段携带适用 ID，其余为 null |
| `audience_feedback` | 已公开且实际收到的相关反馈，可为空；每条含稳定消息 ID、发送者 ID、内容及发生时间 |

歌曲、观众、片段、音色配置和状态均动态传入，单人无伴奏清唱是本技能的固定演唱要求。音色必须来自宿主当前角色配置，不能由弹幕或模型任意指定。缺少请求身份或原始诉求时返回 `reject`、`invalid_input`；缺少工具或控制配置时返回 `reject`、`capability_unavailable`；缺少当前音色配置、映射未经确认或该音色不可用时返回 `reject`、`voice_unavailable`。均说明当前无法执行，不伪装为歌曲不存在。

## 已确认结果的最低证据

| 结果 | 必须包含/表达 |
| --- | --- |
| `lookup` | `status=found / ambiguous / not_found / error`、来源引用、查询条件；not_found 表示在检索范围内无可靠匹配，不是证明世界上没有该歌 |
| `song` | 稳定 `song_id`、title、artist、version、检索 `evidence_refs`、当前引擎 `performable` 状态。不得自行捏造发行信息 |
| `segment` | `selection=explicit / default_chorus`、kind（chorus/verse/bridge/range/full）、`locator` 和 `evidence_refs`；locator 为段落 ID、相应版本的时间范围或引擎可接受的歌词边界之一 |
| `task` | 实际工具返回的 `task_id`、attempt、status、request_id/revision；候选生成参数不等于任务已接单 |
| `audio` | 实际返回的 `audio_id`、可用 asset_ref、duration_ms、format、对应 task_id；过期地址需由工具刷新，模型不拼接 URL |
| `checks` | 对该 audio_id 的解码、非静音、歌曲/片段、旋律/歌词、声线、无伴奏、完整结尾逐项给出 `pass / fail / unverified` 和检查来源。全部 pass 才能 enqueue；人工试听可作为来源 |
| `playback` | 宿主分配的 playback_id、关联 audio_id/task_id、状态、已播位置和真实完成时间；供应商结束音频输出不能填作 completed |

`segment.locator` 的三种形式：`{type:"section_id", value:"…"}`；`{type:"time_range", start_ms:0, end_ms:1000}`（左闭右开）；`{type:"lyric_boundary", start_ref:"…", end_ref:"…"}`。边界引用指向检索/曲库的真实位置，不把模型编造歌词当边界。定位器必须绑定当前 song/version；不能跨录音直接复用时间。

工具声称“能唱”只表示支持能力，最终音频仍需要检查。上游不能返回可靠歌曲/片段结果时，不跳过检索与定位步骤直接让生成模型猜。

## 输出

生产输出只返回 JSON，供宿主执行或交给主 Agent；不在观众可见文本中暴露内部字段。

```json
{
  "request_id": "req-song-001",
  "revision": 0,
  "decision": "lookup",
  "reason_code": null,
  "speech": null,
  "data": {
    "query": {"title": "测试歌曲甲", "artist": null, "version": null},
    "requested_segment": null
  }
}
```

顶层字段始终存在。`speech` 是可供主 Agent 采用/改写的短句或 null，不表示已经说出；`data` 根据当前 decision 返回必要参数或上下文，不重复整段输入，不含密钥。`reason_code` 正常操作为 null，异常或等待时使用简短代码。

| decision | 何时返回 | data 的最低内容 |
| --- | --- | --- |
| `lookup` | 需要真实歌曲检索 | query、requested_segment |
| `clarify` | 歌曲/版本歧义，或需要用户选择范围替代 | candidates 或 alternatives；speech 为一个简短问题 |
| `resolve_segment` | 歌曲确认且可演唱，片段尚未定位 | song_id、version、requested_segment；未指定时 strategy=`first_complete_chorus` |
| `generate` | 歌曲、片段、当前角色音色映射、能力和配置均就绪 | song_id、version、segment、voice_profile、render_constraints（下述固定值） |
| `check_audio` | 已收到真实完整音频，但尚未检查通过 | task_id、audio_id、asset_ref |
| `enqueue` | 对应音频检查全部通过且任务仍有效 | task_id、audio_id、segment、voice_profile；只申请排队，不声称已开始 |
| `wait` | 任务已接受、正在生成、等待试听、已排队、播放中或交互已处理 | task_id、可用时的 audio_id/playback_id、等待依据 |
| `interact` | 匹配播放实例正常完成，且此完成尚未处理 | task_id、audio_id、playback_id、completed_segment、requester、feedback_ids；speech 为自然承接 |
| `stop` | 请求被取消、会话结束或有效停止事件到达 | 已有任务/播放 ID；停止范围由宿主执行 |
| `reject` | 未找到歌曲、能力不足、无法定位、失败或不合格且没有可用重试 | 简短具体原因；speech 如实说明，不虚构不存在或已唱完 |

`generate.data.render_constraints` 固定为：`{"a_cappella":true,"single_voice":true,"backing_track":false,"harmony_layers":false,"spoken_intro_outro":false}`。这些是宿主内部约束，不宣称存在同名供应商参数。歌曲超长时返回 clarify/限制；不得以强制截断满足 max_duration_ms。

建议 reason_code：`invalid_input / song_not_found / lookup_failed / ambiguous_song / unsupported_song / segment_unresolved / range_unsupported / capability_unavailable / voice_unavailable / voice_changed / deadline_exceeded / audio_check_failed / interrupted / cancelled / awaiting_event / awaiting_review / already_handled`。它们不代替真实工具错误；面向观众不输出生硬错误码。

## 事件与状态约束

宿主正常顺序：检索确认 → 片段确认 → 实际任务 accepted/generating → 音频 ready → checks pass → queued → playback started → playback completed → 唱后互动。不同阶段允许终止，不能因为下一步“应该发生”就提前更新状态。

- 每个生成尝试、音频和播放实例都有各自 ID；回调同时核对 request_id、revision 及相应 ID，丢弃旧尝试、旧修订和取消任务的迟到事件。
- `playback_completed` 必须表示实际完整播放结束；中断、播放失败、部分播放只能产生相应事件。部分演唱可以自然接话，但不得走完整唱后成功分支。
- 同一播放完成回执只触发一次互动；运行时按 playback_id 标记已处理。重复事件返回 wait/already_handled，不再次说唱后话语或生成下一首。
- 在 closing 阶段是否还能开新演唱由 allow_new_performance 控制；ended 一律 stop。晚安弹幕不是停止授权事件。
- 生成中按运行时的 deadline_at 检查超时。工具失败或质量不合格时，只有 retries_remaining > 0 且请求仍有效才可重试，每次减少额度；重试不更换歌曲、范围或音色。
- 每次生成绑定 voice_identity_id 和 config_revision，任务、音频及检查记录保留该绑定；生成、排队和实际播放前均与当前角色配置核对。配置已变则 stop/voice_changed，使未播候选失效；宿主递增请求 revision、更新 voice_profile 后重新准备，不能把旧音频重新标记成新音色。播放中发生音色变更时先停止旧播放，不在半句或半首中直接切换。
- 缓存键至少包含歌曲、版本、片段、voice_identity_id、config_revision、歌唱接口 ID/模型版本及清唱约束；命中缓存后仍需确认对应检查记录有效、资产可用，不能复用另一个请求的播放完成状态或其他角色音色的音频。

## 播放与直播调度

音频准备好只进入待播队列；运行时选择话口并独占主持/歌声主音轨。开始播放前保存当前 BGM 状态并静音；正常结束、失败或中断均释放音轨。恢复时尊重用户或其他模块在演唱期间做出的新设置，不用旧快照覆盖更新后的静音/音量决定。

首次接入建议缓存完整片段、试听后再放入直播。若以后改为边生成边播放，需要另外设计流式质量控制和故障处理；不能沿用“完整检查已通过”的标记。

与模拟弹幕的交接依据是实际播出事件，可包含已播片段的内容描述及播放位置。音频生成好但未播放时，只能公开“准备中”等真实状态，不能引导 NPC 评价演唱效果。唱后主 Agent 不依赖 NPC 批次完成即可继续互动。
