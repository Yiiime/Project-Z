# 本地统一角色验收 · 2026-11-08

基线 `77cf536`，本地分支 `codex/npc-schema-live-local-20261108`。本轮无推送、PR、部署。完整范围见 [整改说明](../../../docs/NPC统一链路整改-20261108.md)。

## 已执行

| 检查 | 结果 | 证据与边界 |
| --- | --- | --- |
| MemoryLab + live-service 全量 Node 回归 | 170/170 通过 | `/private/tmp/z-local-all-tests-final.log`；包含紧凑 Schema 条件、NPC 轮转、实际回执、重复与迟到消息、引用存档、跨频道历史 |
| 横版静态检查 | 270 个 C# 文件，0 语法错误；资源引用通过 | `Z_DOTNET=… bash Tools/check.sh`，`/private/tmp/z-local-static-final.log` |
| Skill 官方结构校验 | 通过 | `uv run --with pyyaml python …/quick_validate.py Tools/MemoryLab/content/current/moonlife-actors`；结构校验不证明人感 |
| 真实模型最终三部分样本 | 9/9 有效 | 一次岛上 Z＋八人直播，每人单独调用；约 1.14—2.06 秒，非并发 P95；`/private/tmp/z-local-real-results.json` |
| 真实 A2A＋援助立场 | 8/8 有效 | 五轮阿星/林序当面交流，三轮玩家分别问林序、周伯伯、李庭；周伯伯没有承诺实质援助，林序可提出任务；`/private/tmp/z-local-social-results.json`。为隔离引擎测试，不冒充 Unity 物理维修完成 |
| Unity 编辑器实际流程 | 通过 | `MemoryLiveVerification.Begin`：岛上问身份→真实 UI 打开直播→玩家发弹幕→实际播出/显示→返回岛上→追问；`/private/tmp/z-local-unity-live-v2.json` |
| 全员获得直播判断机会 | 八人都被调度 | 65 秒样本中：Z 11 次，梨园/李庭/阿星各 2 次，正义/小小晴/林序/周伯伯各 1 次；不要求每轮发言 |
| Unity macOS 构建 | Succeeded，0 errors，0 warnings | `MemoryPlaytestBuild.Build`；`Builds/UnifiedCharacters/build-report.txt`；补齐了项目要求的官方 RTC 原生构建依赖 |
| 独立游戏启动 | 已实际打开 Day1 场景、模型已连接 | 本机 `Builds/UnifiedCharacters/ProjectZ.app`；独立存档，未覆盖此前试玩进度。游戏窗口沿用原工程标题“海风捎来的信” |
| 改动范围与格式 | `git diff --check` 通过 | Unity 自动重写的模型、字体缓存和工程设置已备份并恢复；不纳入逻辑改动 |

Unity 追问结果：玩家直播说“这次见面的暗号叫蓝色风筝”，回到岛上问“刚才直播里，我说见面的暗号叫什么？”，Z 回答：“蓝色风筝。你自己说的，我还在直播里应了一声呢。”同一存档两种频道的实际经历已接通。

## 发现并修复的失败

- 模型曾把禁止字段填成 false：模型契约中删除不适用的属性，并明确省略规则，接收端保留严格校验。
- 模型曾混淆公开弹幕 ID 与后台记忆 ID：分别注入可用 ID，禁止互换。
- 紧凑 action 迁移时遗漏 communicate 的 communication 必填条件：将旧条件改写为 option_id 条件，同时在模型与接收契约执行；增加回归测试。失败样本保存在 `/private/tmp/z-local-social-failed-before-contract-fix.json`。
- 首次 Unity 流程回岛后记不住刚说的暗号：逐字回执曾反复写入同一句话、近期原话又只取当前会话。改为一个进行中观察＋完成/中断后一次经历，补跨频道近期经历。保留 `/private/tmp/z-local-unity-live-first-failure.json`，修复后复测通过。
- 首次打包缺原生 RTC 组件失败：执行项目 `Tools/native-rtc/build-macos.sh`，官方 SDK 固定 SHA256 校验后构建，再次打包通过。没有声称实际连麦已验收。

## 未验证与未完成

1. 真实 RTC、麦克风、声纹、唱歌外部链路未在本轮完成实测；只保留原能力并完成原生依赖打包。
2. 没有把修理、试航、装载、拍照和最终离岛当作已经实现。仍需物件执行器、可信任务回执、启用条件与结局结算。
3. 本轮没有做新一轮数小时长局、云端并发、跨机器引用存档迁移或离屏旅行验收。旧版本的 600 秒 A2A 测试不算本轮新证据。
4. 自然度/事实问题仍存在：真实样本会把日常职责说成“刚检查过”“今天已做完”，有时连续承诺而无对应物理动作。它们保存为角色说法，没有自动变为已核实世界事实；这仍是后续语义与玩法联调要处理的问题。
5. 新三部分绑定直播暂时先收完整 JSON，再审阅和播放，不使用旧主持协议的分句流式解析。端到端可见回复还包含审阅与呈现延迟，不能把单次模型耗时当作全链路承诺。
6. 自由对话的秘密泄漏不能靠 JSON Schema 保证；记忆可见范围检查已做，仍需更广泛的多轮披露行为测试。

本地综合启动器默认使用 `Tools/MemoryLab/.local/unified-runtime/saves/` 保存新试玩；原始模型配置、数据库、房间 token、Unity 缓存和实际存档均不包含在源码 zip 中。

后续补验：花店同行“只答应不走”已单独修复并完成真实 Unity 行走、途中交谈恢复和主动取消测试，见 [同行修复验收](Journey-20261108.md)。其他未接物件动作不因此视为已完成。
