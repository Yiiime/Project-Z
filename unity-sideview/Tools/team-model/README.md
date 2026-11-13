# Project Z 团队模型服务

项目成员通过 GitHub 登录后，可在本机或团队试玩包中使用维护者提供的模型。团队部署的模型 Key 保存在 Devbox，仓库和玩家包不分发 Key。本机版本的角色记忆在各自电脑；局域网版本继续按玩家独立保存在局域网主机。

团队服务已部署在 `https://10.37.20.9:4338`，客户端默认启用。NPC 和 Z 直播文字对话统一使用现有方舟接入点；服务端持有模型 Key，客户端仅保存自己的团队登录状态。

## 组员使用

电脑需要 Node 24+、npm 和项目指定的 Unity 版本。Unity 编辑器点击 Play 后会自动启动 NPC 与直播本机服务；首次安装依赖可能需要多等一会儿。首次打开连接页，点击“用 GitHub 连接”，在 GitHub 页面输入一次性代码；之后可以返回游戏。也可以运行 `node login.mjs` 登录。

登录状态保存在当前用户目录的 `.project-z/team-model/`，按服务地址与仓库隔离，不在仓库或游戏存档中。默认有效期 30 天，可在连接页退出。服务端最长每 60 秒重新核验一次项目成员权限；成员被移除后，超过缓存期限的新请求会被拒绝。已经授权的在途生成最多继续到本轮请求结束。

组员电脑完成 GitHub 设备授权握手；Devbox 只需访问 GitHub API，核验令牌属于专用应用且账号仍是项目成员。游戏需能访问 Devbox 的 HTTPS 端口。连接失败会显示错误，不会返回模拟模型回复。

Day01 本地包和局域网包均包含团队客户端。局域网邀请还需配合 GitHub 身份，主机会逐次核验成员资格并把玩家档案绑定到账号；每位玩家的独立后端使用该玩家的团队会话，模型 Key 和 GitHub 管理凭据仍由 Devbox 保存。首次直接打开 app 时，如需登录，会打开团队连接窗口，完成后回到游戏点击开始或继续。

第一次从旧版升级时，如 4330 或 4180 端口已有旧服务，先关闭旧服务再启动本版本。启动器会核对团队服务标记，不会把旧服务误报为团队连接。团队 NPC 回应使用 30 秒联网预算，保留一次契约格式重试和原有取消、校验规则。

## 管理员部署

目标 Devbox：`10.37.20.9`，Node 22.16.0、SQLite 与 systemd 用户服务已核验可用，`Linger=yes`。旧服务继续使用 4318；本服务独立使用 4338。

1. 在 GitHub 创建专用 OAuth App，并启用 **Device Flow**。主页可用项目仓库地址；设备授权不使用浏览器回调，不申请额外 scope，仅确认账号身份。当前应用为 `Project Z Team Model`（Client ID：`Ov23liVoPCPo4dyL08eu`）。
2. 为此 OAuth 应用生成服务端 Client Secret，用于 GitHub API 的令牌归属校验；另为 `Yiiime/Project-Z` 创建专用 fine-grained token：只选择该仓库，只需 **Metadata: Read-only**。两者均只保存于 Devbox 私有配置，不交给组员。成员查询凭据已限定至 2027-02-11 到期。
3. 将 `server.mjs`、`auth.mjs`、`adapter.mjs`、`common.mjs` 放在 Devbox 的 `~/services/project-z-team-model/`。私有配置为 `~/.config/project-z-team-model/server.json`，目录权限 700、文件权限 600。GitHub 与模型凭据只在此配置中维护。
4. 服务端证书 SAN 包含 `IP:10.37.20.9`。私钥 `gateway-key.pem` 留在 Devbox 私有配置目录；公钥证书在客户端以 `gateway-cert.crt` 分发。Node 客户端只信任这个证书并验证主机地址，不关闭 TLS 校验，也不修改电脑的全局证书信任。
5. 将 `project-z-team-model.service` 放入 `~/.config/systemd/user/`，执行 `systemctl --user daemon-reload` 与 `systemctl --user enable --now project-z-team-model`。用对应证书验证 `/health`，再验证真实登录与模型调用。
6. 仓库只提交服务地址、公开模型信息和公钥证书；试玩构建复制本机 NPC、直播服务与团队客户端，不复制服务端私有配置、登录状态、个人数据或私钥。

服务端配置中的 `npc` 与 `live` 都使用方舟 Responses；`live` 将既有 Chat Completions 请求和流式返回做协议转换，不改写台词或跳过原有语义校验。语音合成、识别与点歌沿用现有独立依赖，本服务不代理它们。旧办公室直播共享入口也继续使用其原有配置。

并发默认总计 8、每人 3；每日每人默认最多 5000 次已接受的模型请求，按 UTC 日期计数。模型失败也消耗一次请求计数。维护者可在私有配置中调整。游戏为一次台词执行的后台记忆整理也可能产生额外模型请求。

## 验证与生效范围

在此目录运行 `npm test`；服务没有 npm 运行时依赖。测试使用隔离数据与显式上游替身，不是上线后的真实模型证明。

已用 Yiiime 账号实测真实 GitHub 设备授权、Devbox 应用归属和成员核验、NPC 合规台词、直播流式回复及主持人台词/表情。空白数据目录下的双服务启动已验证。自动测试覆盖其他应用令牌拒绝、撤权、限额、错误证书、流式中断与服务重启。

固定 Unity 6000.3.27f1 已实际编译、构建并启动独立版至姓名输入页，完整应用签名校验通过；本轮没有重新手工跑完整个首日。结果及截图见 [验证记录](verification/summary.json)。

其他组员的首次登录、多人并发容量和各自电脑的网络环境需要在团队使用中继续观察；单机验证不能代表所有组员环境均已验收。

若服务端模型地址、接口类型或 TLS 证书变化，同步更新 `client.json` 及公钥证书再分发。轮换模型 Key 仅修改 Devbox 私有配置并重启服务。

参考：[GitHub 设备授权](https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/authorizing-oauth-apps)、[协作者权限接口](https://docs.github.com/en/rest/collaborators/collaborators)。
