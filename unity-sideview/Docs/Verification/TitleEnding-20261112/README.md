# 首页与片尾验证 · 2026-11-12

## 片尾收尾复核（2026-11-12 10:14–10:16 UTC）

工程：`Project-Z-map-20261107/unity-sideview`；Unity 6000.3.27f1，Pipeline 7800。只检查此前已启动的隔离验收，未创建测试脚本、未修改游戏代码、未重开用户进度。

隔离目录：`UserSettings/TitleEndingReview-20261112/Ending`。该目录及真实用户存档均不纳入版本控制。

### 实际运行状态

在片尾已自然播放完毕后，通过 Unity CLI 读取到：

- `EditorApplication.isPlaying=true`、`isPaused=false`、`GameSession.IsAtTitle=false`。
- `firstDay.phase=Complete`、`day=2`、`period=0`。
- `GameSession.InputBlocked=false`、`Time.timeScale=1`。
- `FirstDayCinema.IsPlaying=false`、`CurrentFilm=null`。
- `SaveRepository.PathOnDisk` 指向隔离目录。磁盘 JSON 也为 phase=9／day=2／period=0，8 名已认识角色、4 张照片保留。

### 图片证据

此次逐张查看以下实际屏幕截图：

| 文件 | 可观察结果 |
| --- | --- |
| `ending-12.png` | 共同创作六位署名完整显示，左侧栈道画面；无文字裁切。 |
| `ending-30.png` | 友情支持三位署名完整显示，古树广场画面。 |
| `ending-53.png` | 右侧出现第一张相册截图，客房／玩家／原始 HUD 完整等比呈现，标注「你留下的照片」。 |
| `ending-59.png` | 左侧出现另一张相册截图，玩家与画面位置有变化；不是上一张图片复制到另一侧。 |
| `ending-66.png` | 全幅海岸游客中心画面、项目名、「谢谢你，来到这里」和创作理念；没有第一天限定。 |

`ending-04/12/22/30/38/46/53/59/66.png` 的文件时间从 10:07:19.947 到 10:08:21.972 UTC；4 秒到 66 秒节点之间约 62.025 秒，与正常播放速度一致。本次没有重新播放影片来判断所有过渡帧，也没有重新操作 Esc 暂停／跳过；不要把静态截图扩张为逐帧动态验收。

视觉限制：当前两张相册候选都来自同一客房，仅机位／角色位置不同；证明了真实相册插入，但不是最终内容挑片的多样性验证。相册截图会保留原 HUD，这是当前实现的明确行为。

### 错误边界

Unity 实时 Console：`consoleErrors=0`、`consoleWarnings=2`、`compilationFailed=false`、`compiling=false`。两条警告为已存在的 memoryless depth load/store 提示。

Pipeline 历史缓存仍保留 09:56:39 UTC 的三条 `NativeRtcAudioReference` 多 AudioSource 错误，来源是此前打开原生直播；最后新序号为 25（09:56:51 UTC 的上述深度警告），片尾期间未新增错误。这里没有把历史直播问题宣称为已修复。

### 会话恢复

复核后已停止 Play，再执行 `Environment.SetEnvironmentVariable("Z_MEMORY_SAVE_ROOT", null)`。读取结果：`playing=false`、`saveRoot=null`，存档路径恢复为用户正式 Application Support 目录。

正式 `seven-memory-save.json` 在复核前后 SHA-256 一致：

`e0e94fe793406afa276aa5c06199c63eeac4ed6839b3a948b6aa634083f9c3a9`

未提交、推送、改动首页或其他代码。
