# 月亮岛 · Skill、自然记忆与 A2A 统一后端

NPC 身份以当前角色包为准：Z、梨园、李庭、阿星、正义、小小晴、林序、周伯伯。角色输出始终是 `dialogue / action / info`。旧十人名单、固定 Day4 全岛公告和按公司／AI关键词清除剧情的迁移规则不再进入此运行入口。

## 启动

需要 Node 22.13+（支持 `node:sqlite`）。在此目录执行：

```sh
sh start.sh --background
```

默认端口 4330。对话体验页 `http://127.0.0.1:4330/`，模型设置页 `/settings`，个人记忆观察室 `/memories`。现有本机 `.local/model.json` 可以继续使用；也可通过环境变量或 `--env-file` 配置 `GAME_MODEL_API_KEY`、`GAME_MODEL_NAME`。配置不会进入角色上下文、状态接口或 Git。

默认数据库为 `.data/unified.sqlite`，与旧 `.data/game.sqlite` 分开。旧十人角色快照不会被自动解释成新八人人设，请使用新的体验进度。需要迁移历史进度时，要明确角色映射与原话来源，不能只按名字或关键词搬记忆。

环境变量：`Z_MEMORY_NODE` 指定 Node 路径，`Z_MEMORY_PORT` 指定端口，`Z_MEMORY_DB` 指定数据库，`Z_MEMORY_CONFIG` 指定本机模型配置文件。缺少 Ajv 依赖时启动脚本会执行 `npm ci --ignore-scripts`。

## 内容和代码

- `content/current/actor-registry.json`：唯一角色注册表。
- `content/current/moonlife-actors/`：当前八份人设、共用规则、地图、任务与单份契约。
- `lib/catalog.mjs`：加载当前人设，编译公开职业／地图，提供路线候选。
- `lib/policy.mjs`：角色特化 Schema、合法动作、来源、数值、信息分享与玩家任务校验。
- `lib/engine.mjs`：独立角色状态、按NPC对话占用、提案、实际发言、动作与存档。
- `lib/tasks.mjs`：正式玩家任务、各 NPC 的协作范围、拒绝后续和幂等回执。
- `lib/natural-memory.mjs`、`memory.mjs`、`consolidator.mjs`：大意／细节衰减、召回、原话证据、后台提取与可修正反思。
- `server.mjs`：唯一 HTTP 入口；`public/` 提供体验与观察室。

自然记忆沿用原实验台的双轴模型，属于可调试的演示模型，关键词召回不等于向量检索。职业、地图、已确认的既有关系是稳定常识；核实知情与任务事务不随普通记忆遗忘。台词生成只调用当前角色一次，记忆提取与反思在后台进行。

## Unity 协议

`X-Z-Memory: 1` 用于本机 Unity 接入，HTTP 外层协议为 2。核心接口：

| 接口 | 用途 |
|---|---|
| `game/open` | 从当前八人 checkpoint 打开时间线，更新 runId |
| `game/focus` | 按 NPC 取得、切换、释放玩家对话占用 |
| `game/sync` | 按稳定 id 和连续 seq 提交实际事件 |
| `game/intent` | 低频选择是否见面、对象与话题；不消费 Unity 事件序号 |
| `game/utterance` | 返回带 proposalId 的三部分提案；尚不写成实际发言 |
| `game/commit` | 提交无台词的实际等待／决定 |
| `game/cancel` | 取消未说出的会话结果 |
| `game/checkpoint` | 一致的角色、任务、记忆与待处理事务快照 |

`sync.eventData.kind` 支持 `utterance / observation / story_event / clock / task_response / task_invite / task_receipt / action_result`。剧情事件由引擎给实际观察者名单，不固定哪一天让所有人知情。玩家和 NPC 对话默认私聊；NPC 互聊由 Unity 的实际距离、遮挡和频道决定听众。

生成、实际发言、送达、相信、答应与完成分别记录。取消／读档后旧生成不得写回；已经实际说出而排在 outbox 中的原话可按批准的提案及连续事件重放。NPC正式任务只发给玩家，协作由NPC逐个决定，不自动计作完成。

## 验证

```sh
npm ci --ignore-scripts
npm test
```

测试使用内存 SQLite 和显式模型替身，普通启动不会启用替身。覆盖身份、知情隔离、遗忘、提案确认、玩家占用、独立互聊、任务协作、取消、事件序号和读档重放。Unity 编译与实际场景验证另见 `../../Docs/MemoryIntegration.md`，不能以 Node 测试代替 Unity 运行结果。
