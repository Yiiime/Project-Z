# Seabreeze integration contract
Namespace: Seabreeze. Unity 6000.3.27f1, URP 17, Input System, UGUI/TMP.
Root owns World/, Editor/ProjectBuilder.cs, GameBootstrap.cs and project config.
Core agent owns Core/ except GameBootstrap, Data/, Editor/StoryContentBuilder.cs.
UI agent owns UI/. Art agent owns Resources/Art and ArtSource.

## Shared public API (implement exactly)
- enum TimePeriod { Morning, Afternoon, Evening, Night }
- enum PlaceId { Street, Cafe, Lighthouse }
- GameSession : MonoBehaviour. static Instance. public SaveData State; public GameClock Clock; public DialogueRunner Dialogue; public StoryDirector Story; public event Action Changed; public bool InputBlocked => Dialogue.IsOpen || UI blocking (UI will call SetOverlay(bool)); public void SetOverlay(bool); public void NotifyChanged(); public void Save(); public void NewGame(); public void SetFlag(string key); public bool HasFlag(string key); public event Action<PlaceId> TravelRequested; public void Travel(PlaceId place); public PlaceId CurrentPlace; public void SetPlace(PlaceId place); public bool IsReady;
- SaveData serializable fields: int day=1; int period=0; string weather; List<string> flags; List<string> journal; List<string> items; string ending; string place="Street"; float playerX=-8; bool started; List<string> relationships optional.
- GameClock : MonoBehaviour. public TimePeriod Period; public int Day; public string TimeLabel; public string WeatherLabel; public void Advance(); public event Action Changed. No realtime ticking; explicit advance after rest/conversation. Night can rest -> next day (story still finishable).
- DialogueRunner : MonoBehaviour. bool IsOpen; string Speaker; string Text; IReadOnlyList<DialogueChoice> Choices; event Action Changed; event Action Closed; void StartDialogue(string id); void Advance(); void Choose(int index). DialogueChoice fields string text, next; effects owned core. If no choices Advance next; end event core.
- StoryDirector : MonoBehaviour. public string Objective; public void Interact(string id); public string PromptFor(string id); public bool IsAvailable(string id); public event Action<string> Toast; public void Notify(string message); public IReadOnlyList<string> PhoneMessages; public void Evaluate(); id mappings below.
- WorldDirector : MonoBehaviour. static Instance; Transform Player; void Travel(PlaceId); void Respawn(); place and spawn persisted; startup builds runtime geometry from scene templates if needed.
- GameUI : MonoBehaviour. public void Initialize(); public bool HasOverlay; public void CloseOverlay(); UI reads session. Root adds component after session initialization. UI owns input for M map, Tab phone, J journal, Escape pause, H HUD, dialogue E/Space/Enter and 1/2/3 choices. World only E interact when !InputBlocked.
- GameInput static class (root): public static float Horizontal; public static bool JumpPressed; public static bool InteractPressed; public static bool Pressed(Key key); using UnityEngine.InputSystem.

## Content ids and places
Street: npc_mio (young photographer, morning/afternoon, evening moves lighthouse), npc_chen (retired postman, morning/afternoon); postcard (starting clue), noticeboard, bicycle, sea, bench (time).
Cafe: npc_aya (barista all periods); radio, guestbook, coffee (time), cafe_exit (world-owned).
Lighthouse: npc_mio (evening/night), letterbox (resolution), telescope, lighthouse_bench (time).
Story: postcard -> npc_aya identifies old photo -> npc_chen tells history -> choose at least 3 approaches (deliver / keep / invite); then evening lighthouse with Mio -> letterbox concludes, distinct ending/journal. Make order forgiving, fallback/recovery no softlocks.
Core implements own coherent branching plot using ids; it may change plot but tell root.
Map should display three nodes with connections, current location; buttons may call GameSession.Travel for discovered/current reachable places (all 3 allowed). World physically has Street left cafe door and right lighthouse exit; Cafe left street exit; Lighthouse left street exit.
Fonts: root supplies TMP font at Resources/Fonts/SeabreezeChinese.asset. UI use Resources.Load<TMP_FontAsset>("Fonts/SeabreezeChinese") fallback TMP_Settings.defaultFontAsset.
Art backgrounds names: street.png, cafe.png, lighthouse.png (no people or baked UI). Dimensions freely inspect. World maps each single composition to 32 world units width, height determined aspect, floor baseline ≈ bottom 16.5% image; camera ortho 5.3, y floor+3.2; can adapt per art. Separate independent foreground/parallax effects root.
Character art root implements modular painted sprite puppets unless art agent provides character sprites. Ground y=0; character visual height=2.5.

## NPC destination execution V1

`NpcPresence` attaches `NpcMovementController` after building its existing visual. Authored scene files and NPC schedules are unchanged. Optional `movementDestinations` are relative to the NPC's spawn; empty arrays provide home and nearby left/right points. The controller resolves them once and filters candidates against ground, obstacles, other NPCs and scene bounds.

