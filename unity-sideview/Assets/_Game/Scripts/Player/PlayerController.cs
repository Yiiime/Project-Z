using UnityEngine;
using ZFromMoon.Core;

namespace ZFromMoon.Player
{
    /// <summary>Connects one player to its explicit App session, without global lookups.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        private PlayerMotor motor;
        private bool started;
        private bool bound;
        private bool hasSpawned;

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
        }

        private void Start()
        {
            started = true;
            Connect();
        }

        private void OnEnable()
        {
            // Initial binding waits for all components' Awake / OnEnable calls.
            if (started) Connect();
        }

        private void Connect()
        {
            motor.SetSimulationEnabled(false);
            if (session == null)
            {
                Debug.LogError("PlayerController requires an explicitly assigned GameSession.", this);
                enabled = false;
                return;
            }

            session.Ready += OnSessionReady;
            // Subscribe first: the initial location can already be loaded in the Editor.
            if (session.IsReady) OnSessionReady();
        }

        private void OnSessionReady()
        {
            if (bound || !session.IsReady) return;
            if (!hasSpawned)
            {
                motor.Teleport(session.Location.InitialSpawn.position);
                hasSpawned = true;
            }

            session.Input.MoveChanged += motor.SetHorizontal;
            session.Modes.Changed += ApplyMode;
            session.LocationChanged += OnLocationChanged;
            bound = true;
            ApplyMode(session.Modes.Current);
        }

        private void Update()
        {
            // Polling keeps movement alive when a held key's events were lost while the
            // input map was disabled during a location transition.
            if (!bound || session == null || !session.IsReady) return;
            if (session.Modes.Current == GameMode.Exploration)
                motor.SetHorizontal(session.Input.CurrentMoveValue);
        }

        private void OnLocationChanged(string entryId)
        {
            if (session == null || session.Location == null) return;
            if (session.Location.TryGetEntry(entryId, out Transform entry))
                motor.Teleport(entry.position);
        }

        private void ApplyMode(GameMode mode)
        {
            // Dialogue retains gravity; loading must wait for a valid destination floor.
            motor.SetSimulationEnabled(mode != GameMode.Transition);
            motor.SetMovementEnabled(mode == GameMode.Exploration);
            if (mode == GameMode.Exploration)
                motor.SetHorizontal(session.Input.Horizontal);
        }

        private void OnDisable()
        {
            if (!started) return;
            if (session != null)
            {
                session.Ready -= OnSessionReady;
                if (bound)
                {
                    session.Input.MoveChanged -= motor.SetHorizontal;
                    session.Modes.Changed -= ApplyMode;
                    session.LocationChanged -= OnLocationChanged;
                }
            }
            bound = false;
            motor.SetSimulationEnabled(false);
        }
    }
}
