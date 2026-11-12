# 单人对话 TTS

玩家与 Z／七位 NPC 的岛内单人对话，在台词获得记忆服务提交回执、音频合成就绪后，同步开始语音与逐字显示。NPC 互聊、旁听、公告和晚间直播不走这个入口。当前实现先接收整句音频再播放，不承诺首包流式播放或精确口型同步。

## 本机试玩

构建当前分支到 `Builds/TtsPlaytest/ProjectZTts.app` 后，双击主工程目录的 `启动单聊语音试玩.command`。入口使用本机 4436 端口和 `.local/tts-playtest-saves` 独立进度，读取本分支 MemoryLab 的本机模型与 TTS 配置。构建包、密钥和试玩存档不随 Git 提交。

左右移动，靠近角色按 E 开始单聊，输入文字并发送即可试听；Esc 结束交谈，“声音”按钮可静音。主街有正义、晴野、留洋和周伯伯，咖啡馆有李庭，灯塔海堤有梨园和阿星。模型配置页与服务日志用于排查连接问题，不需要在游戏中手工填写音色 ID。

## 语音、文字和口型的节奏

有声单聊先显示“正在准备语音…”，语音就绪且角色准备动作完成后再开始逐字显示；等待期间不提前打完整句台词。文字读取 AudioSource 的实际采样进度，按整句音频时长分配显示速度，采用 4% 的加速并将领先量限制在最多约 0.35 秒。当前没有逐字时间戳，因此句中停顿仍是整句比例近似，不是逐字强制对齐。

Live2D 通过已有语音驱动接口读取当前播放音频的音量；文字提前显示完后口型继续跟随声音，声音结束后停止。点击气泡仍可主动显示全文。静音或语音失败时降级为每秒最多 6 字的文字显示，新消息、关闭对话和读档仍取消旧语音。此改动消除音画启动错位，不缩短服务端整句合成耗时。

## 音色和本机配置

`tts-voices.json` 保存非敏感音色映射：Z 使用 Vivi 2.0；周伯伯、正义、梨园、李庭、晴野、留洋、阿星使用用户提供的音色设计 ID。`chenchen` 仅保存服务端音色，没有加入岛内角色目录或新增对话入口。游戏稳定 ID `xiaoxiaoqing` 对应晴野，`linxu` 对应留洋。

