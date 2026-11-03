# Z Live2D：DeepSeek 接入说明

> 本文说明此前独立交付包 `Z_Unity_Demo_20261103` 的 DeepSeek 接入方式和边界。
> 它是接入文档，不代表当前 `Project-Z` 主工程已经接入对话服务。

## 先看结论

此前的交付 Demo 使用一个 OpenAI-compatible Chat Completions 适配器。DeepSeek 提供兼容的接口协议，因此可以沿用这条适配器路径：把服务端 provider 配成 `openai-compatible`，把 Base URL 指向 DeepSeek，模型名和 API Key 在服务端本机配置。

当前代码已经具备以下协议能力：

- 发送 `model`、`messages`、`temperature`、`response_format: {"type":"json_object"}`、`stream: false`。
- 从 `choices[0].message.content` 读取模型内容。
- 由 Prompt 要求模型输出 JSON，并在服务端再次校验结构和白名单标签。
- 把合法的 `expression`、`gesture`、`gaze` 标签交给 Unity，Unity 再映射为表情、姿势、视线和连续动作。

当前没有用真实 DeepSeek API 完成联调。`/health` 返回 `configured: true` 只表示所需环境变量非空，不代表 API Key 有效、模型可用或网络请求成功。

## 1. 这层技术架构怎么工作

```text
玩家输入
  ↓
Node 服务组装 Prompt：Z 运行规则 + 最近对话 + 玩家记忆 + 当前输入
  ↓
DeepSeek Chat Completions
  ↓
返回结构化 JSON
  ↓
Node 解析、校验、清洗标签
  ↓
Unity 读取 expression / gesture / gaze
  ↓
Unity 固定映射到 Live2D 表情、姿势、视线和连续动作
```

LLM 选择的是台词和轻量表演意图，不直接控制 Live2D 参数，也不直接返回头部角度、身体角度或逐帧动画。

### 1.1 Provider 要返回什么

Provider 内部必须返回一个 JSON 对象。`segments` 和 `memory_updates` 都是顶层必需字段；没有记忆候选时也要返回空数组：

```json
{
  "segments": [
    {
      "text": "见到你我很开心。",
      "expression": "smile",
      "gesture": "wave",
      "gaze": "player"
    }
  ],
  "memory_updates": []
}
```

字段含义：

| 字段 | 含义 |
| --- | --- |
| `text` | 显示给玩家的台词 |
| `expression` | 表情意图：`neutral`、`smile`、`surprise`、`angry`、`troubled`、`blush` |
| `gesture` | 姿势/手势意图：`none`、`wave`、`explain`、`point` |
| `gaze` | 视线意图：`player`、`away` |
| `memory_updates` | 玩家记忆候选；只接受 `player.*`，且 `evidence` 必须来自本轮玩家输入 |

当前协议没有独立的 `emotion` 或 `motion` 字段。连续动作由 Unity 根据标签固定映射：

下面按自上而下的顺序匹配，命中第一条后停止；它不是持久的情绪状态机。合法标签也只表达轻量表演意图，不保证每次台词都符合美术预期。

```text
gesture=wave        → Happy
gesture=explain     → Nod
gesture=point       → Listen
expression=troubled → Think
expression=angry    → Shake
expression=smile    → Happy
其他                 → Nod
```

服务端会把未知表演标签回退到 `neutral`、`none`、`player`；结构错误、空台词或长度超限会报 provider 错误。Unity 只收到 `segments`、`provider`、`memory_count` 和 `turn_id`，不会收到 `memory_updates` 原文。

### 1.2 Prompt 为什么能让模型选标签

服务端把下面几类信息拼进同一轮请求：

1. `server/Z_RUNTIME_SKILL.md` 中的角色规则、知识边界和对白边界。
2. 当前 `save_id` 最近的历史和已确认玩家记忆。
3. 当前玩家输入。
4. JSON 输出协议、字段要求、长度限制和标签白名单。

所以模型是根据上下文选择一组有限标签；Unity 不会从台词文字自行猜表情。标签选择仍是模型输出，具体参数曲线、幅度、平滑和动作持续时间由 Unity 控制器决定。

## 2. 配置 DeepSeek

以下配置写在此前交付包的 `server/.env` 中。真实 API Key 只放在运行服务的机器上，不写入 Git、不放入 ZIP，也不放进 Unity 客户端：

```env
Z_LLM_PROVIDER=openai-compatible
Z_LLM_BASE_URL=https://api.deepseek.com
Z_LLM_MODEL=<填写当前账户支持的 DeepSeek 模型名>
Z_LLM_API_KEY=<填写本机或服务器环境变量中的 API Key>
```

当前代码会把 Base URL 自动补成：

```text
https://api.deepseek.com/chat/completions
```

如果 Base URL 已经以 `/chat/completions` 结尾，代码不会重复追加。

启动服务时使用交付包自己的启动器：

```sh
cp -n server/.env.example server/.env
# 编辑 server/.env，填入上面的 provider、模型和 Key
./启动对话服务.command
```

启动器会持续运行服务。请在第二个终端检查健康状态：

```sh
curl http://127.0.0.1:8796/health
```

直接运行 `node server/server.mjs` 不会自动读取 `server/.env`；使用 `start-local.mjs` 或启动脚本，它会通过 Node 的 `--env-file` 传入配置。当前交付目标是 Node 26。

