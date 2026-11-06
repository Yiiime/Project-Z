# 七人记忆服务 · Memory Lab

这是现有自然记忆实验台的游戏适配版本。游戏控制日期、实际发言和知情范围；服务负责模型调用、个人记忆提取、召回与反思。

## 启动与模型配置

```sh
sh start.sh --background
```

打开 `http://127.0.0.1:4330/settings`，填写 API Key、Base URL、Model／接入点 ID，保存并测试连接。支持现有方舟／豆包、内部方舟 Responses 和 DeepSeek 适配器。内部方舟地址填写后会自动选择 Responses。

配置在本机 `.local/model.json`，权限为 `0600`；密钥不进入数据库、游戏存档、状态接口或日志。换电脑或首次使用独立试玩包时需重新配置。配置页不回显已有密钥。

| 环境变量 | 用途 |
|---|---|
| `Z_MEMORY_NODE` | 支持 `node:sqlite` 的 Node 22.13+ 路径；启动器也会检查本机已有运行时 |
| `Z_MEMORY_PORT` | 本机端口，默认 4330 |
| `Z_MEMORY_CONFIG` | 自定义本机模型配置文件路径 |
| `Z_MEMORY_DB` | 数据库路径；默认新建 `.data/game.sqlite` |

原实验台的 `.data/natural.sqlite`、`.data/lab.sqlite` 和手动实验均不读取或修改。只监听回环地址。模型未连接时可接受游戏的 Day4 通知，不产生伪造的 NPC 回复。

## 游戏接口

POST 请求使用 JSON，并携带 `X-Z-Memory: 1`。核心字段是 `worldId`、本次载入生成的 `runId`、游戏 `day/period`。公开配置中没有 API Key。

| 接口 | 作用 |
|---|---|
| `GET /api/health` | 服务、模型与真实／模拟模式状态 |
| `POST /api/game/open` | 从游戏的记忆快照打开时间线，取消旧运行结果 |
| `POST /api/game/sync` | 按序提交真实事件，返回新的快照与确认序号 |
| `POST /api/game/intent` | 当前 NPC 根据自己的知识、记忆和可见名单选择对象与话题 |
| `POST /api/game/utterance` | 为指定发言者生成一条提案；此时尚不写入交谈记忆 |
| `POST /api/game/cancel` | 取消未说出的生成结果；已实际说出的事件仍可整理 |
| `POST /api/game/checkpoint` | 获取当前快照 |

`sync.eventData` 支持 `clock`、`day4`、`observation`、`utterance`。事件必须有稳定 `id`、连续 `seq` 与游戏时间。游戏决定实际参与者和旁听者；模型没有发布世界事件或完成任务的接口。

## 时间与存档边界

游戏日期和上午／午后／傍晚／夜晚映射为逻辑小时；模型等待不推进游戏时间。公开 Day4 事件使用固定 ID，每个 NPC 只接收一次；工作人员内部通知另存为私有来源。

游戏存档包含最近已确认的记忆快照和待提交事件队列。读档先恢复该快照，再重放队列；不会直接采用服务端可能较新的状态。`runId`、版本检查和取消令牌阻止旧请求写回，响应序号阻止迟到响应覆盖新快照。恢复时拒绝未来记忆。

自然记忆沿用容易想起／细节清晰两个独立维度，没有六天强制清空。对话内容先作为有说话人和原话证据的陈述；自己的反思保留主观标记，不能仅靠旧反思自证。口头声称履约不能把约定标为已兑现，只有游戏确认的在场结果能够兑现。

## 验证

```sh
node --test tests/*.test.mjs
```

测试只使用临时 SQLite 与本机替身端点。Unity 模拟验收服务必须显式运行 `tests/fixture-server.mjs`，并指定 `/tmp/` 下的数据库；界面会标注“模拟模型验收”。它不会被普通启动器或可分享试玩包自动启用。

来源与适配说明见 `UPSTREAM.md`。Node 运行时许可位于 `licenses/NODE-LICENSE.txt`。
