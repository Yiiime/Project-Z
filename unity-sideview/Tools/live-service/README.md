# Unity 原生直播服务

主游戏按 Tab 打开手机，选择「Z 的直播 · 进入聊天小屋」。直播以 Unity UI Toolkit、Live2D Cubism 和 AudioSource 呈现，返回按钮或 Esc 回到探索。直播期间暂停世界时钟和玩家输入。

## 启动

需要 Unity 6000.3.27f1、Node.js 24+。

1. 在此目录执行 `npm ci --ignore-scripts`。
2. 将 `.env.example` 复制为 `.env`（或 `.env.local`），填写所需服务密钥。
3. 执行 `npm start`，或双击 `启动直播服务.command`。
4. 打开主工程 `Assets/Seabreeze/Scenes/Street.unity`，运行后从手机进入直播。

服务默认监听 `127.0.0.1:4180`，可用 `Z_UNITY_PORT` 更改端口。启动后在主工程 `UserSettings/ZLive/local-connection.json` 写入本机连接信息。该文件与 `.runtime` 均不提交。

| 配置 | 功能 |
| --- | --- |
| `DEEPSEEK_API_KEY` | 主播决策、观众、绘图和点歌需求解析 |
| `VOLC_TTS_API_KEY` | Vivi 话语、台词演绎和唱段生成 |
| `VOLC_ASR_API_KEY` | 连麦语音识别 |
| `Z_SONG_CATALOG` | 可选本地歌曲资料清单路径 |

配置读取顺序为：进程环境变量 → `.env.local` → 对应厂商的 `*配置.env` → `.env`。修改后重启直播服务并重新进入直播。

