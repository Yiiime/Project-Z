# Project Z 团队模型服务

项目成员通过 GitHub 登录后，可在本机或团队试玩包中使用维护者提供的模型。API Key 只保存在 Devbox；角色逻辑、游戏进度和记忆仍由每台电脑的本地服务维护。

当前为待上线实现：`client.json` 的 `enabled` 保持 `false`。只有配置 GitHub 应用、服务端凭据与 HTTPS 证书并完成真实验证后，才将正式客户端配置启用并合入主分支。

## 组员使用

上线后的 Unity 编辑器在点击 Play 时自动启动本机对话服务。首次打开连接页，点击“用 GitHub 连接”，在 GitHub 页面输入一次性代码；之后可以返回游戏。也可以运行 `node login.mjs` 登录。

登录状态保存在当前用户目录的 `.project-z/team-model/`，按服务地址与仓库隔离，不在仓库或游戏存档中。默认有效期 30 天，可在连接页退出。服务端最长每 60 秒重新核验一次项目成员权限；成员被移除后，超过缓存期限的新请求会被拒绝。已经授权的在途生成最多继续到本轮请求结束。

开发机与组员电脑需要能访问 GitHub；游戏需能访问 Devbox 的 HTTPS 端口。连接失败会显示错误，不会返回模拟模型回复。

第一次从旧版升级时，如 4330 端口已有旧对话进程，启动器会要求先关闭旧服务再启动本版本，不会把旧服务误报为团队连接。电脑仍需安装 Node 22.13+；项目原有启动脚本会检查版本。

## 管理员部署

目标 Devbox：`10.37.20.9`，Node 22.16.0、SQLite 与 systemd 用户服务已核验可用，`Linger=yes`。旧服务继续使用 4318；本服务计划独立使用 4338。

1. 在 GitHub 创建专用 OAuth App，并启用 **Device Flow**。主页可用项目仓库地址；设备授权不使用浏览器回调。成员授权只请求 `read:user`。
2. 为 `Yiiime/Project-Z` 创建专用 fine-grained token：只选择该仓库，只需 **Metadata: Read-only**。该凭据用于服务端核验协作者身份，不交给组员，也不复用开发者的完整 `gh` 登录凭据。
3. 将 `server.mjs`、`auth.mjs`、`common.mjs` 放在 Devbox 的 `~/services/project-z-team-model/`。将 `server.example.json` 复制为 `~/.config/project-z-team-model/server.json`，私下填写 GitHub 配置和已有模型 Key，并设置目录权限 700、文件权限 600。
4. 在私有配置目录生成服务端证书，证书 SAN 必须包含 `IP:10.37.20.9`。私钥留在 Devbox；将公钥证书 `gateway-cert.pem` 放到客户端目录。Node 客户端只信任这个证书并验证主机地址，不关闭 TLS 校验，也不修改电脑的全局证书信任。
5. 将 `project-z-team-model.service` 放入 `~/.config/systemd/user/`，执行 `systemctl --user daemon-reload` 与 `systemctl --user enable --now project-z-team-model`。用对应证书验证 `/health`，再验证真实登录与模型调用。
6. 完成下方验收后，将 `client.json` 中的 `enabled` 设为 `true`。仓库只提交服务地址、公开模型信息和公钥证书；试玩构建只复制客户端文件，不复制服务端配置、登录状态或私钥。

服务端配置中的 `npc` 沿用当前内部方舟 Responses 接口；`live` 用于 Z 的 DeepSeek 文字调用。语音合成、识别与点歌沿用现有独立依赖，本服务不代理它们。旧办公室直播共享入口也继续使用其原有配置。

并发默认总计 8、每人 3；每日每人默认最多 5000 次已接受的模型请求，按 UTC 日期计数。模型失败也消耗一次请求计数。维护者可在私有配置中调整。游戏为一次台词执行的后台记忆整理也可能产生额外模型请求。

## 验证与生效范围

在此目录运行 `npm test`；服务没有 npm 运行时依赖。测试使用隔离数据与显式上游替身，不是上线后的真实模型证明。

上线前仍须完成：两个不同项目成员在各自电脑登录并生成真实回复；非成员登录被拒绝；移出成员后撤权生效；Devbox 重启后恢复；正式 Unity Play 与构建包的首次登录、保存和读档。自动测试与 C# 语法检查不能替代这些验收。

若服务端模型地址、接口类型或 TLS 证书变化，同步更新 `client.json` 及公钥证书再分发。轮换模型 Key 仅修改 Devbox 私有配置并重启服务。

参考：[GitHub 设备授权](https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/authorizing-oauth-apps)、[协作者权限接口](https://docs.github.com/en/rest/collaborators/collaborators)。
