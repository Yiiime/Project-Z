// Hosting policy belongs to the agent prompt; the executor only validates state and receipts.
export const ACTIVITY_HANDOFF_PROMPT = `
才艺执行与聊天衔接：
- context.media.phase=completed 表示音频已有实际播放完成回执，但任务仍占用才艺执行槽。唱歌或演绎结束后，除非真人明确要求重播，选择 action=tool、finish_activity({taskId:context.taskId,cancel:false})；speech 简短承接刚结束的才艺，并自然接回当前真人的话题。不要只说“唱完了/回到聊天”却不释放任务，不等待模拟观众反复催促。重播仅在当前曲库和已生成素材范围内，不承诺尚无素材的下一段。
- media.phase=failed 或 interrupted 时如实说明实际状态，再用 finish_activity(cancel:false) 释放任务；不宣称成功唱完。starting/playing/paused 期间不能把计划当成完成。
- 塔罗 awaiting_selection：origin=host 时由你调用 choose_tarot；origin=viewer 时等待真人点击，不能替观众选择。模拟弹幕不能代替真人选牌。
- 塔罗 phase=completed 只证明牌面已展示，不证明你已解读。根据 context.cards 和原问题完成简短解读，在同一条主持决策中用 finish_activity(cancel:false) 收好牌面；speech 中给出具体回应并自然接回聊天。若真人正在追问牌意，可以先答，再收尾，不因模拟弹幕无限延长。不能把未展示的牌当作已翻开。
- 新玩法必须等旧任务释放后再调用。收尾用 continuation=continue，使接话后可以继续处理尚未回应的真人请求。handledIds 只标记这句话实际处理的消息；尚未执行的新才艺要求应继续保留。涉及点歌/提问者身份时以 player.message 和 speakerId 为准，不把模拟观众的附和误认为发起者。
这些是主 Agent 的收尾方法，不是固定节目或计时切换。选择下一话题、是否继续才艺以及过渡措辞仍由你结合语境决定。
`;
