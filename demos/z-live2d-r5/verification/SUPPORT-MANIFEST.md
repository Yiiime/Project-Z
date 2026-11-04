# R5 支持层文件清单

## 纳入

- `server/server.mjs`、`server/performance-contract.mjs`：本地 HTTP 服务和表演标签契约。
- `server/Z_RUNTIME_SKILL.md`：已确认的 Z 运行时角色规则及知识、记忆边界。
- `server/codex-provider.mjs`：只读、无工具的本机 Codex CLI provider 适配。
- `server/start-local.mjs`：本机启动器，默认只监听 loopback。
- `server/package.json`、`server/.env.example`、`server/.gitignore`：运行元数据、无值配置模板和数据库忽略规则。
- `server/server.test.mjs`、`server/test-codex-provider.mjs`：不调用真实 LLM 的服务与 provider 测试。
- `server/README.md`：服务协议和接入说明。
- `verification/r5-summary.json`、`verification/faces/`：脱敏的既有验收摘要与两张局部画面。

## 明确排除

- `server/.env`、`server/data/` 及 SQLite/WAL/SHM：本机 provider 选择、状态和聊天记录。
- API Key、Codex 登录状态、认证文件、真实对话、日志、临时目录。
- `.local/`、`Library/`、`Temp/`、`Logs/`、`Obj/`、构建 App、Cubism SDK 原包和下载缓存。
- 其他人物、美术试验、PSD/原画、旧 ZIP、完整预览截图集。

## 上传前检查

以下命令从交付根目录执行：

```sh
cd server
npm test
rg -l --hidden -g '!*.sqlite*' -g '!server/.env' -g '!server/.env.example' -g '!MANIFEST.md' '(BEGIN .*PRIVATE KEY|/Users/|/Volumes/|AKIA[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9_]{20,})' ..
```

第二条命令只输出命中文件名，不打印潜在敏感值。它用于发现 staging 目录中的凭据或私人绝对路径；测试里出现的 `/tmp/` 是临时目录断言，不是用户路径。若命中，先人工检查并移除再提交。