## 3. 当前请求和 DeepSeek 的兼容边界

当前服务请求体的核心字段是：

```json
{
  "model": "<configured model>",
  "messages": [],
  "temperature": 0.7,
  "response_format": { "type": "json_object" },
  "stream": false
}
```

当前实现已经在 Prompt 中明确要求 JSON，并附带输出 Schema。它没有把 `max_tokens` 或 `thinking` 做成环境变量，也没有在代码中强制设置这两个字段。接入 DeepSeek 时，如果模型或供应商对输出长度、思考模式有额外要求，可以在代码适配器层增加对应请求字段；这属于后续代码改动，不能把它们当作当前配置项。

后续适配器可以按 DeepSeek 账户和模型能力评估是否加入下面的请求字段（示意，不是当前已支持的环境变量）：

```json
{
  "max_tokens": 2048,
  "thinking": { "type": "disabled" }
}
```

这些字段需要改交付包的 `server/server.mjs` 中 `createOpenAICompatibleProvider` 请求体，再重新测试。当前服务默认 provider 超时约 45 秒，Unity 对话请求超时约 130 秒；调大 Unity 客户端超时不会延长服务端截止时间。

建议先使用非流式 JSON 输出完成协议联调，再单独评估思考模式、延迟和输出长度。模型名称不要写死在项目文档里，以 DeepSeek 官方当前模型列表和团队账户权限为准。

官方文档：

- [Chat Completions API](https://api-docs.deepseek.com/api/create-chat-completion/)
- [JSON Output](https://api-docs.deepseek.com/guides/json_mode/)
- [Thinking Mode](https://api-docs.deepseek.com/guides/thinking_mode/)

## 4. 最小验收流程

不要使用个人默认存档，也不要把真实 API Key 写入测试文件。先用一个新的 `save_id` 和合成输入：

```sh
DEMO_SAVE_ID="deepseek-qa-$(node -p 'require("node:crypto").randomUUID()')"
DEMO_TURN_ID="deepseek-turn-$(node -p 'require("node:crypto").randomUUID()')"

curl -f -sS http://127.0.0.1:8796/v1/chat \
  -H 'Content-Type: application/json' \
  -d "{\"save_id\":\"$DEMO_SAVE_ID\",\"turn_id\":\"$DEMO_TURN_ID\",\"message\":\"你好，这是一次合成联调。\"}"

# 这是 HTTP 协议验收。先人工查看上一个响应中的台词，再 ACK；curl 回包不会自动送到 Unity。
curl -f -sS http://127.0.0.1:8796/v1/ack \
  -H 'Content-Type: application/json' \
  -d "{\"save_id\":\"$DEMO_SAVE_ID\",\"turn_id\":\"$DEMO_TURN_ID\"}"

curl -f -sS "http://127.0.0.1:8796/v1/session?save_id=$DEMO_SAVE_ID"
```

验收时确认：

- `/health` 显示期望的 provider；这只是配置检查。
- `/v1/chat` 的每段同时包含 `text`、`expression`、`gesture`、`gaze`。
- curl 回包只验证 HTTP 服务；要验收 Unity 的完整链路，另启动 Demo，在面板发起一轮新对话，确认台词、表情、姿势、视线和连续动作，再让客户端自动 ACK。
- 完整显示后 ACK，`/v1/session` 才出现这轮历史和已确认记忆。
- 认证失败、限流、超时和 JSON 截断时，服务返回明确错误，不把错误内容写入角色台词。

## 5. 公网部署边界（当前未完成）

这套 Demo 当前只监听 `127.0.0.1:8796`，只接受本机连接和本机 Origin，没有用户认证、限流、多人存档隔离或生产监控。因此不能直接把当前端口暴露到公网。

推荐的后续结构是：

```text
Unity 客户端
  ↓ HTTPS + 用户身份
公网 API 网关 / 反向代理
  ↓ 内网
Node 对话服务
  ↓ 服务端持有 API Key
DeepSeek API
```

公网版至少需要：

- 可配置监听地址、HTTPS 和 CORS/Origin 策略。
- 用户认证、项目级 token、限流和调用配额。
- 按用户隔离 `save_id` 和 SQLite/数据库，不能让所有用户共用 `main`。
- API Key 只在服务端保存，Unity 客户端永远不携带 DeepSeek Key。
- 超时、重试、日志脱敏、健康检查和成本监控。
- 把当前本机单用户 SQLite 评估为独立数据库或按用户隔离的持久化方案。

这些改造尚未在 `Project-Z` 主工程或此前交付包中实现。本文只记录接入设计和改造边界，不代表已经完成公网部署。

## 6. 两个工程的版本边界

- 此前交付包：Unity `6000.6.7f1`、Built-in Render Pipeline，包含 `server/` 对话服务。
- 当前 `Project-Z` 主工程：Unity `6000.3.27f1`、URP。本文放在 Z 角色美术目录下，方便随 PR 交接，但没有把此前服务端代码复制进主工程。

因此，组员要测试 DeepSeek，需要拿此前的独立交付包运行服务；当前 PR 没有把独立 Live2D Demo 的 DeepSeek/标签表演链路接入主工程。