| API | Contract |
| --- | --- |
| `NpcPresence.Movement`, `Visual`, `IsPresent` | Runtime movement, existing artwork and authored schedule availability. Hidden NPCs have no active movement or physical body. |
| `movement.BeginDecision()` | Unity main thread; returns null while unavailable, talking, transitioning or paused. Otherwise returns `request_id`, `actor_id`, `location_id`, current position, copied destinations and `available_actions`. |
| `movement.AcceptDecision(requestId, outputJson, out reason)` | Main thread; consumes `action` and checks `info.actor_id` in the team's `dialogue/action/info` output. Supported actions are `move` and `wait`, with the exact offered action ID. The host retains request IDs; the model does not invent them. |
| `movement.MovementFinished` / `LastResult` | Request, actor, target, actual position, outcome and reason. Outcomes: `Arrived`, `Waiting`, `Interrupted`, `Blocked`. A receipt callback may submit a newer command. |
| `movement.SimulateDecisions` | Defaults to true for local demonstration through the same JSON acceptance path. Set false before attaching a real model service. |
| `movement.Interrupt(reason)` | Stops physical motion, cancels pending responses and emits a receipt for an active move. |

Arrival is checked against physical position, not elapsed animation time. Dialogue, time changes, loss of schedule availability, disable and scene travel invalidate old actions. Overlays pause an active route and discard pending responses; the route accelerates again when the overlay closes. Mutable flags and state replacement are checked independently of notifications. Movement does not mutate story facts, relationships, inventory or game time.

Player/NPC pass-through follows the existing exploration behavior; NPC bodies cannot seal the horizontal route. Environment and peer-NPC obstructions stop movement with a truthful result. One-way benches retain their original ground-level pass-through behavior. `CharacterVisual` receives actual velocity; existing NPC art remains single-frame with prototype motion unless role-specific walk frames are supplied. This is same-scene movement, not an offscreen scheduler or cross-scene NPC navigation/persistence system.

Verification: `Seabreeze/Verify NPC Movement` or `-executeMethod Seabreeze.Editor.NpcMovementVerification.Run`. Optional arguments: `-npc-save-root <absolute temporary directory>`, `-npcReport <json path>`, `-npcCaptureDir <directory>`. The editor-only `SaveRepository.VerificationDirectory` is established before Play mode and cleared after exit; standalone/default save paths are unchanged. The NPC suite writes its report and screenshots outside tracked content and does not invoke the original story walkthrough's save-reset workflow on a player's normal save.

## Ten-islander memory mode

`Resources/Memory/integration.json` enables the mode and supplies a local service URL without credentials. `-memoryDisabled` preserves the original story mode. Islander-mode saves use `seven-memory-save.json`; legacy saves keep their original name. Explicit QA save roots are isolated.

| Owner | Contract |
| --- | --- |
| `MemoryGameController` | Bridges game time, Day4, actual observations and utterances. Holds no model credentials. |
| `MemoryLabClient` | Nonblocking local HTTP, durable event outbox, checkpoint/revision acceptance and per-load run identity. A stale response cannot replace a later checkpoint. |
| `SevenNpcSpawner` | Runtime assembly in existing scenes; keeps independent character IDs, presence and saved positions. Original scenes/prefabs are not rebuilt. |
| `NpcConversationCoordinator` | One bounded session, participant reservations, approach/face/speak/end; separate generation per speaker. Player intervention or context loss cancels unspoken results. |
| `NpcMovementController` | `SetInteractionHold`, `CanReachConversationPoint` and `MoveForConversation` use engine-owned targets and existing collision checks. Only the participants receive holds. |
| Memory Lab service | Public directory and private knowledge projection, source-attributed extraction/reflection, game-validated events, idempotent receipts and snapshot restoration. |

`NonPlayerInputBlocked` retains existing global menus/legacy-dialogue semantics. An islander-mode player text box only adds player input blocking; it does not freeze unrelated NPC movement. Day4 is persisted independently of scene presence and model availability.

API and model setup: `Tools/MemoryLab/README.md`. Full playtest guide: `Docs/MemoryIntegration.md`. Runtime tests distinguish real and simulated providers; gameplay does not silently switch to a fixed-response fallback.

### PR32 presentation and persona revision 2

The active roster is c01–c07, z_story, npc_aya, npc_chen. `SevenCharacterCatalog` / `SevenNpcSpawner` retain their historical class names. `SevenCharacter.portrait` is an optional Resources path to `Live2DModelProfile`; only Z is currently assigned. Schedules are applied to the three retained islanders without rebuilding scenes.

`MemoryDialogueView.PlayerInputReady` gates free-text submission during presentation transitions; `Closing` keeps player input blocked until the reverse transition ends. This never adds a global NPC pause. `GameUI.IsOverlayTransitioning` and `CloseOverlayImmediately()` handle menu animation and external scene transitions.

Day4 is public for all ten; retired staff/AI-identity lore is not in any persona. Checkpoints and new utterance events carry `personaRevision: 2`. Old snapshots are archived locally and migrated before restoration. The existing save filename, event IDs and sequence receipts remain compatible. The live-room backend and player locomotion are unchanged.
