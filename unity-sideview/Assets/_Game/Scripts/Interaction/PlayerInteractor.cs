using System;
using System.Collections.Generic;
using UnityEngine;
using ZFromMoon.Core;

namespace ZFromMoon.Interaction
{
    /// <summary>Selects one nearby target and publishes requests; owns no dialogue or story effects.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField, Min(0.1f)] private float interactionRadius = 1.6f;
        [SerializeField] private Vector2 originOffset = new Vector2(0f, 0.9f);
        [SerializeField] private LayerMask targetLayers;
        [SerializeField] private LayerMask blockingLayers;

        private readonly List<Collider2D> hits = new List<Collider2D>(8);
        private readonly List<RaycastHit2D> blockers = new List<RaycastHit2D>(4);
        private readonly HashSet<Interactable> visited = new HashSet<Interactable>();
        private bool started;
        private bool bound;

        public Interactable CurrentTarget { get; private set; }
        public bool CanInteractNow => isActiveAndEnabled && bound && session != null
            && session.IsReady && session.isActiveAndEnabled
            && session.Input.isActiveAndEnabled && session.Modes.Current == GameMode.Exploration;
        public event Action<Interactable> InteractionRequested;

        private void Start()
        {
            started = true;
            Connect();
        }

        private void OnEnable()
        {
            if (started) Connect();
        }

        private void Connect()
        {
            if (session == null || targetLayers.value == 0)
            {
                Debug.LogError("PlayerInteractor requires a GameSession and target layer mask.", this);
                enabled = false;
                return;
            }
            session.Ready += OnSessionReady;
            if (session.IsReady) OnSessionReady();
        }

        private void OnSessionReady()
        {
            if (bound || !session.IsReady) return;
            session.Input.InteractRequested += OnInteractRequested;
            session.Modes.Changed += OnModeChanged;
            bound = true;
        }

        private void Update()
        {
            CurrentTarget = SelectTarget();
        }

        private Interactable SelectTarget()
        {
            if (!CanInteractNow) return null;
            Vector2 origin = (Vector2)transform.position + originOffset;
            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(targetLayers);
            hits.Clear();
            visited.Clear();
            Physics2D.OverlapCircle(origin, interactionRadius, filter, hits);

            var obstructionFilter = new ContactFilter2D { useTriggers = false };
            obstructionFilter.SetLayerMask(blockingLayers);
            Interactable best = null;
            float bestDistance = float.PositiveInfinity;
            var state = session.State.Snapshot;
            foreach (Collider2D hit in hits)
            {
                if (hit == null || !hit.isTrigger) continue;
                var candidate = hit.GetComponentInParent<Interactable>();
                if (candidate == null || !visited.Add(candidate)
                    || candidate.gameObject.scene != session.Location.gameObject.scene
                    || !candidate.CanInteract(state)) continue;

                Vector2 point = candidate.InteractionPoint.position;
                float distance = (point - origin).sqrMagnitude;
                if (distance > interactionRadius * interactionRadius) continue;
                if (blockingLayers.value != 0)
                {
                    blockers.Clear();
                    if (Physics2D.Linecast(origin, point, obstructionFilter, blockers) > 0) continue;
                }

                bool better = best == null || candidate.Priority > best.Priority;
                if (best != null && candidate.Priority == best.Priority)
                {
                    int distanceOrder = distance.CompareTo(bestDistance);
                    better = distanceOrder < 0;
                    if (distanceOrder == 0)
                    {
                        int order = string.CompareOrdinal(candidate.InteractionId, best.InteractionId);
                        better = order < 0 || (order == 0 && candidate.GetEntityId().CompareTo(best.GetEntityId()) < 0);
                    }
                }
                if (!better) continue;
                best = candidate;
                bestDistance = distance;
            }
            return best;
        }

        private void OnInteractRequested()
        {
            // Recheck on the input event; the last frame's target may have moved or become unavailable.
            CurrentTarget = SelectTarget();
            if (CurrentTarget != null)
                InteractionRequested?.Invoke(CurrentTarget);
        }

        private void OnModeChanged(GameMode mode)
        {
            if (mode != GameMode.Exploration) CurrentTarget = null;
        }

        private void OnDisable()
        {
            if (started && session != null)
            {
                session.Ready -= OnSessionReady;
                if (bound)
                {
                    session.Input.InteractRequested -= OnInteractRequested;
                    session.Modes.Changed -= OnModeChanged;
                }
            }
            bound = false;
            CurrentTarget = null;
            hits.Clear();
            blockers.Clear();
            visited.Clear();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.65f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position + (Vector3)originOffset, interactionRadius);
        }
    }
}
