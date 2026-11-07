# 直播进程管理接线与验收

本目录是 Unity 横版工程的运行副本，规则见 [SKILL.md](../SKILL.md)，协议与初始预算见 [runtime.md](runtime.md)。源设计来自 `04_玩法策略与人设/skills/live-session-control`，本次不回写来源项目。

## 接线

1. `main-agent/config/skill-registry.json` 注册主 Agent 常驻进程 Skill；运行 `node scripts/build-agent-bundle.mjs` 更新来源锁、清单和实际 `server/shared/worker/agent-bundle-data.js`。只改 Markdown 不会改变模型输入。
2. `server/shared/worker/agent-director.js` 将 Skill 和 `host.sessionControl` 放入主持决策上下文，严格校验可选 `sessionDecision`。普通 host、预备候选及审阅仍走现有路径；预备阶段不提交进程状态。
3. `server/shared/ui/conversation-engine.js` 在实际发布提交时调用进程钩子；明确的真人停止优先于普通发言审阅。`server/session-control.mjs` 校验状态版本、真实消息来源和一次额度，持有权威阶段与截止时间。
4. 单独 `session` 模型通道处理真实玩家消息的停止/挽留语义，以及短告别生成。它复用同一主播人设与 Skill，不等普通 host 或演唱完成；`control stop_session` 按钮直接进入 P00，无需模型批准。语义通道失败不自动关播，按钮仍可用。
5. `server/session.mjs` 负责取消普通生成、音轨和待播队列，等待 Unity 的 `execution.cancel` 回执，再提交告别。`host.present` 的真实完成回执才将本场设为 ended。断线及异常到期结束会记录失败原因，不声称已播。
6. Unity 的 `LiveSessionControls.cs` 在底部输入框旁常显按钮，保留直播画面至道别结束。P00 后冻结输入和麦克风；主播主动收束时保留文字挽留入口；结束后按钮变为“返回游戏”。外层返回/Esc 沿用原即时离开语义。

## 阶段守卫

- opening → live：首段实际完成或真实玩家消息已进入互动。
- live → closing(host)：审阅通过的主持决策仍匹配版本，当前无活动、无未处理真人消息；以交流落点为依据，可以提前结束；另外满 8 分钟由宿主直接收束，不受活动或未回复消息阻塞。
- opening/live/closing → closing(player)：按钮或已识别的真人整场停止。冻结普通内容、清队列、隔离晚到结果；重复停止不改变期限。
- closing(host) → live(extension)：只有第一次、道别前、有真实来源及有限范围；最多两分钟且不得晚于整场 9 分 15 秒，不随消息/暂停/重连重置。范围完成可提前收束。
- closing → farewell：旧音轨已确认停止；主播收束提示实际完成后保留短挽留窗口，P00 不等待此窗口。
- farewell → ended：匹配告别真实完成；失败可一次文字回退，超过保护期限则结束并记录原因。ended 不恢复旧场。

## 回归入口

`npm test` 覆盖真实会话控制器、协议解析、模型请求构建、取消回执、幂等、静音/暂停、断线超时、演唱中停止、一次挽留、竞态及重启恢复。使用隔离临时目录与模拟供应商，不调用付费服务。

Unity 验收还需检查：按 Tab 打开直播；输入框旁按钮可见；点击后当前发言/歌曲停止，只显示一次相关告别；告别后显示“返回游戏”；暂停/静音时仍可停止；主播自主收尾时一次挽留有效，第二次不重开。实际模型自然度与耗时需单独试玩，不能以静态检查或模拟测试代替。

持久唱歌任务继续沿用跨会话存储规则；P00 取消本场播放并隔离结果，不谎称付费后台任务已被远端取消。

时间推进验收：虚拟时钟覆盖 1/3/5/7 分钟节点唤醒、实际主持请求收到 timing、8 分钟阻塞活动强制收束、一次挽留截断、暂停/重启不重置、10 分钟绝对结束。Unity 验收显示当前节点、下一节点和计划/硬截止剩余时间；模拟供应商只验证协议和控制流程，不证明真实模型剧情自然度。
