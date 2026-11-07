> 本文件保留通用/历史协议说明。当前只开放唱歌，其他才艺不可执行；交付范围以 [当前接入说明](INTEGRATION-HANDOFF.md) 为准。

# 连麦接入（2026-11-04）

交互更新（2026-11-05）：底部仅一个麦克风按钮，上麦/下麦。原 Z 对话面板改为 Z 左、玩家右的头像及气泡，最近三条包含占位符；面板最高 280px / 舞台 35%，超过三行可展开。旧进度条、阶段条、切换收音方式、独立打断按钮已移除。Z 的正在播放气泡可点击打断；低置信度、半句或超长文本才显示气泡内确认操作。

本目录对应新建的 yuxingliang2026 Sites 项目；原站点项目和域名保持不变。连麦是一人和 Z 互动的浏览器模式；其余弹幕由现有 NPC 模拟。它不是多客户端共享房间、真人排麦或 RTC 连线服务。

## 已有资产的接线

|资产|现有入口|连麦中的使用|
|---|---|---|
|主 Agent|worker/agent-director.js、main-agent/prompts/conversation-host.txt|同一个 DeepSeek host 请求增加 context.call；同一套审阅、工具白名单和回执|
|人设与发言规范|skills/moon-z-life、scripts/z-skill.mjs|原有投影继续加载，不泄露后续剧情或改写原始导入文件|
|连麦 Skill|skills/call-companion/SKILL.md 及 companionship/audience/talents/story 参考|构建时注入 Worker；浏览器不会收到完整人设/Skill。原交付协议由 CALL_HOST_ADAPTER 映射为现有 host 协议|
|Vivi 2.0|worker/volc-tts.js → ui/speech-director.js|沿用同一声线、音频队列、播放进度和取消通道；进入连麦的点击解锁原播放器|
|Live2D|ui/live2d-stage.js、ui/live2d-actions.js|继续消费 SpeechDirector/TalentDirector 的表现与播放事件；没有第二套口型时钟|
|本场记忆|ui/session-continuity.js、ConversationEngine.memory|实际公开输入和完整播放回执继续供主 Agent 使用；被打断的话只保留实际进度|
|唱歌|ui/talent-director.js、worker/singing-cloud.js|延用已验收清唱曲库和持久点歌任务；新歌曲仍需已有后台生产及试听验收，朗读不冒充唱歌|
|绘画、塔罗|ui/drawing-director.js、ui/play-engine.js|沿用原生成、展示、玩家选牌、结束回执；连麦 Skill 决定何时提议和回到原话题|

资料里的 references/memory.md 是角色记忆方法，不等于可调用的玩家记忆数据库。本次没有找到独立的已接入玩家长期记忆接口，因此只使用现有本场公开记忆。未来接入时应由服务端按认证的来客身份读取，注入可公开且有来源的条目，同时扩展审阅证据；不能用浏览器任意传来的 guestId 读取他人的私聊。

## 实际链路

1. 页面点击麦克风→ CallConversationEngine.joinCall，停止旧主持发言、清理预备回复，进入来客优先模式。
2. 上麦后启动浏览器 SpeechRecognition（zh-CN, continuous, interimResults）。设备启动或语气声不抢话；首次有效词语才进入玩家草稿气泡并在下面展示 Z 的“正在听…”。草稿不进入主持、弹幕或记忆。
3. 文字稳定约 1.2 秒后尝试停止识别；半句词尾等待 2.2 秒。只有 final、置信度合格且无残留 interim 才提交 sendMessage，改同一占位符为“想一想…”。识别器自行结束也需等待成句窗口；30 秒上限不能自动确认。低置信度、缺 final、半句、持续能量或超长文本留在气泡内让玩家继续/确认/重说。
4. 原 ConversationEngine → /api/agent：人设 + 主持协议 + 连麦方法 + 当前公开记忆 + 当前才艺状态。Worker 校验 call 的结构、剧情 ID 和状态；不能让模型自行添加工具。
5. 主 Agent 输出 speak/wait/tool → 原审阅流程 → 原 SpeechDirector 合成、播放 Vivi → 字幕与 Live2D。
6. Z 思考时仍可收音；玩家补充取消旧生成，保留两次已提交输入，并在气泡合并呈现。Z 发声时停止 ASR 防止回录；点击其气泡停止播放，保留已播前缀，取消未播片段和旧工具。tool 在前导话实际播完后执行；唱歌/绘画/塔罗真实回执唤醒同一个主 Agent，接回聊天。
7. 再次点击麦克风、文字输入、暂停、切页会取消相应识别/旧生成/旧播放。迟到的模型或识别结果不能在下一次连麦里发声；挂断恢复原直播调度。

## 内容与状态规则

- 连麦中不做下一轮自动预生成，不运行自由聊天的话题推进器；无新来客输入或工具事件时等待；来客每轮最多允许一次主 Agent 明确声明的 continue 后续步骤，保证先说后做的工具计划可落地而不形成连续独白。
- NPC 弹幕独立持续生成，目标间隔 5 秒，玩家收音、Z 思考和发声都不暂停；不再限制每轮两条。它读取双方已公开内容，不能读取草稿或待播台词。网络、模型跳过、冷却和审阅可能延长间隔，不保证固定出条。默认不能推动话题；“听听弹幕”打开 12 秒参与窗口，仍不允许替玩家确认技能、选牌或选剧情。
- 玩家输入和正在准备的语音拥有发言优先权。真实媒体播放也走唯一音频所有者，准备完成不能抢正在说话的玩家。
- 轻剧情是可选的“今晚明信片”口头小游戏（ui/call-contract.js）。主 Agent 只能提出 offer_call_story；玩家点击选项推进，revision 防止重复/旧按钮生效。跳过同样有效；不改变 Z 主线剧情，也不谎称生成了图片。
- 并行审阅只在连麦启用：观众新增及 Z 已播前缀增长可保留当前审阅；新玩家定稿、证据改写、系统/工具状态和连麦结束使票据失效，同角色票据仍互斥。非连麦继续使用原严格 revision 检查。
- 当前使用一次上麦后自动轮转的语音对话；主播发声时停止识别以避免回录，回复结束自动恢复识别。浏览器可能调用自身的在线识别服务，支持和权限因浏览器而异；不是常开麦、全双工、声学回声消除或豆包 ASR 接入。本次未新增 ASR 密钥。
- 公开页面的来客身份、服务端密钥、D1/R2、歌曲任务与站点公开权限延用既有机制。

## 验证

- call-regression.mjs：本场记忆、玩家优先、停止播放、旧任务失效、剧情选择、挂断、Worker 协议。
- call-input-regression.mjs：真实 CallConversationEngine + DOM/识别设备替身，验证延后占位、成句、半句、确认/继续、思考中补充、打断、已播前缀、三条限制、权限失败、迟到回调及切页停止。不代表真实麦克风录音验收。
- 完整原有回归，以及 singing-cloud.test.mjs。同步了旧测试夹具中遗漏的 Unity 暂停变量、唱歌构建资源和访客 cookie 续期行为。
- 旧版本已验证真实主 Agent/Vivi/Live2D 链路。本次浏览器气泡检查使用标注的本地识别/播放事件夹具；真人收音、不同设备及真实网络延迟仍需访问者授权麦克风后验收。

主要新增代码：ui/call-engine.js（调度），ui/call-ui.js（交互/语音识别），ui/call-contract.js（剧情目录），ui/call.css；后端及构建仍是原来的入口。

Web Speech 的接口与权限机制参考：https://webaudio.github.io/web-speech-api/ 。
