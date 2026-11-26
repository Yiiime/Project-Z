# 本地试玩与团队游戏后台

Unity 同时需要角色记忆服务（NPC、剧情、对白语音）和直播服务（主播、上岛邀请、点歌、连麦）。团队文字模型网关只负责推理。下列命令在仓库的 `unity-sideview/` 运行。

## 在自己的电脑开发

安装 Node.js 24、npm 和 FFmpeg。首次配置执行：

```sh
node Tools/DevServices/init.mjs --install
node Tools/team-model/login.mjs
node Tools/DevServices/local.mjs start
```

第一条从两个 npm 锁文件安装依赖，在 `~/.config/project-z/services/` 创建空模板，保留已有私有配置。第二条用于能访问团队内网的成员；供应商直连模式跳过这一步。第三条在前台启动两个后台，保持这个终端打开。在 macOS Unity Editor 停止 Play，选择 **Seabreeze → 服务连接 → 本地开发**，再进入 Play。

```sh
node Tools/DevServices/local.mjs status
node Tools/DevServices/local.mjs app "/path/to/ProjectZ.app"
node Tools/DevServices/local.mjs stop
```

从另一个终端运行上述命令。`status` 区分进程存活、配置存在与供应商调用；它不消耗推理或语音额度。`app` 将这两个服务传给 macOS 游戏，并用独立的试玩存档打开。默认角色、直播仅监听回环端口 8942、8943。私有连接保存在项目的 `UserSettings/ZLive/local-connection.json`。若已有外部连接，明确使用 `start --activate` 切到本机；正常停止会恢复此前由该启动器替换的连接。可用 `Z_DEV_CONFIG_DIR` 指定其他凭据目录，用 `Z_DEV_STATE_DIR` 设置独立数据库、日志与运行状态目录。切换后台后退出 Play 再进入。

### 在独立存档中验证上岛邀请

要检查真实供应商响应，可执行 `node Tools/DevServices/check-arrival.mjs --allow-real-api`。该命令会使用模型与语音额度，在独立的临时运行目录中按新游戏实际使用的无绑定流程检查主播、语音和邀请。附加 `--bound` 可单独检查带角色记忆绑定的房间。附加 `--connection 私有连接文件` 可验证已有的本机或隧道后台。诊断脚本代替 Unity 提交播放回执，因此结果只覆盖服务协议，游戏画面与实际扬声器播放需要另行验收。

### 主机上的私有配置

| 体验 | 配置 |
| --- | --- |
| NPC 与直播文字模型 | 在 `.env.local` 选 `Z_DEV_PROVIDER=team`（团队内网、每位成员 GitHub 登录），或 `direct`（主机自行承担供应商调用）。直连两项服务可共用 `model.json` 中的 DeepSeek 配置。 |
| NPC 和直播发声 | 在 `tts.json` 填火山语音 API Key 与有授权的声音、资源；同一个 API Key 可供两项后台读取。实际成功取决于语音授权与额度。 |
| 麦克风识别 | 在 `.env.local` 填 `VOLC_ASR_API_KEY`，须有对应识别服务权限。 |
| 原生声纹连麦 | 按模板配置 `Z_CALL_VOICE_MODE`、RTC App ID/App Key、火山 AK/SK 及识别权限。共享主机让每位玩家登记自己的声纹。 |
| 直播点歌 | 提供项目授权的网易云账号会话 `netease-session.json`，并安装 FFmpeg。使用已有音乐登录工具取得账号授权，不能把开发者个人音乐账号作为默认团队账号。 |

输入文件、存档、邀请和玩家令牌都留在私有目录。配置由后台读取，修改后重启。API Key 不写入 Unity、不放进分发给玩家的包。

## 一台常驻 Linux 主机，多人试玩

1. 使用专用的非特权系统账号，在其 `~/services/Project-Z` 放置仓库。空间有限时可只检出 `unity-sideview/Tools`。准备 Node 24、npm、OpenSSL、FFmpeg。以该账号运行 `node Tools/DevServices/init.mjs --install`，在它自己的配置目录填写上表要启用的服务。独立云主机访问不到团队内网时，选 `direct`。
2. 在 `unity-sideview/` 运行 `node Tools/LanPlay/team-host.mjs setup`，再运行 `node Tools/LanPlay/team-host.mjs start`。默认只监听主机回环端口 49460。首次 setup 会生成私钥、证书和邀请；同一运行目录重复 setup 会沿用已有身份。要供同一私有网络/VPN 使用，可在首次 setup 后面的参数指定本机私网 IPv4。端口可用 `Z_LAN_PORT` 指定，注册设备名额可用 `Z_LAN_MAX_PLAYERS` 指定。只把生成的 `client-config.json` 私下交给成员。
3. 需要自动恢复时将 `Tools/LanPlay/project-z-game-host.service` 放入该账号的 `~/.config/systemd/user/`。确保 Node 24 在 unit 的 PATH 内，执行 `systemctl --user daemon-reload`、`systemctl --user enable --now project-z-game-host`。管理员可为该服务账号开启 linger，让服务在注销与重启后继续运行。手动前台实例和 systemd 实例选择其一。
4. 回环部署下，每位成员需要自己的 SSH 转发权限、已校验的主机公钥及账号别名。第五步中的启动器支持加 `--ssh 团队主机别名` 自动建立或复用隧道。也可以手动保持 `ssh -NT -o ExitOnForwardFailure=yes -L 127.0.0.1:49460:127.0.0.1:49460 团队主机别名`。这把本机端口转发到服务器回环监听端口。私网/VPN 直连部署可跳过隧道。
5. 在成员电脑执行下列一条命令。若已用私网/VPN 直连，省略 `--ssh 团队主机别名`。邀请模式从连接文件读取；团队网关模式要求每个人完成 GitHub 登录，直连模式以私有网络或 SSH 权限、邀请和独立设备身份接入。

```sh
node Tools/LanPlay/start-client.mjs --editor --ssh 团队主机别名 --config "/path/to/client-config.json"
node Tools/LanPlay/start-client.mjs --app "/path/to/ProjectZ.app" --ssh 团队主机别名 --config "/path/to/client-config.json"
```

证书固定校验，每位设备有独立的数据库、直播状态与声纹记录。主机默认数据目录为 `~/.local/share/project-z/host`，可用 `Z_LAN_HOST_ROOT` 覆盖。升级后台时停服务、备份该目录和私有配置，更新代码并按锁文件安装依赖，然后重启。迁移时应保留证书与玩家状态。切换认证策略时用新的主机运行目录和邀请。补充的客户端分发与管理员操作见[共享试玩说明](../LanPlay/README.md)。
