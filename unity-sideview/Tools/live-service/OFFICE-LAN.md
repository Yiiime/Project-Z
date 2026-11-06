# 办公室直播试玩（Mac）

仅接入直播、连麦、语音、点歌。记忆服务保持原状，不通过此后台共享。

## 主机

在本目录配置 `.env.local`（已有配置可沿用），安装 Node 24+ 和 `npm ci`。模型、TTS、ASR 的 Key 只在主机读取。

首次运行 `node scripts/office-setup.mjs <本机局域网IPv4>`，生成 `.runtime/office/` 中的证书、服务器私钥、六份独立客户端凭据。然后双击本目录的 `启动办公室直播.command`。专用 HTTPS 端口 4181，仅绑定指定内网地址；原本机入口仍使用 127.0.0.1:4180。

主机需接电、开机、联网并保持启动窗口；启动器阻止闲置睡眠，但不能保证合盖后仍可用。不要合盖。客户端和主机需在允许互访的办公室网络；防火墙需允许 Node 的该连接。这里不自动修改防火墙、路由器或系统设置，也不开放公网。

## 第一台 Mac

Unity 菜单 `Seabreeze > Build Office Live Mac (Player 1)` 输出 `Builds/OfficeLive/Player-1/Z Office Live.app`。构建器只放入地址、该玩家访问凭据和证书指纹，不复制后台、模型 Key 或服务器私钥。

把完整 `.app` 压缩包交给第一位同事，解压打开，按 Tab → Z 的直播。测试：进入直播、发文字、连麦说话、退出再进入。不要把 Player 1 的包发给所有人：每位玩家应获得相应 `player-N.json`，放在其 `.app/Contents/Resources/zlive-connection.json`；替换配置应在分发前完成，并运行 `codesign --force --deep --sign - "应用路径.app"` 重新本地签名。此包未做 Apple 公证，首次打开可能需要由同事在系统中确认。

每个凭据最多两个未结束直播会话。不同凭据的会话、回执、语音和点歌任务隔离；曲库成品可共用。六份凭据只控制访问，不代表已完成六人并发性能验收。模型账单仍记主机账号。

本机执行 Unity 编辑器方法 `Seabreeze.Editor.LiveLanPlaytestBuild.VerifyConnection` 可验证实际 C# 传输层的 HTTPS、认证、创建/结束会话和错误证书拒绝。此检查不替代第二台电脑的防火墙/Wi-Fi 互访与音频试玩。

## 更换地址或停用访问

建议由办公室网络管理员为主机保留 DHCP 地址。地址改变时，先关闭办公室后台，归档 `.runtime/office` 后重新 setup、重建客户端；旧会话和任务保存在归档中，不会自动迁移。不要直接覆盖数据库。

删除 `host.json` 的某个 `tokens` 项并重启后台可停用对应访问。要全部停用，关闭后台即可。玩家配置应私下发给对应同事，不提交 GitHub。

不运行 `tools/team/z ship`：本任务保持草稿 PR，由用户体验确认后另行决定是否合并。
