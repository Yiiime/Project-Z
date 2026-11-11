# 《来自月亮的 Z》第一日原创配乐

四首 Demo 配乐使用专门编写的旋律、和声、配器和合成器生成，无外部录音、音乐采样或现有歌曲改编。这里保留可复现的制作源和 FLAC 无损母带；Unity 使用 `Assets/Seabreeze/Resources/Audio/Original/` 下的 Vorbis OGG。

| 曲目 | 用途 | 速度 | 配器 |
|---|---|---|---|
| 海风来信 | 上午／午后室外探索 | 78 BPM | 柔和钢琴、尼龙拨弦、低音、轻刷鼓 |
| 灯火与潮汐 | 傍晚／夜晚室外 | 70 BPM | 电钢琴、暖和弦、低音、稀疏刷鼓与铃音 |
| 为你留一扇窗 | 首页、家、书屋、咖啡馆 | 64 BPM | 稀疏钢琴、拨弦、柔和长音，无鼓 |
| 烟花之后 | 第一日片尾 | 72 BPM | 钢琴主题回归，高音答句、低音与轻刷鼓 |

每首 24 小节、完整 A–B–A 变化，约 74–90 秒。统一 44.1 kHz 双声道；准确时长、峰值和 RMS 见 `render-manifest.json`。乐器释放、混响尾音沿乐句边界循环，不通过整首淡出来隐藏接缝。OGG 导入采用 Streaming，避免四首长音乐全部解压到内存。音乐播放本身使用 Unity 原生 AudioSource，无新增 Unity 插件。

## 制作来源与限制

先尝试了当前可用的 PixelLab 音乐生成服务，但服务返回账号需 tier 2，未生成音频、未升级订阅。最终交付是本项目原创的程序作曲／合成配乐，不是 PixelLab 生成音乐，也不是专业乐手录音。它可以作为完整可听的 Demo 配乐；后续正式制作最有价值的提升是真实演奏的力度、触键和弦乐质感。

复现：在带有 `numpy` 的 Python 环境、安装 `ffmpeg` 后执行 `python render_score.py`。所有旋律、和声与声音生成规则在脚本内，随机种子固定。Python 与 NumPy 仅用于离线资产制作，不进入游戏运行依赖。

## 接入

- `MusicDirector.Initialize(GameSession)` 建立独立的两个音乐音源，地点或时段变化时以 2.4 秒平滑交叉淡入。
- `MusicEnabled`／`ToggleMusic()` 仅控制背景音乐；用户选择保存在 `Seabreeze.Music.Enabled` 偏好中，海浪、脚步、对话／直播声音保持独立。
- `SetMoodOverride("credits")` 可选择片尾音乐；传 `null` 恢复地点／时段规则。
- `SetCinematicSuppressed(true)` 为影片自身音轨留出空间，结束时设为 `false`。这个操作不改写用户音乐开关。
- 普通聊天时音乐自动降低至 55%。直播沿用现有 NativeLiveHost 的世界音频暂停机制。
- 旧 `AmbientAudio.MakePad` 已移除，避免音乐开关关闭后仍有另一条和弦音乐持续播放；原有海浪、脚步保留。

音频工程检查与 Unity 实机验收是两个层次。母带时长、幅度、编码和循环边界可离线核查；场景切换、暂停、聊天、直播及开关的实际结果由本轮统一 Unity 验收记录报告。
