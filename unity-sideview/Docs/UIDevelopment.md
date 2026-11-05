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

# Phase 2 · Shared UI refactor and conversation handoff

The old bottom conversation implementation has been removed from GameUI. `DialogueUIController.Initialize(GameSession, RectTransform)` now exclusively owns all conversation presentation and input. GameUI reads `GameSession.Conversation` only to hide exploration HUD / prompts and reject map, phone, journal, pause and Escape handling until the conversation has fully exited. Ending presentation waits for the conversation exit transition.

Shared components:

- `UIStyleConfig`: public ScriptableObject tokens for translucent navy surfaces, cool blue / warm orange accents, typography, border radii and unscaled animation durations. An optional `Resources/UI/DefaultUIStyle` asset overrides the runtime defaults; the Create Asset menu can produce this asset.
- `RoundedPanel`: UGUI Image subclass with rounded fill and a one-pixel interior border; existing Panel return signatures remain compatible.
- `UIHoverFeedback`: restrained smooth color, border and 1.2% scale feedback, using unscaled time. No external tweening package added.
- `InteractionPromptUI`: independently styled E badge and action caption, soft appearance and world tracking. NPC prompts always read “交谈”; environment verbs retain their own labels.
- `UIPrimitives` is public and keeps its existing construction API so the dialogue UI can use the same system.

Map, phone, journal, save, restart confirmation, sound volume and ending recall remain functional paths in the refactored source. Modal panels fade in/out; closing keeps the overlay lock until fade completion. Map travel and restart execute only after closing completes. `CloseOverlayImmediately()` is available for an external scene transition that must cancel an in-flight UI animation. The scene lock is preserved when closing while WorldDirector is transitioning.

Visual direction uses the user-provided scene/UI images and the calm, scene-first presentation described on the [official Chill with You Steam page](https://store.steampowered.com/app/3548580/?l=english). No Steam assets were copied. Screenshot URLs were available from the official store API, but the image browsing tool could not open them during this pass; this document does not claim a fresh screenshot-level comparison.

This pass changed UI source only, with no Core / World / Data / Live2D edits, test scripts, test-suite runs or Unity compile RPC. Integration compilation and runtime visual review are pending the root agent's coordinated pass.