语音配置使用[豆包语音控制台的 API Key](https://console.volcengine.com/speech/new/setting/apikeys?projectName=default)。同一项目需开通语音合成大模型 2.0（`seed-tts-2.0`）、流式语音识别（`volc.seedasr.sauc.duration`）；动态唱歌还需开通端到端实时语音大模型／实时语音模型全双工服务（`volc.speech.dialog`）。方舟 `ark-` Key 和通用 AK/SK 使用各自的鉴权接口。一个语音项目 Key 可填入两个语音配置项。`Invalid X-Api-Key` 表示密钥鉴权失败；`requested resource not granted` 表示该 Key 所属项目缺少所请求服务的授权。

无供应商密钥时可验证界面、服务连接和返回探索；AI 生成与对应语音能力需要配置后使用。供应商密钥只由 Node 服务读取。

独立 Player 从 `Application.persistentDataPath/ZLive/local-connection.json` 读取同格式连接文件。部署宿主也可在 `LiveFeatureController.Start` 前调用 `Configure(serviceUrl, gameSessionToken)`。连接使用 HTTPS，回环地址允许 HTTP。当前服务面向单机开发者，使用一个本机 Bearer token；多人账号隔离和公开服务器部署需要单独接入游戏登录系统。

## 操作

界面以 1280 × 720 为参考分辨率，聊天正文、滚动弹幕和字幕随游戏画面缩放，适配 QHD 等高分辨率窗口。

- 文字输入上限 120 字。发送时自动恢复暂停的直播，聊天记录显示送达和回应进度；向上查看历史时，新消息不会强制滚动到底部。
- 输入框旁的麦克风用于语音输入：点击开始录音，停顿或再次点击后转为文字并填入输入框，可编辑后点击发送。识别结果保留在当前输入框中，发送上限为 120 字。
- 右侧顶部「上麦连聊」用于上麦/下麦。上麦后自动轮流收音，Z 发言或准备回应时等待；每轮最多 20 秒。连麦发言进入对话上下文，聊天列表和画面弹幕只展示弹幕消息。连麦期间可输入最多 360 字作为对话补充；下麦后可使用语音输入。
- 首次上麦先请求系统麦克风权限，授权弹窗期间保持直播状态。编辑输入框时暂停收音；挂断、暂停、失焦和退出会释放麦克风并取消该轮识别，迟到结果不会进入后续对话。
- 设置中可切换 Haru 动态形象与 Z 原画，调整声音、音量、画面弹幕和轻柔动画，并重新开始对话。Esc 优先关闭弹窗或展开画面，再返回探索。
- 主播可以展示可选明信片话题，也可以邀请观众回应。选择只影响当前直播话题。
- 右侧顶部「点歌与试听」提交歌曲、歌手与明确片段，设置中也保留入口。后台任务跨会话保存；新音频需要确认歌曲、片段、音色、无伴奏、结尾五项并填写结论。
- 点击「听 Z 试唱」即可在直播中听唱段，角色嘴型随歌声音量变化；点歌面板自动收起，重新打开可停止试听或填写审核结论。试听期间主播只播放歌声，结束后继续聊天；试听不计入已审核歌曲的正式播放完成记录。已审核任务由主播按对话时机安排演唱。
- 曲库查询显示第一段副歌的已掌握状态。资料、已审核唱段与实际播放分别记录；当前资料查询不触发整曲下载或自动歌词对齐。
- 绘画、塔罗与台词演绎由主 Agent 调度，Unity 回传实际展示及播放结果。塔罗支持暂停选牌、三轮选牌和牌意复看；绘画支持放大和修改请求。

动态生成支持明确单段且最长 90 秒。整曲与含糊范围会要求补充说明。生成中断且结果未知时，核对原尝试后再明确重试。

## 代码与数据

- `server/shared/`：最新直播主 Agent、连麦及才艺调度；来源哈希见 `server/shared/PROVENANCE.json`。
- `server/session.mjs`、`http.mjs`：原生会话、SSE 命令、执行回执、幂等和租约。
- `server/voice/`：PCM16LE / 16 kHz / 单声道识别桥接。
- `server/singing/`：SQLite 点歌队列、生成、审核与持久播放回执。
- `server/song-library/`：按歌曲版本、片段和声音配置计算资料状态。
- `.runtime/`：本机凭据、会话、音频和 SQLite 数据；备份应在停止服务后复制完整目录。
- `tests/`：会话、网关、唱歌和 ASR 的离线回归；`npm test` 不调用付费供应商。

本地服务重启会暂停未结束会话，并使旧执行回执失效。关闭窗口或断网后执行租约到期会暂停会话。

## 在现有直播上增加内容

本服务与 `Assets/Seabreeze/UI/ZLive/` 是当前横版工程的直播实现。新增需求请描述入口、玩家动作、Z 的反应、结束条件及失败时的表现，并说明是否影响正在进行的连麦或演唱。

| 要增加或调整什么 | 实现入口（相对 `unity-sideview/`） |
| --- | --- |
| 连麦按钮、收音、识别草稿、点歌/试听/审核面板 | `Assets/Seabreeze/UI/ZLive/Runtime/LiveCompanionView.cs` |
| 发送消息、暂停/恢复、退出、状态与执行回执 | `Assets/Seabreeze/UI/ZLive/Runtime/LiveFeatureController.cs` |
| 直播布局、样式、房间控制 | `Assets/Seabreeze/UI/ZLive/Runtime/LiveFeatureView.cs`、`LiveRoomControls.cs`；`Assets/Seabreeze/UI/ZLive/Resources/live.uss` |
| 声音播放、进度与口型驱动 | `Assets/Seabreeze/UI/ZLive/Runtime/LiveAudioPlayer.cs`、`LiveCharacterPerformance.cs` |
| 手机入口及退出后恢复探索 | `Assets/Seabreeze/Scripts/UI/NativeLiveHost.cs` 及同目录手机 UI |
| 连麦轮次、话题/选项、状态规则 | `Tools/live-service/server/shared/ui/call-engine.js`、`call-contract.js`；原生协议入口为 `server/session.mjs` |
| 语音识别接入 | `Tools/live-service/server/voice/`、`server/http.mjs` |
| 点歌解析、生成、队列、审核、重试 | `Tools/live-service/server/singing/` |
| 曲库资料及片段状态 | `Tools/live-service/server/song-library/`；本地资料入口为 `Z_SONG_CATALOG` |
| 直播内演唱与试听调度 | `Tools/live-service/server/remote-talent.mjs`、`server/session.mjs` |
| 主播人设、决策和才艺安排 | `Tools/live-service/server/shared/worker/z-persona.js`、`agent-director.js` |
| 回归测试 | `Tools/live-service/tests/`；Unity 控件验证为 `Assets/Seabreeze/Editor/LiveDraftVerification.cs` |

注意：`main-agent/prompts/`、`main-agent/config/` 和 `skills/` 保留了导入的内容来源；当前 `agent-director.js` 实际导入的是 `server/shared/worker/agent-bundle-data.js`。只改来源文本不会自动更新这个运行时包。涉及提示词/技能时，应一起核对包的生成与版本来源，验证实际发给模型的内容。

调试时从 `Street.unity` 运行，按 Tab 进入手机直播。服务变更后重启服务并重新进入直播。服务日志与 Unity Console 分别定位后台失败和客户端执行失败；不要把供应商返回成功等同于角色已播放完成。

审核结论、勾选项、补充说明和重试确认会在当前面板中跨列表刷新保留；任务阶段或音频资产改变时清空，避免把上一版唱段的结论用于新音频。关闭直播后这些未提交草稿不持久保存。超长识别文本完整放入输入框，编辑到弹幕 120 字或连麦 360 字以内后才能发送。

## 多个聊天并行与汇总

每个开发聊天使用独立 Git worktree、任务分支和草稿 PR；在各自目录中遵守仓库 `AGENTS.md` 并运行 `tools/team/z start`。不要让多个聊天在同一个本地目录切换分支。当前协作工具的 `start` 固定从最新 `origin/main` 开工；依赖另一个未合并任务的新功能，应等前置 PR 合并再开工，或先明确安排依赖分支，不能假设 `start` 会保留所选功能分支作为基线。

- 按具体文件分工。例如一项修改 `server/singing/`，另一项修改 `server/voice/`；两项都需要改 `LiveCompanionView.cs` 或 `server/session.mjs` 时，指定一项统一接线。
- 每个 worktree 的依赖、Unity Library、服务 `.runtime` 和本机配置独立准备。并行启动服务使用不同的 `Z_UNITY_PORT`；各 Unity 实例打开对应 worktree 的工程。
- 阶段完成运行 `tools/team/z save --message "本次修改" --handoff "已完成、未完成、下一步"`，记录 PR 地址、文件范围和验收结果。
- 单项验收后运行 `tools/team/z ship`，仓库机器人按队列检查并合并至 `main`。依赖任务先合并，后续任务同步最新 `main` 后重新检查；红色难合并文件冲突先确认处理顺序。
- 最后一项合并后，在包含所有 PR 的最新版本做整体验收。需要修复时另开集成修复任务；检查全部 PR 为 `MERGED`，再通过 `tools/team/z done` 回到最新 `main`。聊天完成、保存成功和 PR 合并是三个不同状态。

Unity 控件回归：编辑器菜单 `Seabreeze > Verify Live Input Drafts`，或批处理调用 `-executeMethod Seabreeze.Editor.LiveDraftVerification.Run`。此检查覆盖刷新时的草稿保留、不同音频资产之间的审核隔离与超长文本，不替代真人麦克风、真实歌声和完整流程试玩。
