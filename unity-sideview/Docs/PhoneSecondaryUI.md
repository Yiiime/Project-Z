# 手机二级页接入 · 2026-11-10

本轮把已确认的手机示意稿接入现有 Demo，范围为消息、任务、相册、人物及各自详情。保持原地图、直播、移动、对话与场景系统。没有把设计稿的示例任务进度、日期或回忆写进游戏。

## 预览

Unity 6000.3.27f1 打开 `unity-sideview`，运行 `Assets/Seabreeze/Scenes/Street.unity`，按 Tab 打开手机。

- 消息 → 一封来信：只有这封会话标为已读；列表支持全部/未读。
- 任务 → 一件事：按真实状态分进行中/待回应/已完成，步骤只读，依赖来自原任务目录。关注至多一件，显示到手机首页。接受、拒绝与完成仍在既有游戏流程中进行。
- 相册：全部/照片/回忆/收藏；文字经历与真实照片明确区分，回忆可收藏、查看相关人物。
- 人物：只列已相识的人；关于 TA / 一起经历，已知信息带来源，可进入相关任务和回忆。
- 返回箭头或 Esc：回到实际来源页，恢复筛选、标签与滚动位置。Tab/关闭按钮/点击场景收起，重开回首页。筛选在当前会话中保留；已读、关注、收藏写入存档。

新存档的任务、人物和相册不会用样例补满。走近 NPC 按 E 正式会面后，会自动留下相识和首次交谈记录；历史玩家直接交谈也可从合法 checkpoint 回填。委托状态需要现有记忆服务同步。

## 文件职责

| 文件 | 职责 |
|---|---|
| `Scripts/UI/GameUI.Phone.cs` | 现有首页、同一玻璃外壳内的导航/动画、返回栈、滚动恢复、共享控件 |
| `Scripts/UI/GameUI.PhonePages.cs` | 四列表与详情、任务步骤、人物关联、收藏、可用照片的大图入口 |
| `Scripts/UI/PhoneDataService.cs` | 玩家可知信息投影、稳定消息 ID、checkpoint 解析、低频增量观察与持久操作 |
| `Scripts/Core/PhoneSaveState.cs` | 玩家自己的手机偏好、相识与经历记录 |
| `Scripts/Core/SaveData.cs` | `phone` 字段及旧存档空值兼容 |
| `Scripts/UI/GameUI.cs` | 初始化与销毁钩子、Esc 返回和关闭清理；其余功能不改 |

界面继续使用原 Final Kit 图标、TMP 字体、RoundedPanel、UIHoverFeedback 与 PhoneSceneGlass。内部翻页只重建内容，不反复截取毛玻璃背景。动态中文段落按字体实际行高计算，避免 TMP 的省略字段高度不足导致首行消失。原有字体和模型导入产生的变化不纳入代码交付。

## 数据边界

- 消息复用 `Story.PhoneMessages` 的实际系统来信。`private_player_chat` 是面对面交流的隐私通道，不是短信；`records.message` 不是收件箱。
- 委托只读实际 `offered/active/completed/declined` 状态；目录初始化的 `available` 不展示。`progress` 是 JSON 对象字符串，成功步骤与依赖分别读取。并行步骤不强制排序。
- 人物不读取 NPC 私有 info、记忆、关系数值或后台决策；旁听别人交谈不等于相识。
- 相册收录真实相识、玩家可见且已核实的剧情见闻，以及已完成委托。完成委托缺权威日期/地点时显示未知，不使用读取时刻冒充完成时刻。
- 已存在的合影任务回执没有照片文件产出链路，因此仅形成文字记录。当前投影不会生成任何假照片；正式照片入库还需由未来真实拍照/奖励链路提供可信图片路径与元数据。
- 空数据、尚未同步与损坏记录分别提示；断线时显示上次已同步委托记录。新存档或 worldId 变化隔离手机状态，普通重连的 epoch 增长不清空偏好。
- 0.5 秒检查数据变化；只有投影内容实际改变才通知 UI，按住鼠标操作期间延迟重建。

## 验证证据

使用现有 Unity CLI 和即时 `script eval` 在同一项目 Play Mode 检查；没有新增测试脚本、测试工程，也没有运行测试套件。

| 检查 | 结果/证据类别 |
|---|---|
| Unity 类型/API 编译 | recompile 成功，0 errors/0 warnings；`EditorUtility.scriptCompilationFailed=false` |
| 消息已读 | 初始 2 条，进入列表仍 2；打开一封后 1；SaveRepository 读回 1 个已读 ID |
| 真实会面入口 | 隔离存档中调用原 `Story.Interact` 会面小小晴，现有对话打开；新增 1 人与 1 条首次交谈记录并持久化。未请求模型生成回复 |
| 关注与收藏 | 按钮回调写入原 SaveRepository；关注不改变 checkpoint；重入 Play Mode 后关注保留 |
| Esc/Tab | 通过现有 Input System 的按键事件检查：回忆→人物“一起经历”→任务恢复滚动 y=90；Tab 关闭恢复 timeScale=1 与移动输入，重开回首页 |
| 串行/并行步骤 | 隔离 checkpoint 夹具：map_blank 的 visit 完成后仅 explain 可做；bar_box active 时 3 项均可做 |
| 隐私/未解锁 | 夹具中的 available map_trial 未展示；不向玩家可见的 NPC 私聊未解锁人物 |
| 损坏记录 | 非法 JSON 显示读取提示，保留已存相识；不冒充无任务 |
| 新存档隔离 | 替换 SaveData 后人物/回忆/收藏为 0、关注为空、旧手机关闭；恢复原隔离状态可恢复偏好 |
| 地图/直播 | 点击原首页入口进入 map / live；live 创建原 NativeLiveHost。未测试发起直播或服务端生成 |
| 视觉 | 查看了实际 GameView 截图的任务、人物、回忆、消息详情和照片空态；已修复首行高度问题 |
| 控制台 | 最终当前 Editor Console errors=0；历史 CLI 日志包含早期开发时错误，不当作最终运行结果 |

验收数据只放在 Unity `Temp/PhoneSecondaryReview`，真实存档未混入夹具。结束后退出 Play Mode，恢复原存档路径。下列截图为**隔离夹具的实际 Unity 画面**，用于检查 UI 布局，不代表用户已经接到或完成这些委托：

- [任务列表](Verification/PhoneSecondaryUI/tasks-fixture.png)
- [任务详情](Verification/PhoneSecondaryUI/task-detail-fixture.png)
- [人物档案](Verification/PhoneSecondaryUI/person-fixture.png)
- [回忆详情](Verification/PhoneSecondaryUI/memory-fixture.png)

当前尚未覆盖真实模型生成一份委托再完成整条任务链、真实照片自动入库，以及新的独立安装包。地图/直播功能未重做。

## 交付状态

手机二级页提交至草稿 PR #48。提交前在保留 Git LFS 指针的独立检出中运行仓库检查，避免本机已下载的模型被工作区文件大小检查误判。

当前编译无错误；控制台仍有既有直播代码 `LiveFeatureController.voiceNotice` 未使用字段警告（CS0414），本轮未扩大修改范围处理它。验收期间产生的字体 atlas 与模型 importer 差异已独立保留在本地 stash，没有混入四页 UI 的改动。
