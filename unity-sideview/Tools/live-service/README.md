# Unity 原生直播服务

主游戏按 Tab 打开手机，选择「Z 的直播 · 进入聊天小屋」。直播以 Unity UI Toolkit、Live2D Cubism 和 AudioSource 呈现，返回按钮或 Esc 回到探索。直播期间暂停世界时钟和玩家输入。

## 启动

需要 Unity 6000.3.27f1、Node.js 24+；下载新歌原曲还需要 FFmpeg（可用 `FFMPEG_PATH` 指定）。

1. 在此目录执行 `npm ci --ignore-scripts`。
2. 将 `.env.example` 复制为 `.env`（或 `.env.local`），填写所需服务密钥。
3. 执行 `npm start`，或双击 `启动直播服务.command`。
4. 打开主工程 `Assets/Seabreeze/Scenes/Street.unity`，运行后从手机进入直播。

服务默认监听 `127.0.0.1:4180`，可用 `Z_UNITY_PORT` 更改端口。启动后在主工程 `UserSettings/ZLive/local-connection.json` 写入本机连接信息。该文件与 `.runtime` 均不提交。

| 配置 | 功能 |
| --- | --- |
| `DEEPSEEK_API_KEY` | 主播决策、观众和点歌需求解析 |
| `VOLC_TTS_API_KEY` | Vivi 日常话语和唱段生成 |
| `VOLC_ASR_API_KEY` | 连麦语音识别 |
| `Z_SONG_CATALOG` | 可选本地歌曲资料清单路径，保留诊断查询能力 |
| `NETEASE_SESSION_FILE` | 可选私有登录文件路径，默认 `.runtime/netease-auth/session.json` |
| `FFMPEG_PATH` | 可选 FFmpeg 可执行文件路径 |
| `Z_NETEASE_AUTO_RENEW` | 默认启用登录维护；设为 `0` 关闭 |

配置读取顺序为：进程环境变量 → `.env.local` → 对应厂商的 `*配置.env` → `.env`。修改后重启直播服务并重新进入直播。

