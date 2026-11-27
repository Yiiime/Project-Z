# Project-Z 团队游戏服务

## 团队成员首次试玩

1. 把 GitHub 用户名交给维护者，由维护者加入游戏服务白名单。GitHub 仓库的协作权限与游戏访问资格分别管理。
2. 在 macOS 用 Unity Hub 和 **6000.3.27f1** 打开 `unity-sideview/`，双击 `Assets/Seabreeze/Scenes/Street.unity` 并进入 Play。普通 macOS App 也使用同一云端入口。
3. 在自动打开的浏览器内输入 Unity 显示的六位配对码，用本人的 GitHub 账号授权，再回到游戏首页。下次 Play 或启动 App 会恢复有效会话。编辑器按工程目录分别保管登录状态，换工作树可能需要重新配对。
4. 在编辑器 Console 搜索 `[Seabreeze] 服务模式` 查看实际模式与来源。普通启动默认显示「团队云端」；如需调试本地后台，先停止 Play，选 **Seabreeze → 服务连接 → 本地开发**，再进入 Play。选回 **团队云端（默认）** 即可恢复。启动器显式指定模式时，菜单会灰显。[本地服务启动与配置](../DevServices/README.md)

一期云端支持 NPC 对话、直播上岛、普通麦克风识别（ASR）以及标准和定制音色朗读（TTS）。团队成员只需自己的 GitHub 身份，模型和语音凭据由服务端管理。当前云端登录范围为 macOS Editor 和普通 macOS App。Windows、Linux、iOS、Android 以及原生 RTC、声纹、网易云音乐接入留待后续。换设备后需重新登录；本机存档和照片需另行迁移。

## 登录与存储

macOS 的首次登录从游戏发起。浏览器先输入游戏窗口的六位配对码，再使用白名单中的 GitHub 账号授权。登录成功会通知本机 Unity，Unity 也会轮询结果，以便本机回调受限时继续。GitHub 应用的回调地址固定为 https://api.litingyi.com/auth/github/callback 。这一流程使用浏览器授权码和 PKCE，无需启用 GitHub Device Flow。

游戏网关只请求 GitHub 公开身份，以数字用户 ID 校验成员资格。游戏访问令牌有效 15 分钟，刷新令牌每次使用会更换，设备会话最长 30 天。macOS 钥匙串保存刷新凭证。服务端按设备保存令牌摘要，按成员隔离游戏工作进程和数据；同一成员的多个设备共享服务端身份。本地进度、照片和直播房间缓存也按成员区分。

## 在部署主机配置 OAuth

在 GitHub 的 Settings → Developer settings → OAuth Apps → Project-Z 中获取 Client ID 并生成 Client secret。在服务器交互终端执行以下命令，按提示依次输入两项。第二项输入时不显示字符。

~~~sh
sudo /usr/local/sbin/project-z-oauth-config
~~~

该工具以 root 将配置原子写入 /etc/project-z-cloud/oauth.json（0600）。systemd 在文件首次出现时启动网关，并以服务专属凭据交付配置。后续轮换时，再执行工具后运行 sudo systemctl restart project-z-cloud.service，让进程读取新凭据。交接与备份时同时保护 identity-key、成员配置、网关数据库及游戏数据；identity-key 决定成员与既有游戏数据的关联。

## 维护成员和会话

维护者先用 `gh api users/GITHUB_LOGIN --jq '{login, id}'` 查询 GitHub 账号，将 `GITHUB_LOGIN` 换成成员提供的用户名，核对返回的登录名和数字 ID。在服务器管理终端中按需要选择一项操作，将下表中的 `ID` 换成该数字。普通成员无需 SSH 账号。

| 管理操作 | 在服务器运行 | 生效结果 |
| --- | --- | --- |
| 添加成员 | `sudo /usr/local/sbin/project-z-members add ID` | 成员可用自己的 GitHub 账号首次配对 |
| 撤销一台设备 | `sudo /usr/local/sbin/project-z-members devices ID`，再执行 `sudo /usr/local/sbin/project-z-members revoke-device ID LABEL` | 用首条命令输出的设备标签替换 `LABEL`。该设备会话失效，成员仍可重新登录 |
| 注销所有设备 | `sudo /usr/local/sbin/project-z-members revoke-sessions ID` | 所有设备都需重新配对，成员保留资格 |
| 移除成员 | `sudo /usr/local/sbin/project-z-members remove ID` | 移出白名单并撤销全部设备会话，保留已有数据 |

用 `sudo /usr/local/sbin/project-z-members list` 核对当前白名单。变更无需重启服务，网关在请求时读取成员资格，并定期复查已打开的业务流。成员也可在游戏暂停页选择「退出账号并结束游戏」注销当前设备。

## 部署结构

- TLS 站点 api.litingyi.com 只包含 ops/nginx-game.locations 列出的认证和游戏路径。既有 /status 只表示入口可用。Nginx 将游戏请求交给 127.0.0.1:49510。
- project-z-cloud.service 使用独立系统账号及 systemd LoadCredential。SQLite 位于 /var/lib/project-z-cloud；/etc/project-z-cloud/members.json 存放允许登录的数字 ID。
- project-z-cloud-worker.service 在 127.0.0.1:49461 运行私有游戏主机，与原有局域网／SSH 服务分别管理。网关固定信任该私有主机的证书，内部注册凭证只在服务器使用。按成员启动 MemoryLab 和直播服务；没有活动请求或流后，空闲三分钟回收进程，数据保留。
- 服务器复用项目专用的模型与语音配置。基础对话和普通语音走各自的接口。公网业务路由采用允许清单，排除后台管理、RTC、声纹注册和外部音乐服务。budget.mjs 按 UTC 日设置成员和团队配额；provider-budget.mjs 在模型和 TTS 调用前计量，包含重试和后台生成。估算用量按保守上限计入，并非供应商账单。
- 查看状态可用 systemctl status project-z-cloud.path project-z-cloud.service project-z-cloud-worker.service 。尚未配置 OAuth 时，path 单元等待凭据，私有游戏主机可独立运行。
