# 第一日验证记录

2026-11-12，Unity 6000.3.27f1；项目 Project-Z-map-20261107/unity-sideview，分支 task/seanlee111/title-day-dialogue-1112。

通过现有 Unity Pipeline 的即时 `script eval`、实际 Input System 按键事件和 UI Button 事件验证，没有创建临时测试脚本／测试工程，没有运行测试套件。按钮事件调用不等于人工鼠标操作验收。

首轮使用 `Temp/FirstDayReview` 隔离存档；编辑器退出并锁屏后，Unity 再启动清理了 Temp。解锁后改用 `UserSettings/FirstDayReview-20261112` 下的全新隔离存档和照片，重新走完整第一日流程，没有写入正式玩家存档。以下主线与取消／恢复结论来自解锁后的第二轮实际运行，不把首轮的 4/8 检查点说成已成功恢复。

## 实际主线与中断恢复

| 项目 | 已观察结果 |
| --- | --- |
| 统一启动页 | 新玩家首先看到标题页，进入菜单后选择「开始故事」；已有存档重新 Play 时仍先显示统一标题页，再进入「继续旅程」。首页音乐为「为你留一扇窗」。 |
| 开场直播与邀请 | 进入现有原生直播和 Live2D 界面，打开本地来信并接受邀请。此机无服务配置，AI／语音回应未验收，用户明确本轮暂不考虑。 |
| 抵岛 | 26 秒影片正常播放并自然结束，进入 Harbor。首轮曾读到 `isPrepared=true`、`isPlaying=true`、780 帧；第二轮再次完整播放。 |
| 码头迎接 | 实际 D 移动到 Z 的交互范围，E 打开完整对话，输入锁定；五句寒暄完成后进入 WalkToSquare。 |
| 广场动线 | 实际持续 D 跨过 Harbor → Street → Square，两次自动边界无需 E；进入 MeetIslanders，八名 NPC 到场。 |
| 取消介绍 | 首先 Esc 取消 Z 的广场介绍：认识人数仍为 0，玩家解锁；取消未伪造会面完成。 |
| 全员会面 | 实际 E 依次听完八位岛民介绍，认识人数达到 8/8。全部沿用现有角色资源，部分外观重复。 |
| 取消烟花邀请 | 8/8 后与 Z 对话，取消烟花确认：仍为 MeetIslanders、人数保持 8、输入解锁。 |
| 保存后重新进入 | 停止再 Play，从统一标题页选择继续，恢复 Square / 8 of 8 / 1 photo，没有重播直播或抵岛开场。 |
| 烟花播放与暂停 | 确认开启后 VideoPlayer 已准备、750 帧；13.633 秒打开跳过确认，影片暂停；选择继续观看从 13.633 秒恢复，25 秒影片自然结束后进入 PlayerHome，玩家 x=4.2。 |
| 回家与床 | 房间实际呈现比例、背景和地面协调；实际 A 走到左床，按 E 并确认睡觉，进入片尾。 |
| 完整片尾 | 正常速度滚动 46 秒，自然结束。截图分别记录项目名与理念、六位共同创作者与技术／友情支持、指导与特别鸣谢；全部指定姓名实际可见。 |
| 第二天 | 片尾后实际状态与磁盘存档均为 Complete / day=2 / period=0，玩家输入锁解除，回到 PlayerHome；音乐回到室内主题。 |
| 完成后继续 | 再次停止 Play、重新进入并选择继续，仍为 day=2 / Complete，没有重播片尾；照片与收藏同时恢复。 |
| 完成后自由探索 | 实际 D 走到客房出口、E 返回 Harbor，玩家解锁；再 D 自动跨街回 Square，广场不再强制集会。A 返回 Street 后恢复常规五人（z / zhengyi / xiaoxiaoqing / linxu / zhou_bobo），均 `IsPresent=true`、`Movement.SimulateDecisions=true`、`interactionHold=false`；白天室外选曲为「海风来信」。 |

## 音乐与照片

