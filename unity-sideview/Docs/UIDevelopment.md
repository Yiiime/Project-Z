# 当前界面：PR32 外观与十人 AI 对话

2026-11-06：以 PR32 提交 `70be1c7` 的布局和样式为基准，移植 UIStyleConfig、RoundedPanel、UIHoverFeedback 与 InteractionPromptUI。当前功能由已有记忆／移动／直播模块驱动，不引入 PR32 的剧情书签、8 帧主角或全局对话物理锁。

| 界面 | 接入方式 |
| --- | --- |
| 玩家对话 | MemoryDialogueView 使用左人物区、右对话与输入；Esc 有反向退出动画。关闭动画期间只继续锁玩家输入。 |
| 立绘 | 仅 z_story 绑定 PR32 Z 模型；无立绘时隐藏人物区，同一输入流程。按文字显示驱动嘴部变化。 |
| NPC 互聊 | 使用说话者头顶气泡，跟随角色、自动停留与分段；显示画面内 NPC 的发言，不限玩家距离，不打开全屏界面。 |
| 菜单 | 地图、手机、日志与暂停渐入渐出；关闭完成后执行旅行或重开；WorldDirector 可立即取消菜单过渡。 |
| 直播 | 保留现有 NativeLiveHost 与 ZLive 功能，live.uss 适配深色半透明外观。 |
| 输入 | TMP_InputField 与文本组件统一纯文本；IME 组合文本未提交时不发送。 |

当前验收与限制见 [MemoryIntegration.md](MemoryIntegration.md)。以下保留旧阶段的实施记录，不覆盖上面的当前规则。

---

# Phase 4 · UI implementation

Implemented under `Assets/Seabreeze/Scripts/UI` as an independent UGUI / TextMeshPro module. GameBootstrap calls GameUI.Initialize after GameSession is ready.

- Reference-inspired translucent navy HUD: date / time / weather / place top-left, thin-line map / phone / journal shortcuts right, atmospheric descriptions and controls below.
- A world-positioned E prompt follows the current interactable. World owns selecting the closest target; UI owns screen conversion and visibility.
- Dialogue supports typed text, reveal / continue, mouse and E / Space / Enter, three numbered keyboard choices. Opening frame and line frame guards prevent the interaction E from consuming the first line.
- Map has three locations, current-location marker and working travel. Overlay closes before travel begins. Journal and phone use masked, mouse-scrollable content. Journal shows inventory, newest memory first and ending recall.
- Pause supports resume, save, audio volume and a second-step restart confirmation. Restart clears UI ending state. New endings appear after the final conversation closes; exploration can continue afterward.
- HUD hides with H. M / Tab / J toggle panels; Escape closes panels or opens pause. Overlay states are exclusive and block player control through GameSession.SetOverlay.
- Chinese TMP font comes from `Resources/Fonts/SeabreezeChinese`. UI symbols use code-native line graphics, independent of font glyph coverage.

Validation pending root integration: Unity compilation and runtime checks, Chinese font atlas coverage, scene-transition prompt positions, visual screenshot review. No claims of runtime acceptance until these are checked. Layout targets 1920 × 1080 via CanvasScaler; 16:9 is the intended first-stage presentation.

## Integration corrections

- Actual compiler feedback: explicitly qualified `UIPrimitives.Font` to disambiguate `UnityEngine.Font`.
- HUD now keeps the day, period and short numeric clock / weather on separate lines without repeating the period or overflowing into the location.
- Actual runtime observation from root: line icons were missing. Checked the installed UGUI `Graphic.cs`: it requires only `RectTransform`; added an explicit CanvasRenderer requirement to the custom LineIcon.
- Pointer and keyboard actions, including dialogue advance, are gated during scene transitions. Public CloseOverlay remains callable by WorldDirector.
- All modal overlays pause scaled simulation time. Closing or destroying the UI restores normal simulation time; UI typewriter, notifications and scene fades use unscaled time.
- Interaction prompts resize for long building-entry labels. Notifications render above modal overlays so save confirmation remains visible.

Final compile and visual checks remain owned by the integration pass. No separate UI test project was created.

## Dialogue visual refinement

After inspecting `Docs/Verification/street-integration.png`, moved conversation text to 24 reference pixels above the screen bottom and sized the panel to its actual text. Ordinary two-line passages use roughly 182–220 pixels instead of a fixed 267-pixel box placed above the exploration footer. The continue action shares the speaker row, and choice rows grow upward only when present. During dialogue only the exploration ambience / controls footer hides; the date / place HUD and right-side shortcuts remain. The footer returns when the dialogue closes. This avoids covering the characters' torsos with ordinary conversations.

## Final functional review fixes

- Manual save now displays success only when `GameSession.Save()` returns true. The existing failed-write notification remains visible instead of being overwritten by an unconditional success message. Other save callers may ignore the new boolean return value.
- Ending recall resolves the completed ending's authored `StoryEffect.ending` / `journal` pair, then restores its saved journal date prefix when available. Exploring additional objects after the ending no longer replaces the recalled ending text with the newest unrelated observation. No save-schema changes were introduced.

2026-11-07：E 进入后自动请求 NPC 开场，PR32 对话区显示真实模型结果与等待状态。失败时提供重试开场，保留输入草稿。NPCSpeechBubble 只负责实际已发言的显示，不写记忆、不抢焦点；跳跃提示同步移除。
