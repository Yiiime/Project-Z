# 验收记录

2026-10-23，基础工程 v0.1.0 + 第三版角色精修。当前内容仍为两段预写技术样例；Z 使用绑定模型、Animator 动作片段与头眼跟随，环境接入免费自然素材与本项目制作的道具。

| 检查 | 结果 | 证据与边界 |
| --- | --- | --- |
| C# 游戏核心 | 21 项通过 | 实际编译并运行 Core 源码；TestResults/core.txt |
| 本机角色服务 | 13 项通过 | 回环 HTTP 与模拟模型；TestResults/bridge.txt；未调用真实模型 |
| Unity PlayMode | 4 项通过 | 字体、保留的 Humanoid Avatar、面部形变、手指姿态与挥手、动作中断衔接和脚部稳定、道具尺寸、通行、保存重载与次日接续；TestResults/playmode.xml |
| Unity 导入与校验 | 通过 | URP 17.3.0、字体字形、事件与 Unity JSON 往返；TestResults/unity-verify.log / unity-build.log |
| Mac 构建 | 通过 | Builds/Project Z.app，约 380 MB，包含 x86_64 与 arm64；仅本机已实测 |
| 实机中文与镜头 | 通过 | 内嵌中文字体、探索与对话近景、表情和动作显示正常 |
| 实机键盘流程 | 通过 | 开始、移动、两段选择、推进日期、退出、重开继续与次日接续 |
| 鼠标操作 | 开始按钮通过 | 用户确认手动点击“开始一段新的相处”可正常进入；其他鼠标按钮未逐项验收 |
| 在线模型 / 完整记忆系统 | 未验收 | 默认预写回应；Memory Lab 尚未接入本游戏 |
| 海湾美术试作 | 本机检查通过 | Z 近景、开衫与长裙、海湾长椅、木屋、草木与海水；非最终美术定稿 |
| 正式美术与多机分发 | 未验收 | 衣物物理、行走与坐下动画尚未实现；未在第二台 Mac 或 Windows 上试玩 |

**实机记录**

| 路径 | 实际结果 |
| --- | --- |
| 鼠标点击“开始一段新的相处” | 用户于 2026-10-22 确认可正常进入游戏 |
| 新游戏 → 海湾 → 选“一起观察” | Z 显示对应台词、微笑与挥手；第一天经历写入存档 |
| 结束当天 → 退出应用 → 重新打开 → 继续 | 恢复到第二天；经历仍在，接续尚未消费 |
| 再到海湾交谈 | Z 提起前一天的经历；状态变为“已接续” |
| 地图桌 → 选“直接替她决定路线” | Z 表达需要先听理由，表演与语气改变 |
| 推进到第三天 → 再到地图桌交谈 | Z 提起尚未核对的路线，并提出自己去确认 |

键盘实机验收使用独立目录 TestResults/playtest-A，不覆盖正常试玩存档。运行态集成测试使用 TestResults/playmode-saves。两者都由实际游戏代码保存，未手工拼造状态来冒充通过。

**本轮修复**：移除不存在的 textrendering 包声明；内嵌 Noto 中文字体；以静态网格代替扁平圆柱默认的胶囊碰撞体；将控制器最小位移设为零；补全键盘操作、对话近景和底部提示底色。每项修复后均重新进行相关构建或运行验证。

**美术轮次修复**：保留 FBX 根节点的单位与坐标转换，修复新道具缩小 100 倍的问题；接入正确的叶片贴图；缩小花草和近景礁石；改为连续曲线步道；对话镜头拉近并隐藏前景玩家模型。PlayMode 新增道具尺寸断言与地图桌通行覆盖。素材许可、SHA-256 和编辑入口见 [美术说明](Art/README.md)。

最终美术实机检查使用独立存档 `TestResults/art-review-final`：第一天在海湾选择一起观察，第二天触发对应接续，再走到地图桌选择第二项，显示相应台词、表情与手势。实际保存结果为第 2 天、3 条事件，海湾接续已消费。运行日志 `TestResults/art-review-final.log` 未出现异常；[角色近景](Art/Screenshots/z-closeup.png)、[挥手](Art/Screenshots/z-wave.png)、[地图桌回应](Art/Screenshots/z-firm.png)、[海湾](Art/Screenshots/cove.png) 和 [木屋](Art/Screenshots/cottage.png) 均来自真实游戏窗口。

**第二版记录**

| 项目 | 结果 / 证据 |
| --- | --- |
| 回归 | 本轮 C# 核心 21 项、接口 13 项、Unity PlayMode 3 项通过；PlayMode 覆盖更新后的衣服骨骼和起伏地形 |
| 导入与构建 | `unity-art.log` 与 `unity-build.log` 通过；180 个资源与目录的 metadata 唯一，源代码差异无空白错误 |
| 模型检视 | 在真实玩家端拍摄正面、45 度、侧面、背面、脸部近景、挥手及海湾；修复背部身体/内搭穿插与下摆材质分界 |
| 新服装材质 | 两张 2048 × 2048 贴图已用于 Unity 材质，并打包到 Blender 文件；原始生成图不是实机证据 |
| macOS 启动 | 发现等待 Metal 首次画面提交；关闭玩家端垂直同步、保留 60 帧上限后完成整组截图，日志 `art-v2-final-60fps.log` |
| 镜头实机输入 | 通过。解锁并重启测试窗口后，以原生窗口按键完成进入游戏、海湾交谈、右转/左转、拉近/拉远、选择回应、退出对话和继续移动；日志 `art-v2-interaction-final.log` 无脚本或着色器异常 |

第二版自动检视截图位于 [Screenshots/v2](Art/Screenshots/v2)，由实际 Unity 场景在隐藏 UI 的检视模式生成。最后一轮拍摄使用 `TestResults/art-v2-saves`，没有覆盖正常试玩存档。照片式检视不能代替用户对角色相似度和美术完成度的判断。

补充的实机输入验收使用独立存档 `TestResults/art-v2-interaction-final`。带 UI 的截图分别记录了 [初始对话](Art/Screenshots/v2/gameplay-dialogue.png)、[旋转镜头](Art/Screenshots/v2/gameplay-orbit.png)、[拉近镜头](Art/Screenshots/v2/gameplay-zoom.png)、[选择回应](Art/Screenshots/v2/gameplay-response.png)、[返回探索](Art/Screenshots/v2/gameplay-return.png) 和 [继续移动](Art/Screenshots/v2/gameplay-move.png)。该项由助手通过原生窗口输入完成，用户负责解除锁屏。

自动测试覆盖跨档隔离、重复请求、失效回应、来源与动作校验、备份恢复、中文分块传输和错误恢复。它们不证明真实模型不会编造，也不证明当前预写样例已实现长期人格成长。

**第三版角色精修记录（2026-10-23）**

核心 21 项、接口 13 项、Unity PlayMode 4 项，共 38 项通过。新增运行测试检查挥手时手指变化、双脚固定、动作中断时手部连续性，以及点头结束后回到 Idle。`unity-art.log` 和 `unity-build.log` 均通过；187 个资源/目录 metadata 唯一。

真实玩家端分别完成多角度模型检视与 450 帧动作采集，日志为 `art-v3-model-final.log`、`art-v3-motion-final.log`，均无脚本异常。动作视频为 1600 × 1000、30 fps、15 秒，已完整解码核验；采集使用固定模拟步长，不作为运行帧率基准。使用独立目录 `TestResults/art-v3-saves`，未改变正常试玩存档。视频、截图与编辑入口见 [第三版说明](Art/character-v3.md)。