语音配置使用[豆包语音控制台的 API Key](https://console.volcengine.com/speech/new/setting/apikeys?projectName=default)。同一项目需开通语音合成大模型 2.0（`seed-tts-2.0`）、流式语音识别（`volc.seedasr.sauc.duration`）；动态唱歌还需开通端到端实时语音大模型／实时语音模型全双工服务（`volc.speech.dialog`）。方舟 `ark-` Key 和通用 AK/SK 使用各自的鉴权接口。一个语音项目 Key 可填入两个语音配置项。`Invalid X-Api-Key` 表示密钥鉴权失败；`requested resource not granted` 表示该 Key 所属项目缺少所请求服务的授权。

无供应商密钥时可验证界面、服务连接和返回探索；AI 生成与对应语音能力需要配置后使用。供应商密钥只由 Node 服务读取。

独立 Player 从 `Application.persistentDataPath/ZLive/local-connection.json` 读取同格式连接文件。部署宿主也可在 `LiveFeatureController.Start` 前调用 `Configure(serviceUrl, gameSessionToken)`。连接使用 HTTPS，回环地址允许 HTTP。默认本机模式使用一个 Bearer token。办公室模式支持六份独立访问凭据及会话隔离，Mac 包优先读取包内连接配置，详见 [办公室部署](OFFICE-LAN.md)。公开服务器部署仍需单独接入游戏登录系统。

## 操作

- 底部输入框旁常显“结束直播”。点击即按 P00 停止扩展内容，由 Z 承接上文简短道别；完成后显示“返回游戏”。暂停或静音时也可结束，静音使用文字。
- Z 自主收播以交流进展为主；收束提示播完后保留 6 秒挽留窗口，每场最多接受一次有范围的继续，最长 120 秒。玩家主动停止后不接受挽留。协议、预算和接线见 [直播进程 Skill](skills/live-session-control/SKILL.md) 与 [接入说明](skills/live-session-control/references/integration.md)。

界面以 1280 × 720 为参考分辨率，聊天正文、滚动弹幕和字幕随游戏画面缩放，适配 QHD 等高分辨率窗口。

- 文字输入上限 120 字。发送时自动恢复暂停的直播，聊天记录显示送达和回应进度；向上查看历史时，新消息不会强制滚动到底部。
- 输入框旁的麦克风用于语音输入：点击开始录音，停顿或再次点击后转为文字并填入输入框，可编辑后点击发送。识别结果保留在当前输入框中，发送上限为 120 字。
- 右侧顶部「上麦连聊」用于上麦/下麦。上麦后自动轮流收音，Z 发言或准备回应时等待；每轮最多 20 秒。连麦发言进入对话上下文，聊天列表和画面弹幕只展示弹幕消息。连麦期间可输入最多 360 字作为对话补充；下麦后可使用语音输入。
- 首次上麦先请求系统麦克风权限，授权弹窗期间保持直播状态。编辑输入框时暂停收音；挂断、暂停、失焦和退出会释放麦克风并取消该轮识别，迟到结果不会进入后续对话。
- 设置中可切换 Z Live2D 动态形象与 Z 原画，调整声音、音量、画面弹幕和轻柔动画，并重新开始对话。Esc 优先关闭弹窗或展开画面，再返回探索。
- 主播可以展示可选明信片话题，也可以邀请观众回应。选择只影响当前直播话题。
- 直接在弹幕或连麦里点歌，例如“唱一下颜人中《晚安》的副歌”。主 Agent 登记需求、回应进度，准备好后按对话时机唱；没有独立点歌/播放/试听审核面板。
- 同一版本、片段和 Z 音色命中可用缓存时直接待唱；新歌先检索网易云并保存完整原曲、歌词，再生成唱段。未指定歌手时取平台排序首个合格结果，明确指定版本时严格匹配，不换成翻唱。
- 新唱段通过文件、时长、非静音和生成模式检查后即可安排，不需要人工审核，不固定等待五分钟或24小时。后台任务跨会话保存；取消可以直接告诉 Z。结果未知的付费尝试仍需后台核对再重试，防止重复计费。
- 唱歌、说话仍共用现有音轨与口型。内部媒介事件用于释放音轨、恢复聊天，不建立唱歌业务播放回执库；任务 `scheduled` 仅表示已交给播放器，不代表唱完。

动态生成支持明确单段且最长 90 秒。整曲与含糊范围会要求补充说明。生成中断且结果未知时，核对原尝试后再明确重试。

## 代码与数据

- `server/shared/`：最新直播主 Agent、连麦及才艺调度；来源哈希见 `server/shared/PROVENANCE.json`。
- `server/session.mjs`、`http.mjs`：原生会话、SSE 命令、执行回执、幂等和租约。
- `server/voice/`：PCM16LE / 16 kHz / 单声道识别桥接。
- `server/singing/`：SQLite 点歌队列、生成、技术检查、取消与调度。
- `server/song-library/`：网易云检索、完整原曲/歌词留存、共享唱段缓存及资料状态。
- `.runtime/`：本机凭据、会话、音频和 SQLite 数据；备份应在停止服务后复制完整目录。
- `tests/`：会话、网关、唱歌和 ASR 的离线回归；`npm test` 不调用付费供应商。

本地服务重启会暂停正常互动会话，并使旧执行回执失效；正在收束/道别的旧场直接结束，已经结束的旧场不恢复。关闭窗口或断网后执行租约到期会暂停会话。

## 在现有直播上增加内容

本服务与 `Assets/Seabreeze/UI/ZLive/` 是当前横版工程的直播实现。新增需求请描述入口、玩家动作、Z 的反应、结束条件及失败时的表现，并说明是否影响正在进行的连麦或演唱。

| 要增加或调整什么 | 实现入口（相对 `unity-sideview/`） |
| --- | --- |
| 连麦按钮、收音和识别草稿 | `Assets/Seabreeze/UI/ZLive/Runtime/LiveCompanionView.cs` |
| 发送消息、暂停/恢复、退出、状态与执行回执 | `Assets/Seabreeze/UI/ZLive/Runtime/LiveFeatureController.cs` |
| 直播布局、样式、房间控制 | `Assets/Seabreeze/UI/ZLive/Runtime/LiveFeatureView.cs`、`LiveRoomControls.cs`；`Assets/Seabreeze/UI/ZLive/Resources/live.uss` |
| 声音播放、进度与口型驱动 | `Assets/Seabreeze/UI/ZLive/Runtime/LiveAudioPlayer.cs`、`LiveCharacterPerformance.cs` |
| 手机入口及退出后恢复探索 | `Assets/Seabreeze/Scripts/UI/NativeLiveHost.cs` 及同目录手机 UI |
| 连麦轮次、话题/选项、状态规则 | `Tools/live-service/server/shared/ui/call-engine.js`、`call-contract.js`；原生协议入口为 `server/session.mjs` |
| 语音识别接入 | `Tools/live-service/server/voice/`、`server/http.mjs` |
| 点歌解析、生成、队列、技术检查、重试 | `Tools/live-service/server/singing/` |
| 检索、曲库资料及片段状态 | `Tools/live-service/server/song-library/`；网易云入口为 `netease.mjs`，排序规则为 `official-retrieval.mjs` |
| 直播内演唱与音轨调度 | `Tools/live-service/server/remote-talent.mjs`、`server/session.mjs` |
| 主播人设、决策和才艺安排 | `Tools/live-service/server/shared/worker/z-persona.js`、`agent-director.js` |
| 回归测试 | `Tools/live-service/tests/`；Unity 控件验证为 `Assets/Seabreeze/Editor/LiveDraftVerification.cs` |

注意：`main-agent/prompts/`、`main-agent/config/` 和 `skills/` 保留了导入的内容来源；当前 `agent-director.js` 实际导入的是 `server/shared/worker/agent-bundle-data.js`。只改来源文本不会自动更新这个运行时包。涉及提示词/技能时，应一起核对包的生成与版本来源，验证实际发给模型的内容。

调试时从 `Street.unity` 运行，按 Tab 进入手机直播。服务变更后重启服务并重新进入直播。服务日志与 Unity Console 分别定位后台失败和客户端执行失败；不要把供应商返回成功等同于角色已播放完成。

超长识别文本完整放入输入框，编辑到弹幕 120 字或连麦 360 字以内后才能发送。

## 多个聊天并行与汇总

每个开发聊天使用独立 Git worktree、任务分支和草稿 PR；在各自目录中遵守仓库 `AGENTS.md` 并运行 `tools/team/z start`。不要让多个聊天在同一个本地目录切换分支。当前协作工具的 `start` 固定从最新 `origin/main` 开工；依赖另一个未合并任务的新功能，应等前置 PR 合并再开工，或先明确安排依赖分支，不能假设 `start` 会保留所选功能分支作为基线。

- 按具体文件分工。例如一项修改 `server/singing/`，另一项修改 `server/voice/`；两项都需要改 `LiveCompanionView.cs` 或 `server/session.mjs` 时，指定一项统一接线。
- 每个 worktree 的依赖、Unity Library、服务 `.runtime` 和本机配置独立准备。并行启动服务使用不同的 `Z_UNITY_PORT`；各 Unity 实例打开对应 worktree 的工程。
- 阶段完成运行 `tools/team/z save --message "本次修改" --handoff "已完成、未完成、下一步"`，记录 PR 地址、文件范围和验收结果。
- 单项验收后运行 `tools/team/z ship`，仓库机器人按队列检查并合并至 `main`。依赖任务先合并，后续任务同步最新 `main` 后重新检查；红色难合并文件冲突先确认处理顺序。
- 最后一项合并后，在包含所有 PR 的最新版本做整体验收。需要修复时另开集成修复任务；检查全部 PR 为 `MERGED`，再通过 `tools/team/z done` 回到最新 `main`。聊天完成、保存成功和 PR 合并是三个不同状态。

Unity 控件回归：编辑器菜单 `Seabreeze > Verify Live Input Drafts`，或批处理调用 `-executeMethod Seabreeze.Editor.LiveDraftVerification.Run`。此检查覆盖超长语音文本完整保留与输入长度复原，不替代真人麦克风、真实歌声和完整流程试玩。


## 网易云后台登录与维护

本次沿用集成包 `Z唱歌玩法集成包-20261105` 的固定账号指纹（`config/netease-account.json`），不复制其 Cookie、真实数据库或登录文件。不读取浏览器登录态。账号不匹配时明确报错；更换绑定账号应单独决定。

```sh
npm run netease:login -- status
npm run netease:login -- begin
# 打开上一步返回的 qrImageFile，用已绑定的网易云账号扫码确认
npm run netease:login -- wait
npm run netease:login -- status
```

登录由后台操作者完成，不让直播观众处理。服务启动时及每小时检查，约24小时刷新一次登录；这只是登录维护周期，不是演唱等待时间。过期、账号不匹配、查询失败和“已登录但只有试听权限”分别报告；不自动换歌或绕过试听限制。`maintain` / `refresh` 可手动触发维护。

## 数据兼容与限制

首次升级在原 `.runtime/library` 中加入技术验收字段，并同步原 `.runtime/singing` 的合格唱段。旧的待试听音频通过技术检查后可以待唱，同一资产关联的任务一起恢复；明确拒绝的音频或参考资料保持拒绝。旧审核记录与旧播放记录保留为历史证据，不再写入新唱歌回执。升级前停止服务并备份整个 `.runtime`，回退旧程序时一并恢复备份。

集成包随附的《晴天》副歌加入曲库，原《晚安》副歌保留。导入包里的历史试听记录仅作为来源记录；本次没有重新进行人工音质验收。当前 Vivi 仍用文字指定歌曲、版本和片段，下载的原曲/歌词不作为音频条件输入，技术检查不能保证旋律、歌词、片段或音色完全正确。

研究对照：参照 K03/K04 对动作结果与状态分离的思路，保留请求、素材准备、任务失败和前台活动的因果关系。按本次明确需求不采用固定时间门槛；技术可用状态与主 Agent 的演唱时机分开。修改仅在本次允许路径，根设计文档和机制采用记录未改动。


## 聊天与唱歌版本

Z 的才艺只保留唱歌。主 Agent、连麦与模拟弹幕使用同一能力边界；旧绘画、塔罗、配音执行入口已移除，旧存档中的未完成活动不会恢复。普通聊天的语音、Live2D 表情和口型继续使用。此前导入的其他才艺规范和独立 demo 模块仅保留来源记录，不在当前服务的技能注册与运行包中加载。

唱歌表达由自然语言人物说明与情境示例引导，不设置禁词、正则替换或固定台词。主 Agent 用真实任务状态判断能否唱，对观众表达想歌、熟悉一下或准备开口；准备就绪与真实唱完仍由不同事件确认。

修改 `main-agent/`、唱歌、弹幕或连麦 skill 后运行 `node scripts/build-agent-bundle.mjs`，同步实际运行提示包和来源校验记录，再运行 `npm test`。生成脚本不读取个人配置或密钥。


## 本地直播收尾试玩包

从最新主线构建后，双击工程根目录 `试玩直播收尾.command`：复用本机已有供应商配置，在 4187 端口启动独立直播服务，写入仅本机使用的连接凭据，再打开 `Builds/macOS/Seabreeze.app`。需要 Node 24+，可用 `Z_LIVE_NODE` 指定；`Z_UNITY_PORT` 可改端口。运行中服务保持在后台，日志在 `Builds/LiveSession/service.log`。试玩世界存档隔离在 `Builds/LiveSession/saves`，此入口暂时关闭与本任务无关的岛民记忆服务。

按 Tab → “Z 的直播”进入；底部输入框旁“结束直播”即 P00 自然收尾。主播自主收束时按钮显示“立即收尾”，仍可在输入框中挽留一次；道别完成后显示“返回游戏”。本地程序、连接文件、供应商配置和存档均不提交 Git。对外分发时不可打包 `.env*`、`.runtime` 或包内 `zlive-connection.json`。


## 收播流程复验

1. 在本目录运行 `node scripts/verify-session-ui.mjs`。它只监听本机 4188，使用固定模型结果，不读取供应商密钥。
2. Unity 使用 `-batchmode -projectPath <unity-sideview绝对路径> -executeMethod Seabreeze.Editor.LiveSessionVerification.Run -logFile <日志路径>` 启动；此入口会自行退出，不加 `-quit`。
3. 查看 `Builds/LiveSession/verification/result.txt`、`before-stop.png`、`ended.png`。验收使用实际按钮提交事件、HTTP、取消屏障、文字展示回执和返回回调，不以直接调用状态 setter 代替完整流程。

2026-11-07 验证：94 项服务回归通过；Unity 流程验收通过，覆盖常显按钮、立即禁输入、相关告别实际展示、结束态标签、关闭连麦和返回回调。真实 macOS Player 已走通按钮 → 语音告别完成 → 结束；真实 DeepSeek 使用固定虚构“晚霞”话题生成相关告别成功。真实模型完整自主收束与一次挽留的自然度仍需玩家试玩；对应状态守卫已有离线回归。Unity 编辑器启动时会输出既有 QuickSearch 索引异常，未影响流程验收，独立 Player 构建报告为 0 错误、0 警告。