- 左下角 BGM 按钮实际关闭时，`lantern_tide` 音源音量从 .42 降至 0；海浪仍为 .15，`AudioListener.volume` 保持 1，再打开后音乐恢复。没有以全局音量开关冒充仅关闭 BGM。
- 标题、室外傍晚、室内与片尾实际选曲正确；片尾为 `after_the_fireworks`，音量 .48。完整听感评价与不同扬声器表现不由音源参数证明。
- 探索中按 P 保存实际 1920×1080 原图；手机详情打开且 `Time.timeScale=0` 时按 P 仍能成功，照片数由 2 增至 3。
- 影片中按 P 保存了 1920×1080 的实际影片画面，并已打开图片核对；不是预制相册图片。
- 相册列表实际加载 512 像素宽缩略图，点击详情、收藏、打开／关闭大图均已运行；收藏写入磁盘。停止 Play、重新进入并选择继续后，3 张照片和 1 项收藏原样恢复。
- 验收发现并修复两个显示问题：缩略图额外进行一次 sRGB 编码导致发白，以及全屏图片保持比例时因左侧 pivot 偏移。只在 `PhotoCaptureService.MakeThumbnail` 调整缩略图 RT／sRGB 写入状态，在 `GameUI.PhonePages` 调整大图居中；没有改原始照片或全局色彩空间。
- 修复后实际按 P 拍第 4 张，最终缩略图颜色与原图一致，大图左右留白对称。新缩略图与原图缩小结果平均绝对像素差约 2.16/255，修前约 64.37/255，剩余为缩放采样差；旧验收缩略图不自动重生成。

## 画面证据

- `01`—`07`：首轮标题、菜单、原生直播、抵岛、码头、对话与广场。
- [08-live-invitation.png](08-live-invitation.png)：解锁后重新从新游戏进入邀请。
- [09-square-eight-greeted.png](09-square-eight-greeted.png)：广场全员介绍完成。
- [10-title-returning-player.png](10-title-returning-player.png)：已有存档仍先展示统一标题页。
- [11-fireworks-playback.png](11-fireworks-playback.png)：烟花影片实际播放。
- [12-player-home.png](12-player-home.png)：影片后进入独立玩家小屋。
- [13-credits-title.png](13-credits-title.png)、[14-credits-cast.png](14-credits-cast.png)、[15-credits-thanks.png](15-credits-thanks.png)：正常滚动过程中的项目、理念和完整署名。
- [18-album-color-fixed.png](18-album-color-fixed.png)、[19-photo-centered.png](19-photo-centered.png)：最终相册缩略图颜色和居中大图。`16`、`17` 保留修复前问题记录，以 `18`、`19` 为最终效果证据。

## 编译、媒体与范围

- 解锁后首次 Unity 编译返回 `up_to_date`；相册显示修正后的重新编译为 `completed`、errors=0、warnings=0。首次导入发现缺少内置 Video 模块，已启用 `com.unity.modules.video` 并经 Package Manager Resolve 后编译成功。
- 两影片经 ffprobe 确认为 1920×1080、30 fps、H264+AAC、26／25 秒。影片由镜头移动、字幕和程序烟花构成有限动画，不是完整人物表演动画。
- 四首音乐有效解码，母带无削波；为原创程序作曲与合成，不是云端音乐模型生成，也没有冒称真人乐器录音。
- 无服务配置时，既有原生直播产生 3 条 `NativeRtcAudioReference` 多 AudioSource 错误。用户已明确真实直播配置／回应本轮暂不考虑；保留原接入口，不在此轮改动其聊天、语音、连麦或音频实现。进入岛上主线后清理 Console 再验收，不能因此声称原生直播整体无错误。
- 相册修正后的岛上整段运行，Console `groundTruth.errors=0`、`warnings=2`，两条为 memoryless depth 警告；这段结果与此前原生直播的 3 条音频错误分别记录，不覆盖或撤销后者。
- 真实直播回应、人工鼠标／设备手感和长期自由探索不属于这份第一日主线记录已证明的范围。
- 旧版存档缺少 `firstDay` 时保留自由探索的兼容行为只做了源码审查，本轮没有用真实旧存档试玩，不将它列为实际读档通过。
