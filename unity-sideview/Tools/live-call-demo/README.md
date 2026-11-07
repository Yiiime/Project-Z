# Z 直播 Demo · 只唱歌接入包

本包来自本对话迭代的 Web 本地 Demo `z-live-local`，不是 Unity 试玩服务或线上站点备份。打包日期：2026-11-07。供接入开发仓库；尚未合并、部署或替换任何已有仓库。

## 本版范围

- 唯一才艺是唱歌。主 Agent 工具校验、前端执行器、能力查询均限制画画、塔罗和配音表演；提示词同步说明。旧模块与原始 Skill 留作源码和协议溯源，不表示开放能力。
- 保留主 Agent（DeepSeek）、Vivi 2.0、原 Live2D 适配、主角人设、会话记忆、陪伴聊天、口头轻剧情与持续弹幕。
- 单麦克风点击上麦、再点下麦；原对话面板最多两条，Z 在左、玩家在右。
- 保留豆包流式 ASR、新 API Key、RTC/MMPG 声纹降噪及打断保护；声音登记仅在设置里。
- 保留安静让话、短回复、按句生成与审阅、TTS 音频边收边播、等待时缓存的 Vivi「我想想啊...」。等待语不计入正式答复或记忆。
- 弹幕依据玩家已确认的话与 Z 实际公开的内容参与；不能冒充真人授权或抢主音轨。

## 本地运行

建议 Node.js 24 LTS（使用原生 `node:sqlite`），npm 随 Node 安装。包内不包含 `node_modules`。重新执行 `npm run export` 打包还需要 Python 3；正常构建与运行不需要 Python。

```sh
npm ci
cp .env.example .env.local
# 在本地编辑 .env.local 填入配置，不提交到 Git
npm run build
npm test
npm start
```

打开 `http://127.0.0.1:4174/`。也可执行 `npm run dev`，或 macOS 的 `启动Demo.command`。不是双击 HTML 离线使用：主 Agent、语音与声纹依赖云服务，麦克风需要 localhost 或 HTTPS。

端口被占用时用 `Z_PREVIEW_PORT=4175 npm start`。同一份目录只开一个服务实例，唱歌任务库有单实例锁。配置优先读取进程环境，然后对应中文配置文件，最后 `.env.local`。`Z_DEMO_CONFIG_DIR` 可以指向本机已有配置目录，但不会复制配置或歌曲库。

## 配置与资产

| 内容 | 配置 / 文件 | 作用 |
| --- | --- | --- |
| 主 Agent | `DEEPSEEK_API_KEY` | 生成主持回复、弹幕与审阅；模型选择沿用源码 |
| Vivi | `VOLC_TTS_API_KEY` | 语音合成；唱歌生成还要求相应服务权限 |
| 豆包识别 | `VOLC_ASR_API_KEY`、`VOLC_ASR_RESOURCE_ID` | 默认新 API Key；旧鉴权可填 App ID + Access Key |
| 声纹模式 | `Z_CALL_VOICE_MODE=voiceprint`、`Z_CALL_ASR_SOURCE=api-key` | 保留目标玩家识别模式；未配置时不宣称已降噪 |
| RTC/MMPG | `VOLC_RTC_APP_ID`、`VOLC_RTC_APP_KEY`、`VOLC_ACCESSKEY`、`VOLC_SECRETKEY` | 需要该应用对应 RTC、MMPG 和声纹服务权限 |
| 玩家声纹 | 设置 → 连麦声音设置 | 新玩家自行登记；本包不带个人 VoicePrintId 或录音 |
| Live2D | `ui/live2d/`、`ui/live2d-config.js`、`ui/live2d-stage.js` | 包含本机现有 Haru 示例、依赖和来源条款；Core 仍按原配置远程加载 |
| 唱歌片段 | `ui/audio/songs/`、`ui/talent-config.js` | 保留已验收《晚安》副歌和对应 SHA-256、验收记录 |

Live2D 示例与人物插画不使用本项目代码的 MIT 许可；原来源与条款随资源保留，见 `ASSETS.md` 和 `ui/live2d/licenses/`。如仓库已有 Z 的正式模型，替换 `ui/live2d-config.js` 的资源及参数映射，不改主 Agent 或 Vivi 链路。

密钥、原玩家声纹、个人会话、SQLite 数据库、生成候选歌声、测试录音、日志、缓存、Git 历史和 Sites 账户绑定均不在包内。浏览器存储的历史也不会随源码迁移。原 Demo 与原站点保留。

