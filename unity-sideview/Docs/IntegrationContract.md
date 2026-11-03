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
