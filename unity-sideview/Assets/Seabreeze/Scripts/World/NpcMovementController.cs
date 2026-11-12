using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Seabreeze
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
    public sealed class NpcMovementController : MonoBehaviour
    {
        const float Skin = .025f;
        const float ArrivalDistance = .045f;
        [SerializeField, Min(.1f)] float maxSpeed = 1.8f;
        [SerializeField, Min(.1f)] float acceleration = 4.5f;
        [SerializeField, Min(.1f)] float deceleration = 6f;
        [SerializeField, Min(.1f)] float blockedTimeout = 2f;
        [SerializeField] bool simulateDecisions = true;
        [SerializeField, Min(.1f)] float simulatedWait = 2.5f;

        readonly RaycastHit2D[] hits = new RaycastHit2D[32];
        readonly Dictionary<string, NpcDestination> resolved = new Dictionary<string, NpcDestination>(StringComparer.Ordinal);
        readonly HashSet<string> requestedFlags = new HashSet<string>(StringComparer.Ordinal);
        Rigidbody2D body;
        CapsuleCollider2D capsule;
        NpcPresence presence;
        CharacterVisual visual;
        NpcDestination[] authoredPoints;
        GameSession session;
        WorldDirector world;
        WorldScene stage;
        SaveData requestedState;
        int requestedDay, requestedPeriod;
        string requestedPlace, pendingRequest, activeRequest, blockingObject;
        NpcDestination target;
        float speed, blockedTime, movementTime, movementTimeout, nextDecisionTime;
        int simulatedIndex;
        bool initialized, available, wasBlocked = true;
        bool interactionHold;
        float previousVisualX;
        bool hasVisualPosition;

        public string ActorId => presence != null ? presence.npcId : "";
        public bool IsReady => initialized;
        public bool IsMoving => target != null;
        public float ActualSpeed { get; private set; }
        public int FacingSign { get; private set; } = 1;
        public bool SimulateDecisions { get => simulateDecisions; set => simulateDecisions = value; }
        public NpcMoveResult LastResult { get; private set; }
        public event Action<NpcMoveResult> MovementFinished;

        bool InCurrentWorld => initialized && isActiveAndEnabled && available && presence != null
            && presence.isActiveAndEnabled && session != null && session == GameSession.Instance && session.IsReady
            && world != null && world == WorldDirector.Instance && stage != null && world.Stage == stage
            && presence.location == session.CurrentPlace && gameObject.scene == stage.gameObject.scene;
        bool CanAct => InCurrentWorld && !session.NonPlayerInputBlocked && !interactionHold && !world.IsTransitioning && Time.timeScale > 0;
        public bool InteractionHeld => interactionHold;
        public void SetInteractionHold(bool value)
        {
            if (value && !interactionHold) Interrupt("conversation_hold");
            interactionHold = value;
            if (!value) resolved.Remove("conversation");
        }
        public bool CanReachConversationPoint(Vector2 point) => InCurrentWorld && Finite(point) && IsSupportedTarget(new NpcDestination("conversation", point));
        public bool MoveForConversation(Vector2 point, int facing)
        {
            if (!CanAct || !CanReachConversationPoint(point)) return false;
            resolved["conversation"] = new NpcDestination("conversation", point, facing);
            var request = BeginDecision();
            if (request == null) return false;
            var output = new NpcMoveOutput { dialogue = "", action = new NpcMoveCommand { type = "move", target = "conversation", action_id = "move_conversation" }, info = new NpcMoveOutput.ActorInfo { actor_id = ActorId } };
            return AcceptDecision(request.request_id, JsonUtility.ToJson(output), out _);
        }

        public void Configure(NpcPresence owner, CharacterVisual artwork, NpcDestination[] points)
        {
            if (initialized) throw new InvalidOperationException("Configure movement before initialization.");
            presence = owner;
            visual = artwork;
            if (artwork != null && artwork.NpcWalkSpeed > 0) maxSpeed = artwork.NpcWalkSpeed;
            authoredPoints = points;
        }

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            capsule = GetComponent<CapsuleCollider2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.useFullKinematicContacts = true;
            capsule.isTrigger = false;
            capsule.size = new Vector2(.6f, 2.65f);
            capsule.offset = new Vector2(0, 1.35f);
            body.simulated = false;
        }

        IEnumerator Start()
        {
            if (presence == null || visual == null || string.IsNullOrWhiteSpace(ActorId))
            {
                Debug.LogError("NPC movement requires its NpcPresence and CharacterVisual.", this);
                enabled = false;
                yield break;
            }
            while (GameSession.Instance == null || !GameSession.Instance.IsReady || WorldDirector.Instance == null
                || WorldDirector.Instance.Stage == null || WorldDirector.Instance.Stage.gameObject.scene != gameObject.scene)
                yield return null;
            session = GameSession.Instance;
            world = WorldDirector.Instance;
            stage = world.Stage;
            Vector2 origin = body.position;
            Physics2D.SyncTransforms();
            if (TryGround(origin, out float floor) && Mathf.Abs(floor - origin.y) < .15f)
            {
                origin.y = floor;
                body.position = origin;
            }
            if (authoredPoints == null || authoredPoints.Length == 0)
            {
                float left = Mathf.Clamp(origin.x - 1.5f, -stage.width / 2 + 1, stage.width / 2 - 1);
                float right = Mathf.Clamp(origin.x + 1.5f, -stage.width / 2 + 1, stage.width / 2 - 1);
                authoredPoints = new[] {
                    new NpcDestination("left", new Vector2(left - origin.x, 0)),
                    new NpcDestination("home", Vector2.zero),
                    new NpcDestination("right", new Vector2(right - origin.x, 0))
                };
            }
            foreach (NpcDestination point in authoredPoints)
            {
                if (point == null || string.IsNullOrWhiteSpace(point.id) || !Finite(point.position) || resolved.ContainsKey(point.id))
                {
                    Debug.LogError("NPC destinations require unique IDs and finite positions.", this);
                    enabled = false;
                    yield break;
                }
                resolved.Add(point.id, new NpcDestination(point.id, origin + point.position, point.facing));
            }
            // NPC markers were passable in Seabreeze. Keep the player's walking route open.
            if (world.Player != null)
                foreach (Collider2D playerCollider in world.Player.GetComponentsInChildren<Collider2D>())
                    Physics2D.IgnoreCollision(capsule, playerCollider, true);
            FacingSign = visual.transform.localScale.x < 0 ? -1 : 1;
            session.Changed += OnContextChanged;
            session.Clock.Changed += OnClockChanged;
            session.Dialogue.Changed += OnDialogueChanged;
            session.TravelRequested += OnTravelRequested;
            initialized = true;
            body.simulated = available;
            nextDecisionTime = Time.time + simulatedWait + presence.character * .3f;
        }

        public void SetAvailable(bool value)
        {
            available = value;
            if (!value) Interrupt("not_present");
            SetBodySimulated(value && initialized && isActiveAndEnabled);
        }

        void SetBodySimulated(bool value)
        {
            if (body == null) return;
            // Reassigning simulated=true can resync an interpolated body's pose.
            // Use state transitions for authored motion; legacy NPCs retain their path.
            if (visual == null || !visual.UsesNpcMotion || body.simulated != value) body.simulated = value;
        }

        void OnContextChanged()
        {
            InvalidateRequest();
            if (!InCurrentWorld) Interrupt("context_changed");
        }

        void OnClockChanged() => Interrupt("clock_changed");
        void OnTravelRequested(PlaceId destination) => Interrupt("location_changed");
        void OnDialogueChanged()
        {
            InvalidateRequest();
            if (session != null && session.Dialogue.IsOpen) Interrupt("dialogue");
        }

        void Update()
        {
            if (!initialized) return;
            if (!InCurrentWorld)
            {
                if (activeRequest != null || pendingRequest != null) Interrupt("not_present");
                SetBodySimulated(false);
            }
            else SetBodySimulated(true);
            bool blocked = !CanAct;
            if (blocked && !wasBlocked) Suspend();
            if (!blocked && wasBlocked) nextDecisionTime = Time.time + simulatedWait + presence.character * .3f;
            wasBlocked = blocked;
        }

        void Suspend()
        {
            InvalidateRequest();
            visual?.StopNpcMotionImmediately();
            speed = ActualSpeed = 0;
            if (body != null) body.linearVelocity = Vector2.zero;
        }

        void InvalidateRequest()
        {
            pendingRequest = null;
            requestedState = null;
            requestedFlags.Clear();
        }

        public NpcMoveRequest BeginDecision()
        {
            if (!CanAct) { Suspend(); return null; }
            pendingRequest = Guid.NewGuid().ToString("N");
            requestedState = session.State;
            requestedDay = requestedState.day;
            requestedPeriod = requestedState.period;
            requestedPlace = requestedState.place;
            requestedFlags.Clear();
            foreach (string flag in requestedState.flags) requestedFlags.Add(flag);
            var points = new List<NpcDestination>();
            var actions = new List<NpcMoveCommand> {
                new NpcMoveCommand { type = "wait", action_id = "wait_here", description = "Wait here" }
            };
            foreach (NpcDestination point in resolved.Values)
            {
                if (!IsSupportedTarget(point)) continue;
                points.Add(new NpcDestination(point.id, point.position, point.facing));
                actions.Add(new NpcMoveCommand { type = "move", target = point.id,
                    action_id = "move_" + point.id, description = point.id });
            }
            return new NpcMoveRequest { request_id = pendingRequest, actor_id = ActorId,
                location_id = stage.place.ToString(), position = body.position,
                available_actions = actions.ToArray(), destinations = points.ToArray() };
        }

        public bool AcceptDecision(string requestId, string outputJson, out string reason)
        {
            reason = null;
            if (!CanAct || string.IsNullOrEmpty(requestId) || requestId != pendingRequest
                || requestedState != session.State || requestedDay != session.State.day
                || requestedPeriod != session.State.period || requestedPlace != session.State.place
                || !requestedFlags.SetEquals(session.State.flags)) return Reject("stale_request", out reason);
            if (string.IsNullOrWhiteSpace(outputJson) || outputJson.Length > 32768) return Reject("invalid_json", out reason);
            NpcMoveOutput output;
            try { output = JsonUtility.FromJson<NpcMoveOutput>(outputJson); }
            catch (ArgumentException) { return Reject("invalid_json", out reason); }
            if (output == null || output.action == null || output.info == null || output.info.actor_id != ActorId)
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
            InvalidateRequest();
            NpcMoveResult interrupted = Finish(NpcMoveOutcome.Interrupted, "replaced", false, false);
            activeRequest = requestId;
            target = next;
            blockedTime = movementTime = 0;
            blockingObject = null;
            movementTimeout = next == null ? 0 : 8 + Vector2.Distance(body.position, next.position) / maxSpeed * 3;
            if (next != null && visual.UsesNpcStateMotion)
                visual.BeginNpcMotionRoute(Mathf.Abs(next.position.x - body.position.x));
            NpcMoveResult waiting = next == null ? Finish(NpcMoveOutcome.Waiting, "wait", true, false) : null;
            // State is committed before a receipt listener can submit a newer destination.
            Publish(interrupted);
            Publish(waiting);
            return true;
        }

        static bool Reject(string error, out string reason) { reason = error; return false; }

        bool IsSupportedTarget(NpcDestination point)
        {
            if (point.position.x < -stage.width / 2 + .5f || point.position.x > stage.width / 2 - .5f
                || Mathf.Abs(point.position.y - body.position.y) > .15f) return false;
            float dx = point.position.x - body.position.x;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(dx) / .25f));
            for (int i = 0; i <= steps; i++)
            {
                Vector2 feet = Vector2.Lerp(body.position, point.position, (float)i / steps);
                if (!TryGround(feet, out float floor) || Mathf.Abs(floor - feet.y) > .15f) return false;
            }
            if (Mathf.Abs(dx) <= ArrivalDistance) return true;
            Vector2 direction = dx < 0 ? Vector2.left : Vector2.right;
            int count = capsule.Cast(direction, ObstacleFilter(), hits, Mathf.Abs(dx) + Skin);
            for (int i = 0; i < count; i++)
                if (BlocksHorizontal(hits[i], direction) && hits[i].distance < Mathf.Abs(dx) + Skin) return false;
            return true;
        }

        static ContactFilter2D ObstacleFilter()
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(~0);
            return filter;
        }

        bool BlocksHorizontal(RaycastHit2D hit, Vector2 direction)
        {
            if (hit.collider.attachedRigidbody == body || Vector2.Dot(hit.normal, direction) > -.1f
                || hit.collider.GetComponentInParent<PlayerController>() != null) return false;
            var platform = hit.collider.GetComponent<PlatformEffector2D>();
            return !hit.collider.usedByEffector || platform == null || !platform.useOneWay;
        }

        bool TryGround(Vector2 feet, out float floor)
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(1 << 8);
            int count = Physics2D.Raycast(feet + Vector2.up * .25f, Vector2.down, filter, hits, .55f);
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider == capsule || hits[i].normal.y <= .8f) continue;
                var platform = hits[i].collider.GetComponent<PlatformEffector2D>();
                if (hits[i].collider.usedByEffector && platform != null && platform.useOneWay
                    && feet.y < hits[i].collider.bounds.max.y - .05f) continue;
                floor = hits[i].point.y;
                return true;
            }
            floor = feet.y;
            return false;
        }

        void FixedUpdate()
        {
            if (!CanAct)
            {
                speed = ActualSpeed = 0;
                body.linearVelocity = Vector2.zero;
                return;
            }
            float dt = Time.fixedDeltaTime;
            if (target == null)
            {
                ActualSpeed = 0;
                if (simulateDecisions && pendingRequest == null && Time.time >= nextDecisionTime) SimulateDecision();
                return;
            }
            float distance = target.position.x - body.position.x;
            if (Mathf.Abs(distance) <= ArrivalDistance)
            {
                if (target.facing != 0)
                {
                    FacingSign = target.facing < 0 ? -1 : 1;
                    Vector3 scale = visual.transform.localScale;
                    scale.x = Mathf.Abs(scale.x) * FacingSign;
                    visual.transform.localScale = scale;
                }
                Finish(NpcMoveOutcome.Arrived, "arrived", true);
                return;
            }
            float remaining = Mathf.Max(0, Mathf.Abs(distance) - ArrivalDistance * .5f);
            float desired = Mathf.Sign(distance) * Mathf.Min(maxSpeed, Mathf.Sqrt(2 * deceleration * remaining));
            if (visual.UsesNpcStateMotion)
            {
                if (!visual.HasNpcMotionRoute) visual.BeginNpcMotionRoute(Mathf.Abs(distance));
                desired *= visual.NpcRouteSpeedFactor;
            }
            float rate = Mathf.Sign(speed) != Mathf.Sign(desired) || Mathf.Abs(desired) < Mathf.Abs(speed) ? deceleration : acceleration;
            speed = Mathf.MoveTowards(speed, desired, rate * dt);
            float step = speed * dt;
            if (Mathf.Sign(step) == Mathf.Sign(distance)) step = Mathf.Sign(step) * Mathf.Min(Mathf.Abs(step), Mathf.Abs(distance));
            Vector2 direction = step < 0 ? Vector2.left : Vector2.right;
            float travel = Mathf.Abs(step);
            int count = capsule.Cast(direction, ObstacleFilter(), hits, travel + Skin);
            for (int i = 0; i < count; i++)
            {
                if (!BlocksHorizontal(hits[i], direction)) continue;
                float clear = Mathf.Max(0, hits[i].distance - Skin);
                if (clear < travel) { blockingObject = hits[i].collider.name; travel = clear; }
            }
            Vector2 next = body.position + direction * travel;
            if (!TryGround(next, out float floor) || Mathf.Abs(floor - body.position.y) > .15f)
            {
                blockingObject = "unsupported_ground";
                travel = 0;
            }
            next = body.position + direction * travel;
            ActualSpeed = travel / dt;
            if (travel > .0001f)
            {
                next.y = floor;
                FacingSign = direction.x < 0 ? -1 : 1;
                body.MovePosition(next);
                blockedTime = 0;
            }
            else { speed = 0; blockedTime += dt; visual.StopNpcMotionImmediately(); }
            movementTime += dt;
            if (blockedTime >= blockedTimeout || movementTime >= movementTimeout)
                Finish(NpcMoveOutcome.Blocked, blockedTime >= blockedTimeout ? "obstructed:" + blockingObject : "timed_out", true);
        }

        void SimulateDecision()
        {
            NpcMoveRequest request = BeginDecision();
            if (request == null) return;
            var candidates = new List<NpcMoveCommand>();
            foreach (NpcMoveCommand action in request.available_actions)
                if (action.type == "move" && Mathf.Abs(resolved[action.target].position.x - body.position.x) > .3f) candidates.Add(action);
            NpcMoveCommand choice = candidates.Count == 0 ? request.available_actions[0] : candidates[simulatedIndex++ % candidates.Count];
            var output = new NpcMoveOutput { dialogue = "", action = choice, info = new NpcMoveOutput.ActorInfo { actor_id = ActorId } };
            AcceptDecision(request.request_id, JsonUtility.ToJson(output), out _);
        }

        public void Interrupt(string reason = "cancelled")
        {
            InvalidateRequest();
            Finish(NpcMoveOutcome.Interrupted, reason, true);
            speed = ActualSpeed = 0;
            if (body != null) body.linearVelocity = Vector2.zero;
            nextDecisionTime = Time.time + simulatedWait;
        }

        NpcMoveResult Finish(NpcMoveOutcome outcome, string reason, bool stop, bool publish = true)
        {
            string completed = activeRequest;
            string destination = target?.id;
            activeRequest = null;
            target = null;
            if (stop)
            {
                speed = ActualSpeed = 0;
                if (body != null) body.linearVelocity = Vector2.zero;
                if (outcome == NpcMoveOutcome.Arrived) visual?.CompleteNpcMotionRoute();
                else visual?.StopNpcMotionImmediately();
            }
            nextDecisionTime = Time.time + simulatedWait;
            if (string.IsNullOrEmpty(completed)) return null;
            Vector2 position = body != null ? body.position : (Vector2)transform.position;
            var result = new NpcMoveResult(completed, ActorId, destination, outcome, reason, position);
            if (publish) Publish(result);
            return result;
        }

        void Publish(NpcMoveResult result)
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

        void LateUpdate()
        {
            if (!initialized || visual == null || !visual.isActiveAndEnabled) { hasVisualPosition = false; return; }
            if (!visual.UsesNpcMotion) { visual.Animate(ActualSpeed * FacingSign, true, 0, Time.deltaTime); return; }
            float x = transform.position.x;
            float dx = hasVisualPosition ? x - previousVisualX : 0;
            previousVisualX = x; hasVisualPosition = true;
            // Spawn/teleport/reload is not a walking stride. Interpolated render
            // displacement, rather than the requested physics velocity, drives art.
            if (!CanAct || Mathf.Abs(dx) > Mathf.Max(.25f, maxSpeed * Time.deltaTime * 2))
            {
                dx = 0;
                visual.StopNpcMotionImmediately();
            }
            visual.AnimateNpcDisplacement(dx, CanAct ? ActualSpeed * FacingSign : 0, Time.deltaTime);
        }

        void OnDisable()
        {
            hasVisualPosition = false;
            Interrupt("disabled");
            if (body != null) body.simulated = false;
            if (visual != null && visual.isActiveAndEnabled) visual.Animate(0, true, 0, 0);
        }

        void OnDestroy()
        {
            if (session == null) return;
            session.Changed -= OnContextChanged;
            session.Clock.Changed -= OnClockChanged;
            session.Dialogue.Changed -= OnDialogueChanged;
            session.TravelRequested -= OnTravelRequested;
        }

        static bool Finite(Vector2 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    }
}
