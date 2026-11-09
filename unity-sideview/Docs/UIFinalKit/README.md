# UI Kit 终稿接入（2026-11-09）

基于本地 `task/seanlee111/island-map-travel-1107` 地图分支，未同步或替换新版地图资源。
设计来源：Figma `GQsDhP4EXFWLJYimH44zQ9`，页面 `437:2`（UI Kit · 终稿）；手机 `437:21`，HUD `473:4948`，对话 `484:7145`。SVG 原件和来源清单保存在 ArtSource/UIFinalKit；Unity 使用本地透明 PNG，不依赖 Figma URL。

## 实现

- 行动态：左上日期/时段/天气/当前地图名称、底部氛围文字、右下 Tab 手机入口；去除旧地图/日志竖排快捷入口及技术状态面板。M、J、Esc 快捷键保留。
- E 提示：原交互距离和淡入机制不变，改用紫灰胶囊、独立 E 键帽和气泡尾。
- 手机：384×664 玻璃外壳、Bodoni Moda 时间、一体环境/任务窗、六宫格。
- 消息读取现有 PhoneMessages；未读标记仅本次运行有效。任务读取原 Objective/journal，人物读取现有角色名册。相册为明确的未开放空状态，不伪造照片或拍照能力。
- 地图入口复用旧地图页，保留 PR #37 并行边界；直播直接打开既有 NativeLiveHost，未改直播内部 UI 或服务。
- 当前 Memory 模式对话：真实 Live2D、进入时场景快照柔化、独立左右聊天气泡、逐字/点击补全、滚动历史、声音/关闭、文字输入和 Enter 发送。最多显示12条历史；会话记录仍由原系统管理。
- 普通 NPC 语音输入尚无后端，本轮不把静态麦克风当成功能。设计稿中的咖啡邀约未硬编码到剧情。
- 原非 Memory 模式的脚本剧情对话框、暂停、地图、日志二级页保留既有实现。暂停页补回原来的对话服务设置和读档入口。

## 验证与边界

Unity 6000.3.27f1 实际编译与 Play Mode 检查；不新增测试脚本/测试工程。
截图 action.png、phone.png 为当前地图中的运行画面。dialogue.png 使用实际 Z 会话入口和真实 Live2D，三条文案只注入视图做排版/逐字检查，没有写入故事或后端记录。背景与天气保持当前场景，不替换成设计稿雨景。
在线对话服务当前未连接，因此本轮未验证真实 AI 回复、语音或在线直播服务；直播入口/关闭容器生命周期单独验证。

### 验证记录

- Unity recompile：0 errors / 0 warnings。
- Play Mode：手机首页与消息页运行截图；消息角标读取后消失；实际 Z BeginPlayer 入口、真实模型和逐字气泡显示；结束按钮解除 PlayerChatOpen；手机外侧关闭恢复 HasOverlay=false / timeScale=1。
- 直播按钮实际创建 NativeLiveHost（数量1），关闭恢复游戏容器；在线直播内容未验收。
- UI检查通过运行中按钮回调和状态查询完成。系统原生桌面工具连接超时，CLI 模拟 Tab 未触发界面，故不将物理键盘 Tab/Esc、鼠标原生点击及中文输入法的人工试玩宣称为已验证；既有输入分支保留。
- 全仓保存检查阻塞：已有 `Assets/Seabreeze/Live2D/Models/Z/Model.asset` 约60 MB，`git check-attr filter` 确认为 lfs，但团队 project-check 按工作区展开大小判断并拒绝保存。未修改模型/检查器绕过规则。

## 对话背景毛玻璃修正（2026-11-09）

仅调整 DialogueSceneBackdrop，并新增独立 FrostedScene.shader；手机保留原 SceneBlur。
原先 1/4 尺寸 + 拉大步距的稀疏采样容易保留粗边和重影。改为 1/2 尺寸，显式 Bilinear/Clamp，连续17点归一化高斯核（sigma4），3轮横纵模糊；按输出高度调节半径。支持时使用 ARGBHalf，回退 ARGB32；不另加 gamma 校正。每次进入只捕获和处理一次，退出释放。

验证：Unity C#编译0错误0警告，Shader supported=true且无编译消息。Play Mode实际背景RT为960×540/ARGBHalf（1920×1080输出）；观察窗框、屋檐、树叶边缘已连续平滑，Live2D和UI保持清晰。关闭后 PlayerChatOpen=false、命名背景RT数量0。新版实拍 dialogue.png，修改前保留 dialogue-before-frost.png（两次拍摄机位略有差异）。
