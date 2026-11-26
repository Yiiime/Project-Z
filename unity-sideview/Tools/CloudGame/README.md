# Project-Z 团队游戏服务

macOS 的首次登录从游戏发起。浏览器先输入游戏窗口的六位配对码，再使用白名单中的 GitHub 账号授权。登录成功会通知本机 Unity，Unity 也会轮询结果，以便本机回调受限时继续。GitHub 应用的回调地址固定为 https://api.litingyi.com/auth/github/callback 。这一流程使用浏览器授权码和 PKCE，无需启用 GitHub Device Flow。

游戏网关只请求 GitHub 公开身份，以数字用户 ID 校验成员资格。游戏访问令牌有效 15 分钟，刷新令牌每次使用会更换，设备会话最长 30 天。macOS 钥匙串保存刷新凭证。服务端按设备保存令牌摘要，按成员隔离游戏工作进程和数据；同一成员的多个设备共享服务端身份。本地进度、照片和直播房间缓存也按成员区分。

## 在部署主机配置 OAuth

在 GitHub 的 Settings → Developer settings → OAuth Apps → Project-Z 中获取 Client ID 并生成 Client secret。在服务器交互终端执行以下命令，按提示依次输入两项。第二项输入时不显示字符。

~~~sh
sudo /usr/local/sbin/project-z-oauth-config
~~~

该工具以 root 将配置原子写入 /etc/project-z-cloud/oauth.json（0600）。systemd 在文件首次出现时启动网关，并以服务专属凭据交付配置。后续轮换时，再执行工具后运行 sudo systemctl restart project-z-cloud.service，让进程读取新凭据。交接与备份时同时保护 identity-key、成员配置、网关数据库及游戏数据；identity-key 决定成员与既有游戏数据的关联。

## 维护成员和会话

仅服务器管理员使用 /usr/local/sbin/project-z-members。list 显示成员数字 ID，add 和 remove 接受 GitHub 数字用户 ID。devices 列出某成员的设备会话标签，revoke-device 撤销指定标签，revoke-sessions 撤销该成员的所有设备。remove 同时移除资格及设备会话。成员资格随请求读取，已建立的代理流也会周期性检查撤销状态。客户端的暂停菜单支持退出当前设备。

## 部署结构

- TLS 站点 api.litingyi.com 只包含 ops/nginx-game.locations 列出的认证和游戏路径。既有 /status 只表示入口可用。Nginx 将游戏请求交给 127.0.0.1:49510。
- project-z-cloud.service 使用独立系统账号及 systemd LoadCredential。SQLite 位于 /var/lib/project-z-cloud；/etc/project-z-cloud/members.json 存放允许登录的数字 ID。
- project-z-cloud-worker.service 在 127.0.0.1:49461 运行私有游戏主机，与原有局域网／SSH 服务分别管理。网关固定信任该私有主机的证书，内部注册凭证只在服务器使用。按成员启动 MemoryLab 和直播服务；没有活动请求或流后，空闲三分钟回收进程，数据保留。
- 服务器复用项目专用的模型与语音配置。基础对话和普通语音走各自的接口。公网业务路由采用允许清单，排除后台管理、RTC、声纹注册和外部音乐服务。budget.mjs 按 UTC 日设置成员和团队配额；provider-budget.mjs 在模型和 TTS 调用前计量，包含重试和后台生成。估算用量按保守上限计入，并非供应商账单。
- 查看状态可用 systemctl status project-z-cloud.path project-z-cloud.service project-z-cloud-worker.service 。尚未配置 OAuth 时，path 单元等待凭据，私有游戏主机可独立运行。
