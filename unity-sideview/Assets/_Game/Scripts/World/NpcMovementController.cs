using System;
using System.Collections.Generic;
using UnityEngine;
using ZFromMoon.Core;

namespace ZFromMoon.World
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
    public sealed class NpcMovementController : MonoBehaviour
    {
        private const float Skin = 0.025f;
        private const float ArrivalDistance = 0.045f;
        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int MovingParameter = Animator.StringToHash("IsMoving");

        [SerializeField] private string actorId;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Animator animator;
        [SerializeField] private Sprite idleSprite;
        [SerializeField] private NpcDestination[] destinations = Array.Empty<NpcDestination>();
        [SerializeField] private bool relativeToSpawn = true;
        [SerializeField, Min(0.1f)] private float maxSpeed = 1.8f;
        [SerializeField, Min(0.1f)] private float acceleration = 4.5f;
        [SerializeField, Min(0.1f)] private float deceleration = 6f;
        [SerializeField, Min(0.1f)] private float blockedTimeout = 2f;
        [SerializeField] private LayerMask groundLayers = 1 << 9;
        [SerializeField] private LayerMask obstacleLayers = ~0;
        [SerializeField] private bool simulateDecisions = true;
        [SerializeField, Min(0.1f)] private float simulatedWait = 2.5f;

        private readonly RaycastHit2D[] hits = new RaycastHit2D[32];
        private readonly Dictionary<string, NpcDestination> resolved = new Dictionary<string, NpcDestination>(StringComparer.Ordinal);
        private Rigidbody2D body;
        private CapsuleCollider2D capsule;
        private SpriteRenderer spriteRenderer;
        private GameSession session;
        private SceneContext location;
        private string pendingRequest;
        private GameState requestedState;
        private string activeRequest;
        private NpcDestination target;
        private float speed;
        private float blockedTime;
        private float movementTime;
        private float movementTimeout;
        private string blockingObject;
        private float nextDecisionTime;
        private int simulatedIndex;
        private bool initialized;
        private bool hasSpeed;
        private bool hasMoving;

        public string ActorId => actorId;
        public bool IsMoving => target != null;
        public float ActualSpeed { get; private set; }
        public int FacingSign { get; private set; } = 1;
        public NpcMoveResult LastResult { get; private set; }
        public bool SimulateDecisions { get => simulateDecisions; set => simulateDecisions = value; }
        public event Action<NpcMoveResult> MovementFinished;

        private bool CanAct => initialized && isActiveAndEnabled && session != null && session.isActiveAndEnabled
            && session.IsReady && !session.IsTraveling && session.Modes.Current == GameMode.Exploration
            && session.Location == location && Time.timeScale > 0f;

        public void Configure(string id, Transform visual, Animator controller, NpcDestination[] points, bool relative = true)
        {
            if (initialized) throw new InvalidOperationException("Configure NPC movement before Start.");
            actorId = id;
            visualRoot = visual;
            animator = controller;
            SpriteRenderer renderer = visual != null ? visual.GetComponent<SpriteRenderer>() : null;
            idleSprite = renderer != null ? renderer.sprite : null;
            destinations = points ?? Array.Empty<NpcDestination>();
            relativeToSpawn = relative;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            capsule = GetComponent<CapsuleCollider2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.useFullKinematicContacts = true;
            body.simulated = false;
        }

        private void Start()
        {
            session = FindFirstObjectByType<GameSession>();
            location = GetComponentInParent<SceneContext>();
            if (location == null)
            {
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                {
                    location = root.GetComponentInChildren<SceneContext>();
                    if (location != null) break;
                }
            }
            if (session == null || location == null || visualRoot == null || visualRoot == transform
                || !visualRoot.IsChildOf(transform) || string.IsNullOrWhiteSpace(actorId))
            {
                Debug.LogError("NPC movement requires an actor ID, child visual, GameSession and SceneContext.", this);
                enabled = false;
                return;
            }
            FacingSign = visualRoot.localScale.x < 0 ? -1 : 1;
            spriteRenderer = visualRoot.GetComponent<SpriteRenderer>();
            Vector2 origin = relativeToSpawn ? body.position : Vector2.zero;
            foreach (NpcDestination point in destinations)
            {
                if (point == null || string.IsNullOrWhiteSpace(point.id) || !Finite(point.position)
                    || resolved.ContainsKey(point.id))
                {
                    Debug.LogError("NPC destinations must have unique IDs and finite positions.", this);
                    enabled = false;
                    return;
                }
                resolved.Add(point.id, new NpcDestination(point.id, origin + point.position, point.facing));
            }
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                foreach (AnimatorControllerParameter parameter in animator.parameters)
                {
                    hasSpeed |= parameter.nameHash == SpeedParameter && parameter.type == AnimatorControllerParameterType.Float;
                    hasMoving |= parameter.nameHash == MovingParameter && parameter.type == AnimatorControllerParameterType.Bool;
                }
            }
            initialized = true;
            session.Modes.Changed += OnModeChanged;
            if (session.State != null) session.State.Changed += OnStateChanged;
            session.Ready += OnReady;
            OnReady();
        }

        private void OnReady()
        {
            if (!initialized || !isActiveAndEnabled || !session.IsReady) return;
            session.State.Changed -= OnStateChanged;
            session.State.Changed += OnStateChanged;
            body.simulated = session.Location == location;
            nextDecisionTime = Time.time + simulatedWait;
        }

        private void OnModeChanged(GameMode mode)
        {
            if (mode != GameMode.Exploration) Interrupt("interaction_mode_changed");
            else nextDecisionTime = Time.time + simulatedWait;
        }

        private void OnStateChanged(GameState state)
        {
            pendingRequest = null;
            requestedState = null;
            if (session.Location != location) Interrupt("location_changed");
        }

        public NpcMoveRequest BeginDecision()
        {
            if (!CanAct) return null;
            pendingRequest = Guid.NewGuid().ToString("N");
            requestedState = session.State.Snapshot;
            var actions = new List<NpcMoveCommand> {
                new NpcMoveCommand { type = "wait", action_id = "wait_here", description = "Wait here" }
            };
            foreach (NpcDestination point in resolved.Values)
            {
                if (IsSupportedTarget(point))
                    actions.Add(new NpcMoveCommand { type = "move", target = point.id,
                        action_id = "move_" + point.id, description = point.id });
            }
            return new NpcMoveRequest { request_id = pendingRequest, actor_id = actorId,
                location_id = location.LocationId, available_actions = actions.ToArray() };
        }

        public bool AcceptDecision(string requestId, string outputJson, out string reason)
        {
            reason = null;
            if (!CanAct || string.IsNullOrEmpty(requestId) || requestId != pendingRequest
                || requestedState != session.State.Snapshot)
                return Reject("stale_request", out reason);
            if (string.IsNullOrWhiteSpace(outputJson) || outputJson.Length > 32768)
                return Reject("invalid_json", out reason);
            NpcMoveOutput output;
            try { output = JsonUtility.FromJson<NpcMoveOutput>(outputJson); }
            catch (ArgumentException) { return Reject("invalid_json", out reason); }
            if (output == null || output.action == null || output.info == null || output.info.actor_id != actorId)
                return Reject("actor_or_action_missing", out reason);
            NpcMoveCommand command = output.action;
            NpcDestination next = null;
            if (command.type == "move")
            {
                if (string.IsNullOrEmpty(command.target) || !resolved.TryGetValue(command.target, out next)
                    || !IsSupportedTarget(next)) return Reject("unavailable_target", out reason);
                if (command.action_id != "move_" + next.id) return Reject("unavailable_action", out reason);
            }
            else if (command.type != "wait" || !string.IsNullOrEmpty(command.target) || command.action_id != "wait_here")
                return Reject("unavailable_action", out reason);

            pendingRequest = null;
            requestedState = null;
            NpcMoveResult interrupted = Finish(NpcMoveOutcome.Interrupted, "replaced", false, false);
            activeRequest = requestId;
            target = next;
            blockedTime = 0f;
            blockingObject = null;
            movementTime = 0f;
            movementTimeout = next == null ? 0f : 8f + Vector2.Distance(body.position, next.position) / Mathf.Max(0.1f, maxSpeed) * 3f;
            NpcMoveResult waiting = next == null ? Finish(NpcMoveOutcome.Waiting, "wait", true, false) : null;
            // Commit the transition before callbacks can submit another decision.
            Publish(interrupted);
            Publish(waiting);
            return true;
        }

        private static bool Reject(string error, out string reason) { reason = error; return false; }

        private bool IsSupportedTarget(NpcDestination point)
        {
            return Mathf.Abs(point.position.y - body.position.y) <= 0.3f
                && TryGround(point.position, out float floor) && Mathf.Abs(floor - point.position.y) <= 0.15f;
        }

        private bool TryGround(Vector2 feet, out float floor)
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(groundLayers);
            int count = Physics2D.Raycast(feet + Vector2.up * 0.25f, Vector2.down, filter, hits, 0.55f);
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider != capsule && hits[i].normal.y > 0.8f)
                {
                    floor = hits[i].point.y;
                    return true;
                }
            }
            floor = feet.y;
            return false;
        }

        private void FixedUpdate()
        {
            if (!CanAct) { ActualSpeed = 0f; return; }
            if (!body.simulated) body.simulated = true;
            float dt = Time.fixedDeltaTime;
            if (target == null)
            {
                ActualSpeed = 0f;
                if (simulateDecisions && pendingRequest == null && Time.time >= nextDecisionTime)
                    SimulateDecision();
                return;
            }
            float distance = target.position.x - body.position.x;
            if (Mathf.Abs(distance) <= ArrivalDistance)
            {
                if (target.facing != 0) FacingSign = target.facing < 0 ? -1 : 1;
                Finish(NpcMoveOutcome.Arrived, "arrived", true);
                return;
            }

            float remaining = Mathf.Max(0f, Mathf.Abs(distance) - ArrivalDistance * 0.5f);
            float desired = Mathf.Sign(distance) * Mathf.Min(maxSpeed, Mathf.Sqrt(2f * deceleration * remaining));
            float rate = Mathf.Sign(speed) != Mathf.Sign(desired) || Mathf.Abs(desired) < Mathf.Abs(speed) ? deceleration : acceleration;
            speed = Mathf.MoveTowards(speed, desired, rate * dt);
            float step = speed * dt;
            if (Mathf.Sign(step) == Mathf.Sign(distance)) step = Mathf.Sign(step) * Mathf.Min(Mathf.Abs(step), Mathf.Abs(distance));
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(obstacleLayers);
            Vector2 direction = step < 0f ? Vector2.left : Vector2.right;
            float travel = Mathf.Abs(step);
            int count = capsule.Cast(direction, filter, hits, travel + Skin);
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.attachedRigidbody == body || Vector2.Dot(hits[i].normal, direction) > -0.1f) continue;
                float clear = Mathf.Max(0f, hits[i].distance - Skin);
                if (clear < travel)
                {
                    blockingObject = hits[i].collider.name;
                    travel = clear;
                }
            }
            Vector2 nextPosition = body.position + direction * travel;
            if (!TryGround(nextPosition, out float floor) || Mathf.Abs(floor - body.position.y) > 0.15f)
            {
                blockingObject = "unsupported_ground";
                travel = 0f;
            }
            nextPosition = body.position + direction * travel;
            ActualSpeed = travel / dt;
            if (travel > 0.0001f)
            {
                nextPosition.y = floor;
                FacingSign = direction.x < 0f ? -1 : 1;
                body.MovePosition(nextPosition);
                blockedTime = 0f;
            }
            else
            {
                speed = 0f;
                blockedTime += dt;
            }
            movementTime += dt;
            if (blockedTime >= blockedTimeout || movementTime >= movementTimeout)
                Finish(NpcMoveOutcome.Blocked, blockedTime >= blockedTimeout ? "obstructed:" + blockingObject : "timed_out", true);
        }

        private void SimulateDecision()
        {
            NpcMoveRequest request = BeginDecision();
            if (request == null) return;
            var candidates = new List<NpcMoveCommand>();
            foreach (NpcMoveCommand action in request.available_actions)
                if (action.type == "move" && Mathf.Abs(resolved[action.target].position.x - body.position.x) > 0.3f) candidates.Add(action);
            NpcMoveCommand choice = candidates.Count == 0 ? request.available_actions[0] : candidates[simulatedIndex++ % candidates.Count];
            var output = new NpcMoveOutput { dialogue = "", action = choice, info = new NpcMoveOutput.ActorInfo { actor_id = actorId } };
            AcceptDecision(request.request_id, JsonUtility.ToJson(output), out _);
        }

        public void Interrupt(string reason = "cancelled")
        {
            pendingRequest = null;
            requestedState = null;
            Finish(NpcMoveOutcome.Interrupted, reason, true);
            speed = 0f;
            ActualSpeed = 0f;
            if (body != null) body.linearVelocity = Vector2.zero;
            nextDecisionTime = Time.time + simulatedWait;
        }

        private NpcMoveResult Finish(NpcMoveOutcome outcome, string reason, bool stop, bool publish = true)
        {
            string completed = activeRequest;
            string destination = target?.id;
            activeRequest = null;
            target = null;
            if (stop)
            {
                speed = 0f;
                ActualSpeed = 0f;
                if (body != null) body.linearVelocity = Vector2.zero;
            }
            nextDecisionTime = Time.time + simulatedWait;
            if (string.IsNullOrEmpty(completed)) return null;
            var result = new NpcMoveResult(completed, actorId, destination, outcome, reason, body.position);
            if (publish) Publish(result);
            return result;
        }

        private void Publish(NpcMoveResult result)
        {
            if (result == null) return;
            LastResult = result;
            if (MovementFinished == null) return;
            foreach (Action<NpcMoveResult> listener in MovementFinished.GetInvocationList())
            {
                try { listener(result); }
                catch (Exception exception) { Debug.LogException(exception, this); }
            }
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            Vector3 scale = visualRoot.localScale;
            scale.x = Mathf.Abs(scale.x) * FacingSign;
            visualRoot.localScale = scale;
            if (animator == null || !animator.isActiveAndEnabled) return;
            if (hasSpeed) animator.SetFloat(SpeedParameter, ActualSpeed);
            if (hasMoving) animator.SetBool(MovingParameter, ActualSpeed > 0.02f);
            animator.speed = ActualSpeed > 0.02f ? Mathf.Clamp(ActualSpeed / maxSpeed, 0.15f, 1.2f) : 1f;
            if (ActualSpeed <= 0.02f && idleSprite != null && spriteRenderer != null)
                spriteRenderer.sprite = idleSprite;
        }

        private void OnDisable()
        {
            Interrupt("disabled");
            if (body != null) body.simulated = false;
        }

        private void OnDestroy()
        {
            if (session == null) return;
            session.Modes.Changed -= OnModeChanged;
            session.Ready -= OnReady;
            if (session.State != null) session.State.Changed -= OnStateChanged;
        }

        private static bool Finite(Vector2 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    }
}
