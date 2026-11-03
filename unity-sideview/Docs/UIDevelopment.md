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
