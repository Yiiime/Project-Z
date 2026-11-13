# 可移动 macOS 试玩包

本工具在 Unity 构建的 app 内加入 Node、两种架构的 FFmpeg、本地角色/直播服务、生产依赖和经过授权的运行配置。接收者无需 Unity、Node、Python、原工程或团队 GitHub 登录。所有文件位于 `Contents/Resources/ProjectZHost`，可以单独移动 app。

## 打包

先从当前分支运行 `Seabreeze.Editor.MemoryPlaytestBuild.BuildDay01`，将 app 输出到仓库外。然后运行本目录的 `package.mjs`，传入以下文件路径参数：

- `--app`：构建好的 macOS app（必须在仓库外）。
- `--config-source`：已获准分享的模型/TTS/ASR 配置目录。
- `--voice-source`：已获准分享的 RTC/声纹配置目录。
- `--node`：同时包含 arm64/x86_64 的 Node 24 可执行文件。
- `--node-license`：Node 完整许可证文件。
- `--ffmpeg-arm64`、`--ffmpeg-x64`：各架构的 FFmpeg 可执行文件。
- `--ffmpeg-license`：所分发 FFmpeg 的许可证与来源说明。
- `--tts-config`：可选的角色音色配置 JSON。
- `--netease-session`：可选；仅在账号持有人明确同意分享登录会话后使用。

凭据参数只传源文件路径。不要把凭据写在命令参数、提交、PR、验收报告或日志中。配置导出仅接受固定字段，不复制个人存档、任意环境变量或原电脑绝对路径。音乐会话仅复制 cookie，不复制其他浏览器资料。

`package.mjs` 从 Git 跟踪文件复制服务，使用锁文件安装生产依赖，禁用团队登录，检查架构并重新进行 ad-hoc 签名。未提供 Apple Developer 证书和公证；接收者首次打开可能需要在 macOS「隐私与安全性」允许此开发版。

## 启动与数据

直接打开 app。启动器自动选择两个空闲的本机端口，等待服务就绪，然后启动 Unity。服务仅监听 `127.0.0.1`。关闭游戏后回收本次服务；启动器崩溃时 IPC 断开也会停止服务，避免后台残留。

默认数据目录为用户的 `Library/Application Support/Project Z Portable`。角色记忆、存档、日志、音乐会话续期和服务临时文件都写到这里。服务源文件首次复制到按包版本区分的运行目录，签名 app 保持不变。相同数据目录同时只允许一个实例。

这是联网体验包：模型、语音和音乐仍需网络、有效凭据及供应商额度。接收者可以读取包内凭据并使用同一账号；音乐会话失效后需重新登录。分享范围由账号持有人决定。

## 验证

- `node --test unity-sideview/Tools/PortableHost/portable.test.mjs`：模拟配置白名单、外部环境隔离、路径移动、重复启动、正常退出、强制结束和过期锁恢复。
- 移动 app 到新目录，设置新的 `Z_PORTABLE_DATA_DIR`，执行 app 中的 `Contents/MacOS/ProjectZLauncher --check`；自动启动、验证并关闭服务。
- 使用 `--serve` 保留服务后，用包内 Node 运行 `verify-live.mjs <ProjectZHost目录> <独立数据目录>`：会实际消耗少量供应商额度，检查模型、九个角色音色、合成音频识别、RTC/MMPG 建立会话、音乐登录搜索和直播会话创建。报告不包含密钥，也不使用麦克风。
- 实际打开 app 检查游戏入口，并核对退出后没有服务残留。`codesign --verify --deep --strict` 必须在运行前后都通过。
- ZIP 使用 ZIP64（Unity 资源文件可能超过 4 GB），验证 CRC、UTF-8 文件名、解压后签名及独立启动。

上述连麦检查只验证供应商会话建立，不等同于另一台电脑上的真实麦克风连麦验收。
