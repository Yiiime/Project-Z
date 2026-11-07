# Vivi 2.0 语音接入

## 使用方式

声音默认关闭。点击直播画面中的声音按钮开启；设置中也有相同开关和音量滑块。开启后从下一段发言开始使用 Vivi 2.0，正在展示的段落继续完成，既不重播也不丢字。固定剧情和 DeepSeek 动态回复共用同一声线。弹幕区不重复显示 Z 的台词。

本地将 `VOLC_TTS_API_KEY` 写入 `.env.local` 或 `火山语音配置.env`。优先级为进程环境变量、可见配置文件、`.env.local`，与 DeepSeek Key 独立。需要豆包语音服务的 API Key，不使用火山方舟 Key。托管时在 Sites 的运行时环境变量中以 secret 保存同名 Key，再部署版本使配置生效。Key 不进浏览器、Git、构建产物或分享包。

## 协议选择

- 音色：`zh_female_vv_uranus_bigtts`（Vivi 2.0）。
- 资源：`seed-tts-2.0`。
- 上游：`https://openspeech.bytedance.com/api/v3/tts/unidirectional/sse`。
- 鉴权：服务端发送 `X-Api-Key` 和 `X-Api-Resource-Id`。
- 输出：24 kHz MP3，`audio_params.enable_subtitle: true`。

DeepSeek 先生成一段短回复，随后合成语音。本版采用同模型的官方 HTTP SSE，适配现有 Worker；没有实现双向 WebSocket 连续喂文本。服务端流式转发音频和字幕，浏览器收齐该句 MP3 后解码播放，不是首个音频分片到达即播放。

官方资料：[音色列表](https://docs.volcengine.com/docs/DoubaoVoice/Tonelist-1?lang=zh) · [HTTP SSE 接口](https://docs.volcengine.com/docs/DoubaoVoice/HTTPChunkedSSEUnidirectionalStreaming-V3?lang=zh) · [双向 WebSocket 接口](https://docs.volcengine.com/docs/DoubaoVoice/bidirectional-streaming-text-to-speech-websocket?lang=zh)

## 播放和字幕

`GET /api/tts/config` 返回是否配置及非敏感音色信息。`POST /api/tts` 仅接受 `requestId`、`text` 和受控 `mood`，返回 `start / audio / sentence / done / error` NDJSON 事件。音色、接口地址、资源和说话风格均由服务端限定。

前端使用词时间戳和 AudioContext 的实际播放位置显示字幕。没有有效词时间戳的片段在相邻锚点间插值；文字不会按网络到达速度一次跳完。角色嘴型依据播放能量变化。暂停后保存音频位置，继续时复用已解码音频，不重新收费。

切换发言、静音、重播或离开页面时，旧请求会取消，旧音频和晚到结果会被隔离。合成过程中暂停会取消本句语音，恢复后继续该句文字；不会自动补发合成。静音保留已显示文字，剩余部分继续打字。浏览器需要用户点击声音开关后才开始播放。

弹幕现在不会打断当前讲解。引擎等待当前语音的实际 `onended`，或文字模式的完整展示和阅读停留结束，再切换到选中的玩家回复。字幕提前出现全文也不视为语音已经结束。「下一步」只安排段落结束后的操作，不调用强制结束音频。显式重新体验仍会重置本轮直播。

回复由「真实昵称与对应原话 + 回应正文」组成同一段，字幕和语音使用完全相同的文本。语音模块保留有界的恢复缓存用于兼容已有调用，正常弹幕调度不再使用中断恢复机制。

未配置、超时、额度或服务错误、无法解码等情况会提示并继续文字。后端等待上限 20 秒，前端单句保护上限 25 秒。单句最多 500 字符，超长内容保持完整文字展示。本版没有浏览器系统配音兜底，不会把其他声音当作 Vivi。

## 服务端保护

沿用 Sites 的登录身份及同源要求。后端限制请求体、事件、累计音频与字幕大小，向浏览器输出具有背压；取消和超时会终止上游并释放并发。每个 Worker 实例每用户最多 2 个同时请求、每分钟 30 次，全实例最多 6 个同时请求；同一用户相同请求 ID 五分钟内去重。限流和去重不是跨实例持久配额。付费请求不自动重试。

## 验证

`npm test` 包含离线语音协议、取消、背压、字幕时钟、音频暂停恢复、失败回退与双 Key 集成验证，不访问真实 API。

`node scripts/try-vivi-voice.mjs` 会明确发起一次真实合成，保存 `output/vivi2-z-demo.mp3` 和字幕 JSON。脚本只输出音频大小、耗时和输出位置，不输出密钥；不会自动重试。
