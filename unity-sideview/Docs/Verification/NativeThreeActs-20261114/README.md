# 原本地流程融合 · 三幕回忆与黑屏语音

Unity 6000.3.27f1 完整构建及原生 Player 验收通过。`result.json` 为运行时断言结果，`voice.json` 记录实际播放的 AudioSource 与导入的录音长度、采样率。

- `act-1.png`：原有背景、标题、日期、原文查看与翻页按钮。
- `act-2.png`：咖啡／收音机背景与第二至三天回忆。
- `act-3.png`：原生可滚动回忆页，末句「于是——我说：」。截图滚到文末以检查这句。
- `choice.png`：切黑后的「我想走」，沿用原返回按钮。

使用当前真实存档的只读临时副本验收，恢复展示稿误上传前已完成的七日结局；测试没有调用模型重新生成选择。验证原入口、背景、标题、逐段淡入、翻页、语音只播放一次、返回恢复环境声，以及显示过程不改写已有选择、结局 JSON、时间或地点。原始 WAV 与导入文件的 SHA-256 一致。

检查结果：MemoryLab 101/101、EndingDemo 21/21；`tools/team/z check` 通过，资源引用检查及 C# 语法零错误。Unity 6000.3.27f1 完整构建和脚本增量构建均成功，macOS 包经 `codesign --verify --deep --strict` 校验，原生 Player 的 `result.json` 为通过。

试玩包已更新到 `Builds/IslandEnding/ProjectZ-IslandEnding.app`，沿用 `试玩第七晚结局.command`。
