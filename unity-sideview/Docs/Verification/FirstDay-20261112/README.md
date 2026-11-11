# 第一日验证记录

2026-11-12，Unity 6000.3.27f1；项目 Project-Z-map-20261107/unity-sideview，分支 task/seanlee111/title-day-dialogue-1112。

使用项目 Temp/FirstDayReview 下的隔离存档与照片，未覆盖真实玩家进度。通过现有 Unity Pipeline 的即时 script eval、真实 Input System 按键事件和 UI Button 事件验证，没有创建临时测试脚本／测试工程，没有运行测试套件。

已实际观察：

- 新玩家统一标题页 →「开始故事」菜单，首页音乐来源为「为你留一扇窗」。
- 原生直播界面真实 Live2D 渲染、来信入口与邀请确认；此机无服务连接，未验证 AI／语音回应。
- 抵岛影片：VideoPlayer `isPrepared=true`、`isPlaying=true`，11.166 秒时已解码，780 帧，结束后进入 Harbor。
- 码头 Z 到场；真实 D 移动到交互范围，E 打开全屏剧情，玩家输入锁定；读完寒暄进入 WalkToSquare。
- 持续 D 行走跨过 Harbor → Street → Square，两个自动边界无需 E；进入 MeetIslanders。
- 广场八名 NPC 均 `IsPresent=true`，站位 x=-7.4,-5.35,-3.3,-1.25,0.8,2.85,4.9,6.95，全部处于当前场景。
- 广场按 P 写入一张真实照片；隔离存档 records=1。
- 依次行走并按 E 交谈，自动存档已记录 Z、梨园、李庭、阿星四人，未重复计数。

验收中编辑器退出，机器随后锁屏；当前阶段保存为 Square / MeetIslanders / 4 of 8 / 1 photo。已重开编辑器，等待解锁继续。以下仍待完成，不标记通过：

- 其余岛民介绍、8/8 后的 Z 事件和烟花影片完整播放。
- 回房、床交互、完整片尾、进入第二天。
- 存档重进继续、换档／取消恢复、照片大图与收藏持久化。
- 左下角音乐开关实测，听感与实际换场。
- 配置现有服务后的真实直播玩法。

离线媒体：两影片 ffprobe 验证 1920×1080、30 fps、H264+AAC，26／25 秒；四首音频有效解码、母带无削波。音乐由原创程序作曲产生，非云端音乐模型生成。首次 Unity 编译发现缺少内置 Video 模块，已启用 `com.unity.modules.video` 并经 Package Manager Resolve 后编译成功。
