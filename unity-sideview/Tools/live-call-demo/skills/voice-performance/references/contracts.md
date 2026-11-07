# 输入输出与播放协议

这是宿主内部协议，不是任何供应商的 API。模型输出下一步意图，工具和运行时提供真实回执；不能把计划写成已执行结果。

## 1. 请求上下文

| 字段 | 内容 |
| --- | --- |
| `request_id` | 宿主分配的稳定请求 ID；同一操作重试沿用 |
| `performance_id`、`revision` | 表演 ID 与非负整数版本；创建前 ID 可为 null；改词、声线、情绪、顺序、停顿或关键表演要求后递增版本 |
| `requester` | `{id, source, message_id}`；真实点题者身份与原消息 |
| `request_text` | 观众原始请求，保留原片段/角色/情绪线索 |
| `scene` | session_id、phase、topic_id；phase 为 live/closing/ended |
| `source_materials` | 已提供文本/文件/URL及访问状态；见下一节 |
| `current_voice_profile` | 主播当前日常音色身份、配置版本、模型和接口 ID；不是把全局音色改成表演音色的授权 |
| `voice_inventory` | 实际可用声线及支持语言、引擎、情绪控制、参数范围与版本；不可用/未验证需明确标注 |
| `performance_resources` | 可用表情、视线、动作资源及互斥关系；不能凭动作文字猜测资源存在 |
| `capabilities` | 检索、原片读取/转写、TTS、多角色、音频检查/编排、对齐、字幕与播放等真实能力 |
| `constraints` | 时限、最大演绎时长/文本长度、剩余重试次数、是否允许开始新表演等宿主预算 |
| `script`、`voice_bindings` | 已接受脚本及角色声线映射；未确定时为空 |
| `tasks`、`artifacts`、`checks` | 各次真实生成/编排结果及检查来源 |
| `playback_state`、`event` | 实际排队、播放和回调事件；携带关联 ID、版本和已播位置 |
| `audience_feedback` | 已公开且实际收到的反馈，含发送者、消息 ID、内容和时间 |

缺少后续材料时可先执行有依据的检索/澄清。提交生成或播放前必须具备相应能力、已接受版本和有效预算；不能擅自补出模型、voice_id、音频地址或已完成状态。

## 2. 片段定位与脚本

每条 `source_materials` 记录 `source_id, kind, source_ref, title, version_label, access_status, extracted_content, extraction_method, verified_ranges`。检索结果摘要单独标记为线索；只有实际读取/可靠转写核对过的内容进入 verified_ranges。涉及热点时另记录来源发布时间、核对时间和版本判断依据；发布日期不能替代内容核实。

脚本最低字段：

| 字段 | 含义 |
| --- | --- |
| `script_id`、`revision` | 已接受脚本及其版本，与表演 revision 一致 |
| `content_mode` | user_provided / verified_excerpt / original / adaptation |
| `source_refs` | 使用的真实素材 ID；原创可为空，不能伪造原作出处 |
| `scope` | 本次开头、结尾、角色和删改说明；观众已同意的范围 |
| `roles` | 稳定 role_id、显示名称与表演所需的身份关系；不等于 voice_id |
| `segments` | 有序台词片段，结构见下 |

每个 segment 含 `segment_id, order, role_id, spoken_text, emotion_intent, delivery_notes, pause_after_ms, source_anchor`。order 从 1 连续递增，segment_id 唯一；role_id 必须来自 roles。source_anchor 指向已读取素材的时间或文本范围；观众提供内容可引用 message/source ID；原创/改写部分明确标识，不捏造原文时间码。

`spoken_text` 只含需要发声的台词。delivery_notes 是舞台/语气提示，不直接拼接进普通 TTS 文本。pause_after_ms 是计划停顿，并非测得时长；实际编排需验证引擎是否已经生成等效停顿，避免重复插入。格式标签只有在所用引擎明确支持时由适配器编码，不能把任意括号说明直接当控制指令。

默认保留原文。必要的发音规范化单独记录显示文本和合成文本映射，不能借“修正发音”擅自改意思或补台词。

## 3. 角色与声线绑定

`voice_bindings` 每项为 `{role_id, voice_identity_id, provider, model, api_voice_id, config_version, supported_controls}`。所有角色均需映射到实际 inventory 中可用条目；同一个声音可以映射多个角色，不要求每个角色都有不同音色。

未指定声线时从 current_voice_profile 取得默认。已明确的替代声线可以是任务局部 override，不修改宿主日常配置。生成绑定配置快照；若声线被撤销、能力已失效或用户明确要求使用更新的配置，旧版本失效，更新 revision 后重做。仅日常音色切换不应偷偷改掉已确认的角色映射；表演结束后聊天使用最新日常配置。

指定情绪先存自然语言 intent，再由适配器映射已支持 controls。检查真实控制范围；不支持关键效果时返回限制/澄清。模型不能自创 `angry=100` 等参数并声称生效。

## 4. 完整演绎结果包

结果包 `bundle` 包含：

- `bundle_id, performance_id, revision, script_id`。
- `audio_manifest`：完整音频或有序片段数组，每项记录真实 artifact_id、task_id、segment_ids、voice_binding、asset_ref、format、duration_ms 和 accepted attempt。
- `timeline`：实际片段起止、停顿、可用的词/句时间信息及 provenance。未知时间为 null，不用预估伪装实测。
- `performance_cues`：每项 cue_id、segment_id、类型、已验证 resource_id、触发锚点、持续时间/结束行为及支持状态。
- `checks`：解码、台词完整和顺序、角色声线、情绪表达、提示未被读出、停顿/衔接、尾音、时长和提示资源/时间依据等检查。

