# 局域网玩家包与 Mac 主机

同一可互访局域网内，玩家只需解压原始玩家包，双击「开始试玩.command」。模型、NPC TTS、直播主持、弹幕和唱歌服务运行在主机 Mac；玩家不填写供应商 API、不安装 Node，也不安装系统级证书。支持 macOS 13.5+ 的 Apple Silicon 与 Intel Mac。

## 主机操作

1. 主机沿用工程现有 DeepSeek 和火山语音配置。双击工程根目录的「启动局域网主机.command」，保持窗口运行；它会防止闲置睡眠，但不能保证合盖后可访问，请不要合盖。
2. 主机首次启动生成仅绑定本机私有 IPv4 的 HTTPS 网关，默认端口49460。其他电脑必须能访问这个局域网地址；同一 Wi-Fi 名称不一定代表允许设备互访，访客网络和企业网络可能隔离客户端。
3. 需要停止时用「停止局域网主机.command」，或在主机终端按 Control+C。只停止局域网网关及它自己的玩家服务，不影响原有本机单人试玩服务。
4. 默认允许6台新试玩电脑登记身份，属于容量保护设置，并不代表已做6人并发性能验收。可在停止主机后修改 `Tools/LanPlay/.local/host.json` 的 `maxPlayers`。玩家数据在 `.local/players/`，登记表在 `.local/players.json`；备份时一起保存。将登记表中某项标为 `disabled:true` 后重启可停用该身份。

主机IP变化时，启动器会明确报错，不覆盖玩家数据。停止主机、备份 `.local`，保留 `players/`、`players.json`，只移走 `host.json`、`host.key`、`host.crt`、`client-config.json` 后重新运行 setup；重新分发新的 `connection-config.json`，玩家保留原来的 `.local/lan-client`。固定 DHCP 地址可以减少这一步。

## 构建与分发

先生成 `Builds/Day01-Playtest/ProjectZ-Day01.app`。使用 Node 24：

```sh
node Tools/LanPlay/setup-host.mjs
node Tools/LanPlay/prepare-runtimes.mjs
node Tools/LanPlay/package-client.mjs
```

`prepare-runtimes.mjs` 从 Node.js 官方下载固定版本24.13.0，并核对代码中固定的SHA-256。玩家包只复制应用、客户端转发程序、两种架构的Node和许可证、公开证书与游戏邀请，不复制模型配置、TTS配置、主机私钥、服务源码、数据库或运行后的玩家身份。

输出 `Builds/Day01-LAN/`。原始分发包不可含 `.local/`；打包脚本发现此目录会拒绝覆盖。测试时复制一份客户端，或用独立临时目录。不要把自己运行过的文件夹转发给别人，否则会带上本人的存档与身份。首次打开未公证的开发包，macOS可能要求用户在系统界面确认“仍要打开”；不要求关闭系统安全功能。

## 连接与隔离

客户端仅在127.0.0.1启动轻量转发器，Unity沿用原有角色与直播接口。转发器验证包内公开证书，通过TLS连接主机。每台电脑首次登记随机身份；主机按身份建立独立MemoryLab数据库、直播实例、歌曲/声纹运行目录，并统一复用主机供应商配置。模型请求和语音合成费用均计入主机账户。

网关仅开放游戏接口白名单，配置页、记忆调试页、实验接口、后台歌曲管理和角色直播内部提交接口不可从客户端调用。直播创建时将客户端的角色服务地址替换为该玩家的主机内部地址，不信任客户端给出的任意目标。HTTP/SSE/PCM逐块转发，保留背压和取消语义。没有公网入口、路由器端口映射或自动修改防火墙。

主机重启后，各玩家内部服务端口可能变化；直播服务会使用主机提供的当前地址重绑持久化的角色会话，保留原世界、运行和房间身份，不继续访问过期端口。

## 验证

```sh
node --test Tools/LanPlay/lan.test.mjs
node Tools/LanPlay/start-client.mjs --check # 在玩家包的相同目录结构中执行
node Tools/LanPlay/verify-real.mjs /private/tmp/某个测试客户端/.local/lan-client --allow-real-api
```

本轮连接层9项检查通过：TLS信任、匿名访问拒绝、双玩家同世界编号隔离、管理接口隔离、直播绑定重写、SSE立即到达、取消向上传播及身份重连/容量边界。真实模型验证使用虚构姓名“小海”和新世界，经实际局域网IP的TLS连接完成自然对话、流式TTS、角色绑定直播、事件流和主播音频SHA-256校验；玩家进程清除了供应商密钥环境变量。

现有直播HTTP和统一角色绑定回归17项通过，包括主机端口变化后恢复会话并实际调用新的角色服务。最终包中的两种Node运行程序均已实际输出24.13.0（Intel版本在本机Rosetta下启动）；应用通过`codesign --verify --deep --strict`。结构化结果见[verification.json](verification.json)。

这次真实传输检查的对话为51字、PCM440718字节、45个数据包、首音频469毫秒、合成结束2218毫秒；直播音频75885字节校验通过。数值是本次单次测量，不能代表不同网络或多人同时使用时的延迟保证。

游戏二进制复用已验收的Day0–1版本，没有修改剧情或音频播放器。本轮是同机经局域网IP的客户端→TLS网关→独立服务验收，未冒充另一台实体Mac或真实Wi-Fi链路的测试，也没有重跑一次图形通关。拿到另一台Mac后仍需确认实际局域网互访和系统首次启动确认。
