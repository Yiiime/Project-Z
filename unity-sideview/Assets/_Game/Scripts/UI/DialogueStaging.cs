using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZFromMoon.Dialogue;
using ZFromMoon.Player;
using ZFromMoon.World;

namespace ZFromMoon.UI
{
    /// <summary>
    /// Cinematic staging for a conversation:
    /// camera push-in, world dim, actor fade-out, portrait slide-in and panel rise (reverse on exit).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DialogueStaging : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] private float pushInOrthoSize = 4.85f;
        [SerializeField] private float enterDuration = 0.55f;
        [SerializeField] private float exitDuration = 0.38f;

        [Header("World")]
        [SerializeField, Range(0f, 1f)] private float dimAlpha = 0.55f;
        [SerializeField, Range(0f, 1f)] private float actorFadeAlpha = 0.06f;
        [SerializeField] private Color dimColor = new Color(0.02f, 0.03f, 0.09f, 1f);

        [Header("Panel")]
        [SerializeField] private float portraitSlide = 170f;
        [SerializeField] private float panelRise = 55f;

        private DialogueController controller;
        private CameraFollow cameraFollow;
        private RectTransform panel;
        private RectTransform stage;
        private RectTransform conversation;
        private CanvasGroup stageGroup;
        private CanvasGroup conversationGroup;
        private Image dim;
        private Coroutine routine;
        private Vector2 stageBase;
        private Vector2 conversationBase;
        private Transform playerRoot;

        private readonly List<SpriteRenderer> worldSprites = new List<SpriteRenderer>();
        private readonly List<Color> worldColors = new List<Color>();
        private bool ready;

        private void Start()
        {
            controller = FindFirstObjectByType<DialogueController>();
            cameraFollow = FindFirstObjectByType<CameraFollow>();
            if (controller == null)
            {
                Debug.LogError("DialogueStaging requires a DialogueController in the scene.", this);
                enabled = false;
                return;
            }

            Transform canvas = transform;
            panel = FindDeep(canvas, "DialoguePanel");
            if (panel == null)
            {
                Debug.LogError("DialogueStaging cannot find DialoguePanel.", this);
                enabled = false;
                return;
            }

            stage = FindDeep(panel, "CharacterStage");
            conversation = FindDeep(panel, "Conversation");
            if (stage != null)
            {
                stageBase = stage.anchoredPosition;
                stageGroup = stage.GetComponent<CanvasGroup>();
                if (stageGroup == null) stageGroup = stage.gameObject.AddComponent<CanvasGroup>();
                stageGroup.alpha = 0f;
            }
            if (conversation != null)
            {
                conversationBase = conversation.anchoredPosition;
                conversationGroup = conversation.GetComponent<CanvasGroup>();
                if (conversationGroup == null) conversationGroup = conversation.gameObject.AddComponent<CanvasGroup>();
                conversationGroup.alpha = 0f;
            }

            CreateDim(panel);
            var motor = FindFirstObjectByType<PlayerMotor>();
            if (motor != null) playerRoot = motor.transform;

            controller.Opened += OnOpened;
            controller.Closed += OnClosed;
            ready = true;
        }

        private void OnDestroy()
        {
            if (!ready || controller == null) return;
            controller.Opened -= OnOpened;
            controller.Closed -= OnClosed;
        }

        private void OnOpened(DialogueActor actor)
        {
            if (!ready) return;
            CacheWorld(actor);
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(Enter(actor));
        }

        private void OnClosed()
        {
            if (!ready) return;
            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(Exit());
        }

        private IEnumerator Enter(DialogueActor actor)
        {
            if (cameraFollow != null && actor != null)
                cameraFollow.FocusOn(actor.transform.position.x, pushInOrthoSize, enterDuration * 0.9f);

            SetDim(0f);
            if (stageGroup != null) stageGroup.alpha = 0f;
            if (stage != null) stage.anchoredPosition = stageBase + new Vector2(-portraitSlide, 0f);
            if (conversationGroup != null) conversationGroup.alpha = 0f;
            if (conversation != null) conversation.anchoredPosition = conversationBase + new Vector2(0f, -panelRise);
            SetWorldAlpha(1f);

            float time = 0f;
            while (time < 1f)
            {
                time += Time.unscaledDeltaTime / Mathf.Max(0.01f, enterDuration);
                float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time));

