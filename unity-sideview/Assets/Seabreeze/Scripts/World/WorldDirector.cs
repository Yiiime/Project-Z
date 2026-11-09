using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Seabreeze
{
    public class WorldDirector : MonoBehaviour
    {
        public static WorldDirector Instance { get; private set; }

        public Transform Player { get; private set; }

        public readonly List<Interactable> Interactables = new List<Interactable>();
        public WorldScene Stage { get; private set; }

        public bool IsTransitioning { get; private set; }

        public FollowCamera Follow { get; private set; }

        PlaceId previous;
        bool initial = true;
        CanvasGroup fade;
        float lastSave;
        bool requestHasArrival;
        PlaceId requestedPlace;
        string requestedArrival, pendingArrival;
        Rigidbody2D transitionBody;
        bool restoreSimulation;
        SaveData attachedState;
        bool restoreRequested;
        float requestedRestoreX;
        void Awake()
        {
            Instance = this;
        }

        public void Initialize()
        {
            // Removed/unknown maps from older saves must not strand bootstrap between scenes.
            var saved = GameSession.Instance.State;
            if (!System.Enum.TryParse(saved.place, out PlaceId savedPlace) ||
                !System.Enum.IsDefined(typeof(PlaceId), savedPlace) ||
                !Application.CanStreamedLevelBeLoaded(savedPlace.ToString()))
            {
                saved.place = PlaceId.Street.ToString();
                saved.playerX = -8;
            }
            var g = Resources.Load<GameObject>("Prefabs/Player");
            GameObject player = g != null ? Instantiate(g, transform) : new GameObject("Player");
            if (g == null)
            {
                player.transform.SetParent(transform);
                player.AddComponent<Rigidbody2D>();
                player.AddComponent<CapsuleCollider2D>();
                var vis = new GameObject("Visual");
                vis.transform.SetParent(player.transform);
                vis.AddComponent<CharacterVisual>().Build(0);
                player.AddComponent<PlayerController>();
                player.AddComponent<PlayerInteraction>();
            }

            Player = player.transform;
            var cam = new GameObject("Narrative camera");
            cam.transform.SetParent(transform);
            var c = cam.AddComponent<Camera>();
            c.orthographic = true;
            c.orthographicSize = 6.5f;
            c.backgroundColor = new Color(.06f, .12f, .23f);
            c.clearFlags = CameraClearFlags.SolidColor;
            c.tag = "MainCamera";
            cam.AddComponent<AudioListener>();
            Follow = cam.AddComponent<FollowCamera>();
            Follow.target = Player;
            Follow.baseY = 4.25f;
            var cg = new GameObject("Scene dissolve", typeof(Canvas), typeof(CanvasGroup));
            cg.transform.SetParent(transform);
            cg.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            cg.GetComponent<Canvas>().sortingOrder = 200;
            fade = cg.GetComponent<CanvasGroup>();
            fade.alpha = 0;
            fade.blocksRaycasts = false;
            var panel = new GameObject("Ink", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(cg.transform, false);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(.025f, .055f, .11f);
            GameSession.Instance.TravelRequested += Travel;
        }

        public void Attach(WorldScene scene)
        {
            var saved = GameSession.Instance.State;
            string savedPlace = saved.place;
            float savedX = saved.playerX;
            bool wasStarted = saved.started;
            if (initial && !IsTransitioning && wasStarted && savedPlace != scene.place.ToString())
                return;
            Stage = scene;
            Interactables.Clear();
            Interactables.AddRange(scene.GetComponentsInChildren<Interactable>(true));
            Follow.GetComponent<Camera>().orthographicSize = scene.cameraSize;
            Follow.baseY = scene.cameraY;
            Follow.minX = -scene.width / 2;
            Follow.maxX = scene.width / 2;
            Vector2 spawn = new Vector2(scene.streetSpawn, .1f);
            IslandArrival arrival = null;
            if (!string.IsNullOrEmpty(pendingArrival))
            {
                foreach (var marker in scene.GetComponentsInChildren<IslandArrival>(true))
                    if (marker.id == pendingArrival) { arrival = marker; break; }
                if (!arrival)
                    Debug.LogWarning($"Arrival '{pendingArrival}' missing in {scene.place}; using default spawn.");
            }
            if (arrival)
                spawn = arrival.transform.position;
            else if (restoreRequested || (initial && wasStarted && savedPlace == scene.place.ToString()))
                // Runtime Load/NewGame replaces SaveData. Keep its requested position
                // separate from the old scene's physical player during the dissolve.
                spawn.x = Mathf.Clamp(restoreRequested ? requestedRestoreX : savedX,
                    -scene.width / 2 + 2, scene.width / 2 - 2);
            else if (scene.place == PlaceId.Street)
                spawn.x = previous == PlaceId.Cafe ? scene.fromCafe : previous == PlaceId.Lighthouse ? scene.fromLighthouse : scene.streetSpawn;
            pendingArrival = null;
            restoreRequested = false;
            attachedState = saved;
            initial = false;
            var body = Player.GetComponent<Rigidbody2D>();
            bool simulated = body.simulated;
            body.simulated = false;
            Player.GetComponent<PlayerController>().Teleport(spawn);
            body.simulated = simulated;
            Follow.Snap();
            GameSession.Instance.State.playerX = spawn.x;
            GameSession.Instance.SetPlace(scene.place);
            GameSession.Instance.State.started = true;
            GameSession.Instance.Story.Evaluate();
        }

        // World routes still dispatch GameSession.TravelRequested: NPC memory and
        // conversations capture/cancel from that existing event before the scene unloads.
        public bool TravelTo(PlaceId place, string arrivalId)
        {
            var session = GameSession.Instance;
            if (!session || !session.IsReady || session.InputBlocked || IsTransitioning ||
                !Application.CanStreamedLevelBeLoaded(place.ToString()))
                return false;
            requestedPlace = place;
            requestedArrival = arrivalId;
            requestHasArrival = true;
            try
            {
                session.Travel(place);
                return IsTransitioning;
            }
            finally
            {
                requestHasArrival = false;
                requestedArrival = null;
            }
        }

        public void Travel(PlaceId place)
        {
            if (IsTransitioning)
                return;
            pendingArrival = requestHasArrival && requestedPlace == place ? requestedArrival : null;
            requestHasArrival = false;
            if (!Application.CanStreamedLevelBeLoaded(place.ToString()))
            {
                Debug.LogWarning($"Map '{place}' is not in Build Settings; returning to Street.");
                place = PlaceId.Street;
                pendingArrival = null;
                if (initial)
                {
                    GameSession.Instance.State.place = place.ToString();
                    GameSession.Instance.State.playerX = -8;
                }
                if (!Application.CanStreamedLevelBeLoaded(place.ToString())) return;
            }
            var state = GameSession.Instance.State;
            restoreRequested = state != attachedState && state != null &&
                state.started && state.place == place.ToString();
            requestedRestoreX = restoreRequested ? state.playerX : 0;
            if (GameUI.Instance != null)
                GameUI.Instance.CloseOverlayImmediately();
            // Lock before the first coroutine yield, including the physics step that
            // detected a boundary. MoveAxis from the previous Update cannot drift us.
            IsTransitioning = true;
            GameSession.Instance.SetOverlay(true);
            transitionBody = Player.GetComponent<Rigidbody2D>();
            restoreSimulation = transitionBody.simulated;
            transitionBody.linearVelocity = Vector2.zero;
            transitionBody.simulated = false;
            StartCoroutine(Transition(place));
        }

        IEnumerator Transition(PlaceId place)
        {
            bool arrived = false;
            try
            {
                fade.blocksRaycasts = true;
                previous = GameSession.Instance.CurrentPlace;
                for (float t = 0; t < .25f; t += Time.unscaledDeltaTime)
                {
                    fade.alpha = t / .25f;
                    yield return null;
                }
                fade.alpha = 1;
                AsyncOperation op = BeginLoad(place);
                if (op == null) yield break;
                while (!op.isDone) yield return null;
                yield return null; // WorldScene.Start attaches its destination and arrival marker.
                arrived = Stage != null && Stage.place == place;
                if (!arrived) Debug.LogError($"Map '{place}' loaded without an attached WorldScene.");
                for (float t = 0; t < .35f; t += Time.unscaledDeltaTime)
                {
                    fade.alpha = 1 - t / .35f;
                    yield return null;
                }
            }
            finally
            {
                FinishTransition();
                if (arrived) SavePosition();
            }
        }

        static AsyncOperation BeginLoad(PlaceId place)
        {
            try { return SceneManager.LoadSceneAsync(place.ToString()); }
            catch (System.Exception error) { Debug.LogException(error); return null; }
        }

        void FinishTransition()
        {
            pendingArrival = null;
            restoreRequested = false;
            if (fade) { fade.alpha = 0; fade.blocksRaycasts = false; }
            if (transitionBody)
            {
                transitionBody.linearVelocity = Vector2.zero;
                transitionBody.simulated = restoreSimulation;
                transitionBody = null;
            }
            IsTransitioning = false;
            if (GameSession.Instance)
                GameSession.Instance.SetOverlay(GameUI.Instance != null && GameUI.Instance.HasOverlay);
        }

        public void Respawn()
        {
            if (Stage != null)
                Player.GetComponent<PlayerController>().Teleport(new Vector2(Stage.streetSpawn, .2f));
        }

        void Update()
        {
            if (!IsTransitioning && Player != null && Stage != null && GameSession.Instance != null &&
                GameSession.Instance.IsReady && GameSession.Instance.State == attachedState)
            {
                GameSession.Instance.State.playerX = Player.position.x;
                if (Time.unscaledTime - lastSave > 15)
                {
                    lastSave = Time.unscaledTime;
                    SavePosition();
                }
            }
        }

        void SavePosition()
        {
            if (!IsTransitioning && GameSession.Instance != null && Player != null &&
                GameSession.Instance.State == attachedState)
            {
                GameSession.Instance.State.playerX = Player.position.x;
                GameSession.Instance.Save();
            }
        }

        void OnApplicationQuit()
        {
            SavePosition();
        }

        void OnDestroy()
        {
            if (IsTransitioning) FinishTransition();
            if (GameSession.Instance != null)
                GameSession.Instance.TravelRequested -= Travel;
            if (Instance == this)
                Instance = null;
        }
    }
}