音色设计使用 `seed-icl-2.0`，并指定 `seed-tts-2.0-expressive` 以支持后续语音指令。Z 使用 `seed-tts-2.0`。参见火山官方[音色设计说明](https://docs.volcengine.com/docs/doubao-voice/sound-design-api?lang=zh)和[HTTP SSE 参数](https://docs.volcengine.com/docs/doubao-voice/http-chunked-sse-unidirectional-streaming-v3?lang=zh)。

服务读取环境变量 `VOLC_TTS_API_KEY`，或本机 `.local/tts.json` 中的 `apiKey`。环境变量优先。可从 `tts-config.example.json` 复制模板，并设置文件权限为 `600`。`.local/` 已被 Git 忽略，真实密钥不得提交。`Z_TTS_CONFIG` 可指定其他本机配置文件。

```sh
mkdir -p .local
cp tts-config.example.json .local/tts.json
chmod 600 .local/tts.json
```

在本机编辑该文件填写密钥。Key 必须能够访问这些设计音色所属的语音项目及声音复刻 2.0 资源。可以设置 `enabled: false` 禁用合成；`voices` 可按角色 ID 覆盖默认音色参数。配置每次请求重新读取，修改本机文件后不必重新生成角色回复提示词。

## 上游 delivery 契约

目前不要求上游模型生成 `delivery`，也不修改人设提示词。接入层接受台词输出顶层的可选字段：

```json
{
  "dialogue": "没事，你先忙。",
  "delivery": "  有些失落，但努力保持轻松。  ",
  "action": "沿用现有合法 action 对象",
  "info": "沿用现有合法 info 对象"
}
```

上例仅展示字段位置，action/info 实际仍需遵循现有对象契约。`delivery` 在校验业务对象前单独取出，作为提案表现元数据保存；不会作为人物事实、记忆事件、字幕或可朗读正文。当前返回给 Unity 的顶层 `delivery` 默认 `""`。

- 缺失、null、空串或纯空白：不发送 `context_texts`，不发送默认语气模板。
- 有效字符串：原样放入 `req_params.additions` 所编码的 `{"context_texts":[delivery]}`；保留空格、换行和原始措辞。不根据 emotion/expression 推断，不追加人物语气，不翻译或截断。
- 非字符串、控制字符或超过应用预算 1000 个 Unicode 字符：省略指令，提案记录 `tts_delivery_invalid`，成功播放时输出该诊断码。该预算是本应用约束，不声称是火山接口上限。
- 后续上游只需加入可选 delivery 输出能力；不需要再调用一个模型分析情绪。

## 播放与取消

Unity 向 `/api/game/speech` 提交 world/run/conversation/proposal/actor 标识。服务只读取当前角色已提交的单聊提案中的正文与 delivery，忽略客户端提交的替换文本。未提交、旧时间线、非私聊、已换对象或已有下一条消息的提案不能合成。

火山 HTTP SSE 返回音频被验证为完整终态，再以 24 kHz 单声道 PCM16 交给 Unity 创建 AudioClip。网络数据、单事件和音频长度有上限；同一提案的并发请求复用一次合成，失败不自动付费重试。

新输入、结束对话、切换对象、世界重置和读档会停止旧声音并取消请求。现有“声音”按钮静音时也停止本句，恢复声音不会补播旧句。语音失败显示状态，文字和下一轮输入继续可用。现有记忆提交语义不变，不把“语音已播完”伪装成新的角色事实。

## 验证

```sh
npm test
# 本机模拟服务：不读真实密钥、不调用外部服务
node tests/serve-speech-fixture.mjs
# 显式真实探测：八位岛内角色各一句，无自动重试，产物在临时目录
Z_TTS_PROBE_OUTPUT=/private/tmp/z-tts-real node tests/verify-speech-real.mjs
```

Unity 播放验证入口：`Seabreeze.Editor.DialogueSpeechVerification.Begin`，传入 `-memoryService http://127.0.0.1:4433 -speechVerificationOutput /private/tmp/z-tts-play-report.json`，并将 `Z_MEMORY_SAVE_ROOT` 设置为隔离的 `/private/tmp/` 子目录。该测试操作实际单聊入口，检查等待音频时文字不抢跑、逐字显示跟随音频、口型使用音频驱动、结束时显示完整台词并停止口型，以及音频采样游标、静音、关闭、迟到响应、失败后的输入与读档取消。模拟音是测试音，不代表角色真实音质。

使用正常图形编辑器运行，不能加 `-batchmode` 或 `-nographics`：现有单聊背景截图需要 Game 视图的帧末渲染。先确保 Z 的 Live2D 资产已由 Git LFS 展开。测试完成会写报告并退出该编辑器进程。

本次代码验收：`npm test` 90 项通过；Unity 6000.3.27f1 编译与上述六项播放行为验证通过（Z 单聊、模拟音频）。八个角色的音色路由由服务端测试覆盖。编辑器启动时出现现有 SearchDatabase 索引异常，未阻止本次单聊和音频验证；本次不评估其他角色的美术资源或人工听感。

本轮音画同步修正：语音服务回归 11 项通过，Unity 图形 Play Mode 的 10 项检查通过（Z 单聊、模拟音频，报告 `/private/tmp/z-tts-sync-report.json`）。验证覆盖音频等待、文字进度、口型驱动、自然播完及已有取消路径。

首次真实探测：本机 Key 成功返回 Z 的 Vivi 2.0 音频，七位 NPC 均返回鉴权／权限错误。随后在控制台核对，程序使用的 Key 与音色所在 default 项目的现有 Key 一致；当时声音复刻 2.0 未开通。

2026-11-12 21:06（北京时间），用户开通声音复刻 2.0 后，以相同配置对七位 NPC 各合成一句，delivery 为空，无自动重试：正义、梨园、李庭、晴野、留洋、阿星成功，均收到完整、非静音的 24 kHz PCM 音频（约 1.80～2.21 秒）；周伯伯 `S_bF1VdCXi2` 仍返回 `tts_permission_denied`。此次探测只保存了归一化错误码，尚不能判断周伯伯失败的具体原因，待单独核查。沉晨未调用，接口与音频完整性验收不等于人工音质验收。

随后按用户要求单独诊断周伯伯，未改变 Key、音色 ID 或请求参数：HTTP 200，合成完整终态通过，收到 2.579 秒非静音 PCM，采样峰值 23438。七位 NPC 至此均有真实调用成功记录。此前短暂失败可能与开通后的授权同步有关，但没有当时的原始错误信息，不能确证；当前无需更换音色或凭据。