                SetDim(Mathf.Lerp(0f, dimAlpha, eased));
                if (stage != null) stage.anchoredPosition = stageBase + new Vector2(Mathf.Lerp(-portraitSlide, 0f, eased), 0f);
                if (stageGroup != null) stageGroup.alpha = eased;
                if (conversation != null) conversation.anchoredPosition = conversationBase + new Vector2(0f, Mathf.Lerp(-panelRise, 0f, eased));
                if (conversationGroup != null) conversationGroup.alpha = eased;
                SetWorldAlpha(Mathf.Lerp(1f, actorFadeAlpha, eased));
                yield return null;
            }

            SetDim(dimAlpha);
            if (stageGroup != null) stageGroup.alpha = 1f;
            if (conversationGroup != null) conversationGroup.alpha = 1f;
            SetWorldAlpha(actorFadeAlpha);
            routine = null;
        }

        private IEnumerator Exit()
        {
            if (cameraFollow != null) cameraFollow.ReleaseFocus(exitDuration);

            float startDim = dim != null ? dim.color.a : dimAlpha;
            float startStage = stageGroup != null ? stageGroup.alpha : 1f;
            float startConversation = conversationGroup != null ? conversationGroup.alpha : 1f;
            float startWorld = worldSprites.Count > 0 ? worldSprites[0].color.a : 1f;

            float time = 0f;
            while (time < 1f)
            {
                time += Time.unscaledDeltaTime / Mathf.Max(0.01f, exitDuration);
                float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time));

                SetDim(Mathf.Lerp(startDim, 0f, eased));
                if (stageGroup != null) stageGroup.alpha = Mathf.Lerp(startStage, 0f, eased);
                if (conversationGroup != null) conversationGroup.alpha = Mathf.Lerp(startConversation, 0f, eased);
                SetWorldAlpha(Mathf.Lerp(startWorld, 1f, eased));
                yield return null;
            }

            SetDim(0f);
            SetWorldAlpha(1f);
            if (stage != null) stage.anchoredPosition = stageBase;
            if (conversation != null) conversation.anchoredPosition = conversationBase;
            worldSprites.Clear();
            worldColors.Clear();
            routine = null;
        }

        private void CreateDim(RectTransform reference)
        {
            var go = new GameObject("DialogueDim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(reference.parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.SetSiblingIndex(reference.GetSiblingIndex());

            dim = go.GetComponent<Image>();
            dim.color = new Color(dimColor.r, dimColor.g, dimColor.b, 0f);
            dim.raycastTarget = false;
        }

        private void SetDim(float alpha)
        {
            if (dim == null) return;
            dim.color = new Color(dimColor.r, dimColor.g, dimColor.b, alpha);
        }

        private void CacheWorld(DialogueActor actor)
        {
            worldSprites.Clear();
            worldColors.Clear();
            if (playerRoot != null) AddRenderers(playerRoot);
            if (actor != null) AddRenderers(actor.transform);
        }

        private void AddRenderers(Transform root)
        {
            foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer == null) continue;
                worldSprites.Add(renderer);
                worldColors.Add(renderer.color);
            }
        }

        private void SetWorldAlpha(float alpha)
        {
            for (int i = 0; i < worldSprites.Count; i++)
            {
                var renderer = worldSprites[i];
                if (renderer == null) continue;
                Color color = worldColors[i];
                color.a *= alpha;
                renderer.color = color;
            }
        }

        private static RectTransform FindDeep(Transform root, string name)
        {
            foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
                if (rect.name == name) return rect;
            return null;
        }
    }
}
