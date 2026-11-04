# Moon Z Unity Demo 对话后端

这是 Unity 最小 Demo 的本地 Node.js 26 ESM 服务：玩家发送文字，服务端加载 Z 运行规则、当前存档的近期对话和已确认的玩家记忆，再调用可配置的 OpenAI-compatible Chat Completions provider。首版只返回文字、表情、手势和视线标签，不接 TTS。

## 启动

```sh
node server.mjs
```

在本目录执行 `node start-local.mjs` 即可启动本机服务。启动器会检查 Node `>=22.13`，从脚本所在目录启动服务，不依赖当前 Terminal 目录。若 `server/.env` 存在，启动器只把它作为 Node 的 `--env-file` 传入，不执行 shell source；没有 `.env` 时不会猜测 provider，除非环境中显式设置 `Z_LLM_PROVIDER`，否则以 `configured:false` 启动。

启动器只负责启动本机服务，不验证真实 LLM、账号登录或模型可用性。若 `127.0.0.1:8796` 已被占用，它只提示并退出，不会终止现有进程。按 `Ctrl-C` 时会转发 `SIGINT`，服务端再关闭 HTTP 与 SQLite。

服务固定监听 `127.0.0.1:8796`。数据库默认写入 `server/data/moon-z.sqlite`，也可用 `Z_DEMO_DB_PATH` 指定路径。也可以通过 `createApp({ dbPath, provider })` 注入测试 provider；测试不会调用真实 LLM。

OpenAI-compatible provider 只有在以下变量都存在时才会启用：

```text
Z_LLM_PROVIDER=openai-compatible
Z_LLM_BASE_URL
Z_LLM_MODEL
Z_LLM_API_KEY
```

变量名示例见 `.env.example`。服务不会从 `.env` 文件自动读取，也不会输出或持久化 API Key。未配置时 `/health` 返回 `configured:false`，`/v1/chat` 返回 503，不使用假回复。

若 `Z_LLM_PROVIDER=codex`，主入口会动态加载 `codex-provider.mjs`，使用本机已登录的官方 Codex CLI，超时为 125 秒；可选 `Z_CODEX_BIN`、`Z_CODEX_MODEL` 只在 provider 内读取。服务不读取 auth 文件，CLI 未认证或不可用时，实际请求会返回错误。

## HTTP 契约

- `GET /health` → `{ status, provider, configured, skill_version }`
- `GET /v1/session?save_id=main` → `{ save_id, history, memories }`
- `POST /v1/chat`，请求 `{ save_id, turn_id, message }`，返回 `{ turn_id, segments, provider, memory_count }`
- `POST /v1/ack`，请求 `{ save_id, turn_id }`。客户端确认台词已经显示后调用；调用后才提交本轮对话和通过校验的长期记忆。
- `POST /v1/cancel`，请求 `{ save_id, turn_id }`。取消尚未确认的回合（包括已生成但未 ack 的回合）；迟到 provider 结果不会写入历史或记忆。

`save_id` 使用 `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`，`message` 最多 2000 个 JavaScript 字符。服务固定使用 `player=local`、`character=z`。HTTP 请求可不带 `Origin`；若带 Origin，只接受 loopback 主机（`localhost`、`127.0.0.1`、`::1`）。

## provider 输出

provider 应返回：

```json
{
  "segments": [
    { "text": "……", "expression": "smile", "gesture": "none", "gaze": "player" }
  ],
  "memory_updates": [
    { "key": "player.favorite_place", "value": "海边", "evidence": "我喜欢海边" }
  ]
}
```

服务会把同一份 `RESPONSE_SCHEMA` 和上述 JSON 协议放进 system prompt。OpenAI-compatible 请求使用 `response_format: { type: "json_object" }`，仍由服务端严格校验并对非法表演标签做安全回退。

`performance-contract.mjs` 集中定义表情、手势和选择语义。当前支持十种表情（原六种加 `cry`、`bawl`、`sigh`、`gloom`），分别搭配 `none`、`wave`、`explain`、`point`。LLM 按台词内容选择，未指定手势意图时优先 `none`。Unity 复用已经确认的脸部和辅助素材；手部是三组现有静态姿势。R5 既有验收摘要位于本支持层的 `../verification/r5-summary.json`。

若兼容接口返回空字符串或全空白文本，且没有截断、工具调用或拒绝标记，后端会用同一取消信号重试一次，总请求最多两次。第二次仍为空时返回 `502 / PROVIDER_EMPTY_RESPONSE`。认证失败、限流、非法 JSON 等错误不自动重试；不会拿推理内容充当台词。两次请求共用原回合超时，重试不会重复提交历史或记忆。

服务端限制最多 3 段、每段最多 240 个字符。非法表演标签回退到 `neutral`、`none`、`player`；非法结构、空台词、超长台词会返回 provider schema 错误。记忆候选必须使用 `player.` 前缀，且 evidence 必须是本轮玩家原文的连续子串；候选会等 `/v1/ack` 后才提交。

## 给父任务注入 provider

`server.mjs` 导出 `createApp`、`createServer`、`createOpenAICompatibleProvider`、`readProviderConfig`、`normalizeModelResponse`、`RESPONSE_SCHEMA`。主 provider 接口是 `async generateReply({ messages, schema, signal }) -> raw parsed object`。测试或本机 Codex 适配可传入：

```js
const app = createApp({
  dbPath: ':memory:',
  provider: {
    name: 'injected',
    async generateReply({ messages, schema, signal }) {
      return { segments: [{ text: '……', expression: 'neutral', gesture: 'none', gaze: 'player' }], memory_updates: [] };
    }
  }
});
```

provider 只接收已经筛选过的 Z 规则、近期历史、已确认记忆和当前玩家输入。它不能直接写数据库；只有 `/v1/ack` 会提交回合和记忆。