## 接入开发仓库的位置

建议先将整包放进一个独立子目录跑通，再逐项替换宿主接口；不要把本地预览服务直接暴露到公网。

| 边界 | 主要文件 | 接入要求 |
| --- | --- | --- |
| 页面与对话布局 | `ui/index.html`、`ui/app.js`、`ui/call-ui.js`、`ui/call.css` | 维持单麦克风、两条对话与音频互斥 |
| 会话调度 | `ui/call-engine.js`、`ui/conversation-engine.js` | 使用同一事件、取消及播放回执生命周期，避免多建 Agent |
| Agent 服务 | `worker/agent-director.js`、`ui/agent-client.js` | `/api/agent/config`、`/api/agent`；按句 NDJSON 与最终结果校验 |
| 人设与策略 | `skills/`、`main-agent/`、`scripts/agent-bundle.mjs` | 编辑源文件后构建；原导入文件带哈希锁，新增产品适配独立存放 |
| 只唱歌策略 | `ui/talent-config.js`、`main-agent/prompts/singing-only.txt` | `LIVE_PROFILE` 是执行白名单；不可仅改文案就重新开放工具 |
| 语音与形象 | `worker/volc-tts.js`、`ui/speech-director.js`、`ui/live2d-stage.js` | TTS 凭证只在服务端；口型和播放进度沿用真实音频 |
| 普通 ASR | `server/asr/`、`ui/doubao-recognition.js` | HTTP 配置 + `/api/asr/stream` WebSocket；16 kHz 单声道 PCM |
| 声纹连麦 | `server/voiceprint/`、`ui/voiceprint-recognition.js` | RTC SDK、MMPG 桥接、登记与逐轮目标说话人校验 |
| 唱歌 | `server/singing/`、`ui/talent-director.js` | SQLite 任务、缓存、准备/试听/播放与回执；不能把 ready 当完成 |
| 本地服务装配 | `scripts/preview-server.mjs` | 原生 Node 服务装配 Worker、ASR、声纹、唱歌；最小可运行接入参考 |

`preview-server.mjs` 绑定回环地址，模拟单一用户身份。开发仓库的服务端应替换为真实鉴权、用户与房间隔离；为 WebSocket 设置同源与会话校验，保持流式响应和客户端取消信号，禁止反向代理缓存/缓冲流。Node WebSocket/SQLite 桥接不等于可直接部署的纯 Worker。

主流程：玩家音频 → 目标说话人/打断保护 → ASR → 文本确认 → 连麦调度 → 主 Agent 按句输出 → 逐句审阅 → Vivi 流式音频 → 播放/口型 → 公开回执 → 记忆与弹幕。所有未播完的候选均不能伪装为已经说过；暂停、下麦和新轮次必须取消旧生成、审阅、预合成及待播音频。

## 检查与已知边界

`npm test` 为离线模拟回归；`npm run test:singing-local`、`npm run test:asr-bridge`、`npm run test:voiceprint`、`npm run test:voiceprint-bridge` 为对应链路测试。浏览器与付费云端诊断脚本单列，不随 `npm test` 自动调用。浏览器专项脚本需要另装 Playwright 和浏览器；测时脚本需要真实凭证及对应环境。

本次实际检查见 `VERIFICATION.md`；文件清单与 SHA-256 见 `MANIFEST.json`。`npm run verify:package` 可检查解包后的交付文件，允许后来生成的 `dist/`、`node_modules/` 和本地配置存在。

已知问题仍保留，不能据“打包通过”认为已解决：

1. 人物信息相关回复可能被语义审阅反复退回。此前六轮诊断中三轮未在三次尝试内完成；“连接异常”错误分类已修正，但语义一致性仍需继续验收。
2. 曾出现 ASR 句尾漏字；目标声纹对真实办公室多人说话的效果尚未完成系统验收，不保证完全排除旁人。
3. 历史小样本的正式首声中位数由约 2727 ms 到 2277 ms，不代表当前网络或新提示下的固定延迟。「我想想啊...」是等待反馈，不是正式回答提速。
4. 唱歌沿用 Web Demo 的后台准备和人工试听验收；新生成片段不会未经验收自动公开播放。本包不混入其他 Unity 分支的唱歌流程改动。
5. 跨场玩家记忆仍需要接入实际身份与记忆服务；现有会话笔记/有限公开事件不等于完整长期记忆。
