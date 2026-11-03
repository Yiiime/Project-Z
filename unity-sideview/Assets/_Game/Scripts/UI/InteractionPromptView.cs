using TMPro;
using UnityEngine;
using ZFromMoon.Interaction;
using ZFromMoon.Dialogue;

namespace ZFromMoon.UI
{
    /// <summary>Shows the current world target and a brief response without changing game state.</summary>
    [DisallowMultipleComponent]
    public sealed class InteractionPromptView : MonoBehaviour
    {
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform promptRoot;
        [SerializeField] private TMP_Text promptLabel;
        [SerializeField] private GameObject feedbackRoot;
        [SerializeField] private TMP_Text feedbackLabel;
        [SerializeField, Min(0.1f)] private float feedbackDuration = 3f;

        private RectTransform canvasRect;
        private Interactable displayedTarget;
        private float feedbackUntil;
        private bool configured;
        private bool subscribed;

        private void Awake()
        {
            if (interactor == null || worldCamera == null || canvas == null
                || promptRoot == null || promptLabel == null
                || feedbackRoot == null || feedbackLabel == null)
            {
                Debug.LogError("InteractionPromptView requires its interactor, camera, canvas and UI references.", this);
                enabled = false;
                return;
            }

            canvasRect = canvas.transform as RectTransform;
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay || canvasRect == null
                || promptRoot.parent != canvas.transform || feedbackRoot.transform.parent != canvas.transform
                || transform.IsChildOf(promptRoot) || transform.IsChildOf(feedbackRoot.transform))
            {
                Debug.LogError("InteractionPromptView requires an overlay Canvas with direct-child panels, and must stay outside both hidden panels.", this);
                enabled = false;
                return;
            }

            configured = true;
            HideAll();
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
            if (configured) HideAll();
        }

        private void LateUpdate()
        {
            if (!configured) return;
            if (interactor == null || !interactor.CanInteractNow)
            {
                HideAll();
                return;
            }

            if (feedbackRoot.activeSelf && Time.unscaledTime >= feedbackUntil)
                HideFeedback();

            Interactable target = interactor.CurrentTarget;
            if (target == null || !target.isActiveAndEnabled)
            {
                HidePrompt();
                return;
            }

            Vector3 screenPoint = worldCamera.WorldToScreenPoint(target.PromptAnchor.position);
            if (screenPoint.z <= 0f || !worldCamera.pixelRect.Contains(new Vector2(screenPoint.x, screenPoint.y))
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out Vector2 localPoint))
            {
                HidePrompt();
                return;
            }

            if (displayedTarget != target)
            {
                displayedTarget = target;
                promptLabel.text = "E  " + target.ActionLabel;
            }

            // Local position is relative to the Canvas pivot, independent of panel anchor settings.
            promptRoot.localPosition = new Vector3(localPoint.x, localPoint.y, 0f);
            if (!promptRoot.gameObject.activeSelf) promptRoot.gameObject.SetActive(true);
        }

        private void OnInteractionRequested(Interactable target)
        {
            // Dialogue actors have their own modal response; props keep their brief exploration text.
            if (target != null && target.TryGetComponent<DialogueActor>(out var actor)
                && actor.isActiveAndEnabled && actor.Definition != null)
                return;
            if (!configured || !interactor.CanInteractNow || target == null
                || string.IsNullOrWhiteSpace(target.ResponseText))
            {
                if (configured) HideFeedback();
                return;
            }

            feedbackLabel.text = string.IsNullOrWhiteSpace(target.DisplayName)
                ? target.ResponseText
                : target.DisplayName + "：" + target.ResponseText;
            feedbackUntil = Time.unscaledTime + feedbackDuration;
            feedbackRoot.SetActive(true);
        }

        private void HidePrompt()
        {
            displayedTarget = null;
            if (promptRoot.gameObject.activeSelf) promptRoot.gameObject.SetActive(false);
        }

        private void HideFeedback()
        {
            feedbackUntil = 0f;
            if (feedbackRoot.activeSelf) feedbackRoot.SetActive(false);
        }

        private void HideAll()
        {
            HidePrompt();
            HideFeedback();
        }
    }
}
