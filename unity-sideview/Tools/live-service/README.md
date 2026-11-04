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

语音配置使用[豆包语音控制台的 API Key](https://console.volcengine.com/speech/new/setting/apikeys?projectName=default)。同一项目需开通语音合成大模型 2.0（`seed-tts-2.0`）与流式语音识别（`volc.seedasr.sauc.duration`）；方舟 `ark-` Key 和通用 AK/SK 使用各自的鉴权接口。一个语音项目 Key 可填入两个语音配置项。`Invalid X-Api-Key` 表示密钥鉴权失败；`requested resource not granted` 表示该 Key 所属项目缺少所请求服务的授权。

无供应商密钥时可验证界面、服务连接和返回探索；AI 生成与对应语音能力需要配置后使用。供应商密钥只由 Node 服务读取。

独立 Player 从 `Application.persistentDataPath/ZLive/local-connection.json` 读取同格式连接文件。部署宿主也可在 `LiveFeatureController.Start` 前调用 `Configure(serviceUrl, gameSessionToken)`。连接使用 HTTPS，回环地址允许 HTTP。当前服务面向单机开发者，使用一个本机 Bearer token；多人账号隔离和公开服务器部署需要单独接入游戏登录系统。

## 操作

- 文字输入上限 120 字。发送时自动恢复暂停的直播，聊天记录显示送达和回应进度；向上查看历史时，新消息不会强制滚动到底部。
- 输入框内的麦克风按钮用于上麦/下麦。上麦后自动轮流收音，Z 发言或准备回应时等待；每轮最多 20 秒，停顿后提交最终识别结果。超过直接发送范围的结果进入输入框确认。
- 编辑输入框时暂停收音；挂断、暂停、失焦和退出会释放麦克风并取消该轮识别，迟到结果不会进入后续对话。
- 设置中可切换 Haru 动态形象与 Z 原画，调整声音、音量、画面弹幕和轻柔动画，并重新开始对话。Esc 优先关闭弹窗或展开画面，再返回探索。
- 主播可以展示可选明信片话题，也可以邀请观众回应。选择只影响当前直播话题。
- 设置中的「点歌准备与试听」提交歌曲、歌手与明确片段。后台任务跨会话保存；新音频需要确认歌曲、片段、音色、无伴奏、结尾五项并填写结论。
- 试听会暂停直播，结束后手动继续。试听不写直播播放完成回执。已审核任务由主播按对话时机安排演唱。
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
