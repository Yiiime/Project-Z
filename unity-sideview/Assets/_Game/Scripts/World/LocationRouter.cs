using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using ZFromMoon.Core;
using ZFromMoon.Interaction;

namespace ZFromMoon.World
{
    /// <summary>Owns the fade and input lock around an explicit exit interaction.</summary>
    [DisallowMultipleComponent]
    public sealed class LocationRouter : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private CameraFollow cameraFollow;
        [SerializeField, Min(0f)] private float fadeDuration = 0.28f;

        private Image fade;
        private bool configured;
        private bool subscribed;
        private bool traveling;

        public bool IsTraveling => traveling;

        private void Awake()
        {
            if (session == null || interactor == null || cameraFollow == null)
            {
                Debug.LogError("LocationRouter requires its session, interactor and camera references.", this);
                enabled = false;
                return;
            }

            CreateFade();
            configured = true;
        }

        private void OnEnable()
        {
            if (!configured || subscribed) return;
            interactor.InteractionRequested += OnInteractionRequested;
            subscribed = true;
        }

        private void OnDisable()
        {
            if (subscribed && interactor != null)
                interactor.InteractionRequested -= OnInteractionRequested;
            subscribed = false;
            // The session hosts an accepted trip, so disabling this view cannot interrupt a scene load.
        }

        private void OnInteractionRequested(Interactable target)
        {
            if (!(target is SceneExit exit) || !configured || !isActiveAndEnabled || traveling
                || session == null || !session.isActiveAndEnabled || !session.IsReady
                || session.IsTraveling || session.Modes.Current != GameMode.Exploration
                || interactor == null || !interactor.CanInteractNow || interactor.CurrentTarget != target
                || cameraFollow == null || !cameraFollow.isActiveAndEnabled
                || exit == null || exit.gameObject.scene != session.Location.gameObject.scene
                || !exit.CanInteract(session.State.Snapshot))
                return;

            string scenePath = exit.TargetScenePath;
            string entryId = exit.TargetEntryId;
            traveling = true;
            try
            {
                // Lock synchronously before another listener or input event can start a second trip.
                session.Modes.SetMode(GameMode.Transition);
                session.StartCoroutine(Travel(scenePath, entryId));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                FinishTravel();
            }
        }

        private IEnumerator Travel(string scenePath, string entryId)
        {
            try
            {
                yield return FadeTo(1f);
                if (session == null || !session.isActiveAndEnabled || !session.IsReady)
                    yield break;

                bool completed = false;
                bool succeeded = false;
                // GameSession reports completion from finally, including rejected or failed trips.
                // A separate session coroutine also lets this fade recover if the transaction throws.
                session.StartCoroutine(session.TravelRoutine(scenePath, entryId, result =>
                {
                    succeeded = result;
                    completed = true;
                }));
                while (!completed) yield return null;

                if (succeeded && cameraFollow != null && cameraFollow.isActiveAndEnabled)
                    cameraFollow.SnapNow();
                yield return FadeTo(0f);
            }
            finally
            {
                FinishTravel();
            }
        }

        private void FinishTravel()
        {
            SetFadeAlpha(0f);
            traveling = false;
            if (session == null || !session.IsReady
                || session.IsTraveling || session.Modes.Current != GameMode.Transition)
                return;

            try
            {
                session.CompleteTravelMode();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, session);
            }
        }

        private void CreateFade()
        {
            var root = new GameObject("LocationFade", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            var overlay = new GameObject("Overlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)overlay.transform;
            rect.SetParent(root.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            fade = overlay.GetComponent<Image>();
            fade.color = new Color(0.02f, 0.02f, 0.04f, 0f);
            fade.raycastTarget = false;
        }

        private IEnumerator FadeTo(float alpha)
        {
            if (this == null || fade == null || !gameObject.activeInHierarchy) yield break;
            float from = fade.color.a;
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                if (this == null || fade == null || !gameObject.activeInHierarchy) yield break;
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.001f, fadeDuration));
                SetFadeAlpha(Mathf.Lerp(from, alpha, Mathf.SmoothStep(0f, 1f, progress)));
                yield return null;
            }
            SetFadeAlpha(alpha);
        }

        private void SetFadeAlpha(float alpha)
        {
            if (fade == null) return;
            Color color = fade.color;
            color.a = alpha;
            fade.color = color;
        }
    }
}
