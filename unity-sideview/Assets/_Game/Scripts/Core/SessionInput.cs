using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace ZFromMoon.Core
{
    /// <summary>Owns input-map activation. Gameplay never reads raw keyboard state.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInput))]
    public sealed class SessionInput : MonoBehaviour
    {
        [SerializeField] private PlayerInput playerInput;
        [SerializeField] private InputSystemUIInputModule uiInputModule;

        private GameModeController modes;
        private InputActionMap exploration;
        private InputActionMap ui;
        private InputAction move;
        private InputAction interact;
        private InputAction cancel;
        private bool listening;

        public float Horizontal { get; private set; }

        /// <summary>Live value straight from the action; survives a map re-enable that swallowed the press event.</summary>
        public float CurrentMoveValue => move != null && move.enabled ? move.ReadValue<float>() : Horizontal;
        public event Action<float> MoveChanged;
        public event Action InteractRequested;
        public event Action CancelRequested;

        public void Initialize(GameModeController controller)
        {
            if (modes != null)
                throw new InvalidOperationException("SessionInput is already initialized.");
            if (playerInput == null || playerInput.actions == null || uiInputModule == null)
                throw new InvalidOperationException("Assign PlayerInput, its actions and the UI input module.");

            modes = controller ?? throw new ArgumentNullException(nameof(controller));
            // PlayerInput owns its runtime action instance; do not use InputSystem.actions.
            exploration = playerInput.actions.FindActionMap("Exploration", true);
            ui = playerInput.actions.FindActionMap("UI", true);
            move = exploration.FindAction("Move", true);
            interact = exploration.FindAction("Interact", true);
            cancel = ui.FindAction("Cancel", true);
            if (isActiveAndEnabled)
                Listen();
        }

        private void OnEnable()
        {
            if (modes != null)
                Listen();
        }

        private void Listen()
        {
            if (listening) return;
            listening = true;
            modes.Changed += ApplyMode;
            move.performed += OnMove;
            move.canceled += OnMove;
            interact.performed += OnInteract;
            cancel.performed += OnCancel;
            ApplyMode(modes.Current);
        }

        private void OnDisable()
        {
            if (!listening) return;
            listening = false;
            modes.Changed -= ApplyMode;
            move.performed -= OnMove;
            move.canceled -= OnMove;
            interact.performed -= OnInteract;
            cancel.performed -= OnCancel;
            DisableMaps();
        }

        private void DisableMaps()
        {
            uiInputModule.enabled = false;
            playerInput.DeactivateInput();
            exploration.Disable();
            ui.Disable();
            SetHorizontal(0f);
        }

        private void ApplyMode(GameMode mode)
        {
            DisableMaps();
            if (mode == GameMode.Transition) return;

            playerInput.ActivateInput();
            playerInput.SwitchCurrentActionMap(mode == GameMode.Exploration ? "Exploration" : "UI");
            uiInputModule.enabled = true;
            // Pointer input remains available for future HUD buttons in exploration.
            ui.Enable();
            if (mode == GameMode.Exploration) SetHorizontal(move.ReadValue<float>());
        }

        private void OnMove(InputAction.CallbackContext context)
        {
            SetHorizontal(modes.Current == GameMode.Exploration ? context.ReadValue<float>() : 0f);
        }

        private void SetHorizontal(float value)
        {
            Horizontal = value;
            MoveChanged?.Invoke(value);
        }

        private void OnInteract(InputAction.CallbackContext context)
        {
            if (modes.Current == GameMode.Exploration)
                InteractRequested?.Invoke();
        }

        private void OnCancel(InputAction.CallbackContext context)
        {
            if (modes.Current == GameMode.Dialogue)
                CancelRequested?.Invoke();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) SetHorizontal(0f);
        }
    }
}
