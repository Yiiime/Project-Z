# Unity 原生直播接入

本副本由 build-agent-bundle.mjs 打包到实际运行的 agent-bundle-data.js。主 Agent 常驻加载本 Skill，通过 sessionDecision 提交进程决策；不需要另调用才艺工具。

## 实际协议

host.sessionControl 是可信状态，包含 phase、version、closeReason、extensionUsed、extensionActive、extensionScope、remainingMs、elapsedMs，以及 timing（targetMs、hardMs、targetRemainingMs、hardRemainingMs、beat、beatLabel、directive、nextBeatInMs）。sessionDecision 可省略，提供时为 {intent:"close|stop|finish_extension",sourceMessageId:null或真人消息ID,scope:"追加范围或空字符串",reason:"简短依据"}。close 只在 live、无活动、无未处理真人消息时采用；stop 必须引用真人整场停止消息；finish_extension 只在一次延长的约定范围完成时采用。进程意图仅在发布前验证并提交，prepare 无副作用。stop 优先于普通发言审阅；明确整场停止立即收尾。

close 的 speech 是已通过审阅的简短收束承接，不带新问题；runtime 在它播完后给玩家 6 秒挽留窗口，再生成最终告别。窗口是可调初始值，不表示按沉默判定整场结束。挽留经语义识别确认后，限定最多 120 秒或 scope 完成为止，并截断到整场硬截止前 45 秒；剩余延长预算非正时拒绝；一场只能接受一次。没有有效范围不接受。

P00 按钮调用 control stop_session。每条真人消息也进入独立的 session 语义通道，表演中仍能识别整场停止；普通“晚安”或停止歌曲不等于整场停止。该通道使用同一主 Agent 和 Skill，只输出 keep/stop/extend 或生成告别，不提供新节目。

自主收束保护 45 秒；P00 收尾保护 30 秒；告别生成与审阅各有短截止，音频准备最多 5 秒后转文字；这些是初始技术预算，不是承诺延迟。普通自主收播仍以交流进展为主；满 8 分钟由宿主进入收尾，含挽留和告别最迟 10 分钟结束。时长从服务创建本场开始按墙钟计算，暂停/等待/重连不延长。截止绝对时刻持久化；维护轮询每秒检查一次（进程被系统挂起时在恢复后首次检查执行）。收播不再自动开活动，任务晚到结果隔离；持久点歌队列按既有规则保留跨场记录，本场不再播放，不把后台保留误说成已取消。

先由 execution.cancel all 的真实完成回执确认旧音轨停止，才播放告别。告别 completion 才提交 ended；断线或失败到期结束，记录超时，不能假记成功。静音时文字告别；暂停时也可收尾。重启保留阶段、期限和一次额度，未播完告别不重播，关闭旧场。

主界面输入框旁常显“结束直播”。收束时显示“正在收尾”，自主收束期间仍可发挽留消息；P00/道别时关闭新输入。结束后显示“返回游戏”。原返回游戏/Esc 是离开界面的原入口，不作为自然收播按钮；本次不改外层宿主退出脚本。

接线与验收见 [integration.md](integration.md)，仓库源码入口见 [服务说明](../../../README.md)。本副本没有新增塔罗或绘画能力，不改变当前聊天与唱歌范围。

时间节点为 0/1/3/5/7/8 分钟：确定话题、展开、推进故事、回应回收、准备收尾、收尾。每轮主持决策收到 timing；跨节点撤销未发布候选、更新版本并安排下一可用话口。已实际播放的内容不因普通节点切换被截断；8 分钟兜底仍强制取消普通内容。节点不是剧情完成证据。
