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
        void Awake()
        {
            Instance = this;
        }

        public void Initialize()
        {
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
            if (initial && wasStarted && savedPlace != scene.place.ToString())
                return;
            Stage = scene;
            Interactables.Clear();
            Interactables.AddRange(scene.GetComponentsInChildren<Interactable>(true));
            Follow.GetComponent<Camera>().orthographicSize = scene.cameraSize;
            Follow.baseY = scene.cameraY;
            Follow.minX = -scene.width / 2;
            Follow.maxX = scene.width / 2;
            float x = scene.streetSpawn;
            if (initial && wasStarted && savedPlace == scene.place.ToString())
                x = Mathf.Clamp(savedX, -scene.width / 2 + 1, scene.width / 2 - 1);
            else if (scene.place == PlaceId.Street)
                x = previous == PlaceId.Cafe ? scene.fromCafe : previous == PlaceId.Lighthouse ? scene.fromLighthouse : scene.streetSpawn;
            initial = false;
            Player.GetComponent<PlayerController>().Teleport(new Vector2(x, .1f));
            Follow.Snap();
            GameSession.Instance.State.playerX = x;
            GameSession.Instance.SetPlace(scene.place);
            GameSession.Instance.State.started = true;
            GameSession.Instance.Story.Evaluate();
        }

        public void Travel(PlaceId place)
        {
            if (IsTransitioning)
                return;
            if (GameUI.Instance != null)
                GameUI.Instance.CloseOverlayImmediately();
            StartCoroutine(Transition(place));
        }

        IEnumerator Transition(PlaceId place)
        {
            IsTransitioning = true;
            GameSession.Instance.SetOverlay(true);
            fade.blocksRaycasts = true;
            previous = GameSession.Instance.CurrentPlace;
            for (float t = 0; t < .25f; t += Time.unscaledDeltaTime)
            {
                fade.alpha = t / .25f;
                yield return null;
            }

            fade.alpha = 1;
            var op = SceneManager.LoadSceneAsync(place.ToString());
            while (!op.isDone)
                yield return null;
            yield return null;
            for (float t = 0; t < .35f; t += Time.unscaledDeltaTime)
            {
                fade.alpha = 1 - t / .35f;
                yield return null;
            }

            fade.alpha = 0;
            fade.blocksRaycasts = false;
            GameSession.Instance.SetOverlay(GameUI.Instance != null && GameUI.Instance.HasOverlay);
            IsTransitioning = false;
            SavePosition();
        }

        public void Respawn()
        {
            if (Stage != null)
                Player.GetComponent<PlayerController>().Teleport(new Vector2(Stage.streetSpawn, .2f));
        }

        void Update()
        {
            if (Player != null && Stage != null && GameSession.Instance != null && GameSession.Instance.IsReady)
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
            if (GameSession.Instance != null && Player != null)
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
            if (GameSession.Instance != null)
                GameSession.Instance.TravelRequested -= Travel;
            if (Instance == this)
                Instance = null;
        }
    }
}