每项 check 含 criterion、pass/fail/unverified、evidence_ref。基础音频/脚本检查未通过不得自动播放。可选动作缺失可以明确省略并标记 bundle 的省略项，不影响已合格的语音；如果动作是明确关键要求，则需确认替代后才能继续。

检查可来自实际试听、ASR 辅助核对、音频分析或人工评审；ASR 结果与台词差异需判断，不能把转写错误一律当漏读。工具返回 success 不等于内容检查 pass。

若逐段合成，编排器按 order 组成连续序列并保存真实时长/停顿；不能混入旧版本同名片段。新 revision 可显式复用未修改、声音配置相同且已检查的旧 artifact，记录来源和依赖，重新计算整个 timeline，不把旧片段时标直接照搬。

## 5. 输出意图

生产输出只包含当前证据支持的下一步。六个顶层字段固定存在；speech 是可供主 Agent 采用的主持短句或 null，不属于演绎脚本，不能混进配音。

```json
{
  "request_id": "fixture-request-1",
  "performance_id": null,
  "revision": 0,
  "action": "lookup_clip",
  "speech": "我先确认一下你说的是哪一段。",
  "data": {"query": "观众给出的片段线索", "reason_code": "clip_unresolved"}
}
```

| action | 条件与 data |
| --- | --- |
| lookup_clip | 片段尚不明确；检索线索、用户要求的版本/时间条件 |
| read_source | 实际找到/已提供的 source_id/ref、待读范围；不是已读声明 |
| clarify | 版本歧义、实质改编、时长/声线/关键效果限制；具体问题或选项 |
| prepare_script | 基于已读/提供/明确原创内容的脚本、content_mode、scope 和依据；宿主记录接受状态 |
| bind_performance | roles、实际可用的 voice_bindings、emotion/delivery 计划、预算 |
| generate_audio | 已接受脚本/绑定及本次 segments；不附主持开场白到台词 |
| query_task | 原任务/操作 ID，处理结果未知，不同时重开 |
| assemble_bundle | 真实音频清单、顺序、计划停顿和资源提示；宿主返回真实 timeline |
| check_bundle | bundle_id 和具体检查项，不能自报未执行的结果 |
| enqueue | 当前有效且必需检查通过的 bundle_id，仅请求排队 |
| wait | 等待工具、审核、排队或播放回执；reason_code 说明等待依据 |
| interact | 匹配 playback_id 实际完整结束后，引用 bundle、观众和真实反馈 |
| revise | 明确修改请求、base_revision、修改内容与保留项；宿主原子接受并递增版本 |
| replay | 无修改的明确重播请求，引用已合格 bundle，申请新 playback_id |
| stop | 真实停止、取消、下播或失效原因，关联 task/playback；不宣称已完整演完 |
| unavailable | 无法读取片段、能力不足、资料/预算不足或失败，给出真实原因 |

常用 reason_code：clip_unresolved、ambiguous_version、source_unavailable、transcript_uncertain、adaptation_required、voice_unavailable、effect_unsupported、awaiting_task、awaiting_review、awaiting_playback、already_handled、stale_result、cancelled、session_ended。

## 6. 音轨、时间轴与完成条件

首版正常顺序：脚本/绑定接受 → 实际任务 accepted → 音频产物 → 编排结果包 → 检查通过 → queued → playback_started → playback_completed → 画外互动。末尾 spoken_text 不包含“大家觉得怎么样”等未经确认的主持收尾，避免配音引擎先行接管互动。

配音与主播聊天共用唯一人声主音轨，运行时负责互斥，不靠提示词保证。多角色首版按脚本顺序轮流发声，默认不重叠；明确重叠对白只有实际混音与清晰度检查支持时才可采用。背景音乐按宿主混音策略处理，不额外自作主张加入音效或配乐。

字幕、表情、视线、动作按实际播放器时钟触发，不能按模型输出时刻或预计生成耗时触发。句内时间信息缺失时退化到片段边界提示；没有可用资源时不发送虚构资源 ID。互斥动作由控制器裁决，音频停止后清理旧实例的提示，防止迟到动作继续表演。

playback_completed 只表示选定完整范围已实际播放完，不能由服务端音频 done 或下载完成代替。完成事件同时匹配 session、performance、revision、bundle 和 playback_id；按 playback_id 幂等处理收尾。部分播放、中断和失败只产生对应事件，不能走成功互动分支。

## 7. 修改、重试与恢复

- 任务以 request/performance/revision/segment/attempt 关联。旧 revision 或未接受 attempt 的晚到产物不得覆盖当前结果包。
- revise 接受后撤销旧版本待播图文、声音与提示。若正在播放且要求立即改演，先确认实际停止，再播放新版本；不边播旧音频边改台词标签。
- 重演同一版本可复用已验收 bundle；仍需确认资产有效和声线权限可用，分配新 playback_id。该重播完成可触发一次新互动，重复回执不能反复触发。
- 生成未知先查询；确认失败后仅在预算内重试。重新合成可能产生不同节奏，必须重新检查并更新时间轴，不能沿用旧时标。
- 中断后续演使用真实已播位置和完整短语边界；若宿主无法可靠续播，就说明只能重播/结束，由有效请求决定。不能声称从一个猜测位置精确接上。
- 结束/取消释放主音轨，清理本表演的字幕/动作，聊天回到宿主最新日常音色；不改写全局配置。

NPC 只接收实际已播片段及可靠描述。演绎期间的反应可按规则排队，但主播回应和正式演后互动在配音结束或被有效中止后进行，不让 NPC 内容抢占表演音轨。
