# 剧情任务本地试玩

双击工程根目录的 **启动剧情任务试玩.command**。首次启动会开启独立本机服务与游戏窗口，使用 `Builds/StoryPlaytest/Data/saves` 的独立存档。

## 这版怎么玩

| 操作 | 结果 |
|---|---|
| A / D 移动，走近岛民按 E | 开始真实模型对话 |
| 至少一轮玩家发言与 NPC 回应后结束交谈 | 本 NPC 剧情分 +2；同一时段最多一次 |
| 交流后出现委托，或对话时点击「请 TA 提个委托」 | 模型结合该 NPC 人设和可见记忆生成日常委托，并现场绘制物体 |
| 等待资产就绪，接受委托；走近物体按 E，再到目标容器按 E | 完成整项任务，委托人 +10 分，全岛推进一个时段 |
| 第四个任务完成 | 夜间固定直播样片自动开始；结束后进入次日上午 |
| 某人达到 25 / 50 / 75 分 | 播放该角色下一段剧情样片 |
| 某人先达到 100 分 | 播放该角色结局，锁定本局路线 |

多人进度独立，可以穿插体验。拒绝委托、准备图片、走路、读字和播放剧情片不消耗时段。阶段影片按顺序播放；同一刻到结局与日终时优先结局。影片支持跳过，跳过视为已观看；回到存档不会重复加分。

## 当前内容边界

- 真实 NPC 对话与委托由项目已有方舟文本模型生成。
- 当前默认资产模式为 **AI 简绘**：文本模型现场输出受限几何绘图数据，程序生成透明 PNG；任务物体的类型与外观随生成结果变化。
- 已保留 Seedream 图像模型接口，但当前账号直接调用返回 `InvalidEndpointOrModel.ModelIDAccessDisabled`，所以本版本没有把 Seedream 当作已接通能力。获得相应权限后可使用 `Z_STORY_ASSET_MODE=seedream` 测试。
- 八个 NPC 各三段故事与一个结局，共 32 段；另有一段日终直播。它们是用现有场景美术、字幕和镜头运动制作的 **预制分镜样片**，用于评估节奏，不是最终人物演出。正式影片可按相同 ID 替换。
- 这版直播使用固定流程样片；原生直播功能仍保留在正常游戏模式中。
- 本地生成服务依赖当前配置对应的网络。账号凭证只存在被 Git 忽略的 `.local/model.json`，不进入应用包和提交。

## 配置与重建

| 文件 / 位置 | 用途 |
|---|---|
| `Assets/Seabreeze/Resources/StoryPlaytest/settings.json` | 任务分、聊天分、三个阶段阈值、终点值 |
| `Assets/Seabreeze/Resources/StoryPlaytest/stories.json` | 剧情样片文案与稳定 ID |
| `Tools/story-playtest/build-media.py --output <绝对目录>` | 使用已有场景图与 ffmpeg 重建 MP4 样片 |
| Unity 菜单 `Seabreeze / Story playtest / Build local player` | 构建 macOS 试玩；命令行可传 `-storyMediaSource <影片目录>` |
| `Builds/StoryPlaytest/Data` | 独立存档、模型服务数据库、生成图片与日志 |

构建使用 Unity 6000.3.27f1，依赖项目既有的 RTC 本机构建产物。Node.js 需要 22.13 或更新版本。启动器优先使用本机已安装的 Node 24 运行时。

## 验证

- `node --test tests/*.test.mjs`（在 `Tools/MemoryLab`）：原有协议与新增委托/绘图数据测试。
- Unity 菜单 `Verify progression rules`：任务与聊天计分、同段上限、回执去重、存档恢复、影片顺序和结局锁定。
- `-storySmokeOutput <目录> -storySmokeFixture <JSON>`：独立玩家中的实际场景、拾取/投放、四任务过夜、视频解码、存档与结局验证。必须额外设置独立且包含 `story-smoke` 的 `Z_MEMORY_SAVE_ROOT`。

自动化测试使用标记清楚的重复任务夹具验证结算边界；真实模型生成链路和可见游戏操作另行检查。
