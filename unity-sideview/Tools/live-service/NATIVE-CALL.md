# 原生连麦 · 2026-11-07

本分支将 `live-call-demo/` 中的单麦克风、目标声音保护、多方聊天与流式接话适配到 Unity UI Toolkit / AudioSource。Web 包作为原样对照保留，Unity 不运行隐藏浏览器。主 Agent 仍是现有 DeepSeek，语音仍是 Vivi 2.0，角色仍使用现有 Z Live2D；唱歌和自然收播沿用当前原生直播实现。

## 开发启动

1. Node 24+：在 `Tools/live-service` 执行 `npm ci --ignore-scripts`。
2. macOS：在 `unity-sideview` 执行 `bash Tools/native-rtc/build-macos.sh`。需要 Xcode Command Line Tools。脚本下载已锁定并验证 SHA-256 的火山 macOS ARM / Intel SDK，生成 Universal 桥接。`.downloads/`、`.build/` 不提交；构建 Player 时复制运行库及供应商隐私声明。火山 Unity SDK 目前不支持 macOS，因此本项目使用官方 C++ SDK 加自行编写的 C ABI 桥接。
3. 在本地 `.env.local` 或已有中文配置文件里填写 `.env.example` 中的 RTC 和语音配置。`Z_CALL_VOICE_MODE=voiceprint`、`Z_CALL_ASR_SOURCE=api-key`。旧 App ID / Access Token 仅在 `legacy` 模式读取。`Z_LIVE_VOICE_CONFIG_DIR` 可以只复用已有声纹配置目录；`Z_LIVE_CONFIG_DIR` 可复用主 Agent / Vivi 配置目录。不向 Player 放入 AppKey、AK/SK、ASR 或 TTS 密钥。
4. 执行 `npm start`，在 Unity 6000.3.27f1 打开 `Street.unity`，Play → Tab → Z 的直播。首次录入声音在设置 → 连麦声音设置中主动进行；已有该应用的声纹可继续使用。
5. 独立包：执行 Unity `ZLive.EditorTools.ZLiveVerification.BuildWithInputChecks` 构建 `Builds/ZLiveQA/ZLiveQA.app`，再双击 `启动原生连麦试玩.command`。默认端口 4184、独立存档 `Builds/NativeCall/saves`。启动器保留本地服务在后台；日志位于 `Builds/NativeCall/`。未生成依赖或构建时会给出明确错误。

启动器可读取忽略文件 `Tools/live-service/.runtime/native-call-config.json`，格式为 `{"configDirectory":"本机主服务配置目录","voiceConfigDirectory":"本机声音配置目录","port":4184}`。文件只存本机路径，不能提交。原有歌曲库、存档与其他服务实例不会随源码包搬迁；需要使用已有网易云登录时，以 `NETEASE_SESSION_FILE` 显式指向本机登录文件。

## 当前链路

Unity 麦克风 → macOS RTC 内部采集 → RTC 房间 → MMPG 声纹降噪与豆包 ASR → 服务端逐轮声音校验 → 连麦调度 → 原主 Agent 按句输出 → 原独立审阅 → Vivi 24 kHz 单声道 PCM → Unity 分段缓冲播放 → Live2D 口型和实际播放回执。

原始 VAD 不直接打断。MMPG 同一轮必须同时有 `sv_active=true`、目标说话人校验、有效文字及至少 300 ms 观察窗口，才触发让话。Z 在约 180 ms 内淡出，不插入“好你先说”；下麦、暂停、失焦、会话结束和旧 callId 会撤销过期事件。Unity 当前音轨以 10 ms PCM 帧提供给 RTC 作为回声参考，真实设备效果需要听验。

仅正式 `transcription.done` 自动送给主 Agent；segment/delta 显示为草稿。超过 360 字的完整内容留在输入框编辑；不裁掉尾字后伪装为已发送。服务器只把确认的玩家消息和实际公开的主播前缀提供给观众。多方并行审阅按角色隔离，弹幕不能占据主音轨，也不能替玩家授权唱歌。

主 Agent 普通聊天优先完整短句；工具和收播决策仍整轮校验。每句独立审阅后排入唯一音轨，正式记忆在整轮实际播完后提交，打断只保留已公开前缀。PCM 达到约 100 ms 缓冲即可起播，缺包时等待续播，失败不发送完成回执。字幕用供应商词时间戳；没有词信息时不把全文当作已说。

等待超过约 900 ms 且音轨空闲时，可播放缓存的 Vivi“我想想啊…”。正式发言或玩家新输入到来时取消；此声音不进入正式对话、记忆或回答完成记录。首次缓存未就绪则只显示占位。

## 会话与凭证

沿用现有 Bearer 身份、session/epoch 和 executor lease。声纹按 owner 存在 `.runtime/voiceprint/`；仅本机 `local-owner` 可以复用原 Demo 的已有声纹，其他身份必须单独登记。RTC 短时房间 token 仅在启动响应中下发，不写入公共事件或持久快照；SDK 不接触语音服务密钥。原录音只在登记请求的内存中处理。

新增接口均在 `/v1/live/sessions/:id` 下：`/voice/config`、`/voice/start`、`/voice/joined`、`/voice/stop`、`/voice/enroll` 和 `/audio-stream/:id`。普通语音旧接口留作兼容，当前单麦克风界面使用 RTC 路径，不会在声纹失效时静默退回未保护的收音。

## 验证与边界

验证结果见 [原生直播验证记录](VERIFICATION.md)。`node scripts/check-native-voice.mjs` 仅检查本地配置；加 `--live` 才访问 RTC 权限和创建无音频的 MMPG 会话。该检查不打开麦克风，也不等于真实办公室降噪验收。

当前平台适配为 macOS ARM / Intel。Windows、iOS、Android 尚无本项目原生桥接实现。多人同时说话、麦克风距离、耳机与扬声器回声，以及真人接话延迟仍需要现场测试；离线测试音首声不是 DeepSeek + ASR + Vivi 的端到端延迟。没有绕过语义审阅，因而旧的人物语义反复退回问题仍可能出现。

官方依据：[SDK 下载与平台范围](https://docs.volcengine.com/docs/real-time-communication/rtc-get-sdk?lang=zh)、[自有 Agent / MMPG 接入](https://docs.volcengine.com/docs/real-time-communication/quick-integrate-custom-agent-via-gateway?lang=zh)、[豆包流式 TTS](https://docs.volcengine.com/docs/doubao-voice/unidirectional-streaming-text-to-speech-http?lang=zh)。SDK 文件由供应商条款约束，不作为本仓库代码重新授权。
