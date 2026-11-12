using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Seabreeze
{
    // Authored blocking for the first evening only. The groups follow the existing
    // character relationships; they do not change affinity, memory or greetings.
    public sealed class FirstDayGathering
    {
        sealed class Seat
        {
            public readonly string Id;
            public readonly int Group;
            public readonly float X, Shift;
            public readonly string[] Partners;
            public Seat(string id, int group, float x, float shift, params string[] partners)
            { Id = id; Group = group; X = x; Shift = shift; Partners = partners; }
        }

        static readonly Seat[] seats = {
            new Seat("z", 0, -7.2f, -.24f, "zhengyi"),
            new Seat("zhengyi", 0, -5.8f, .20f, "z", "liyuan"),
            new Seat("liyuan", 0, -4.4f, .24f, "zhengyi", "z"),
            new Seat("xiaoxiaoqing", 1, -1.3f, -.24f, "linxu", "liting"),
            new Seat("linxu", 1, .1f, -.20f, "xiaoxiaoqing", "liting"),
            new Seat("liting", 1, 1.5f, .24f, "xiaoxiaoqing", "linxu"),
            new Seat("zhou_bobo", 2, 5.1f, -.24f, "axing"),
            new Seat("axing", 2, 6.5f, .24f, "zhou_bobo")
        };

        sealed class Member
        {
            public Seat Seat;
            public NpcPresence Npc;
            public bool PreviousSimulation, PreviousHold, Moving;
            public Transform PreviousPartner;
            public float NextAction;
            public int Step, Index;
        }

        readonly List<Member> members = new List<Member>();
        SaveData owner;
        WorldScene stage;
        string greeting;

        public static bool TryPosition(string actor, out float x)
        {
            var seat = seats.FirstOrDefault(s => s.Id == actor);
            x = seat != null ? seat.X : 0;
            return seat != null;
        }

        public void BeginGreeting(string actor) { greeting = actor; }
        public void EndGreeting() { greeting = null; }

        public void Tick(GameSession session, WorldDirector world)
        {
            if (session?.FirstDay == null || !session.FirstDay.GatheringActive || world?.Stage == null
                || world.IsTransitioning || world.Stage.place != PlaceId.Square)
            { Reset(); return; }

            if (owner != session.State || stage != world.Stage)
            {
                Reset();
                owner = session.State;
                stage = world.Stage;
                session.Memory?.Conversations.CancelAll("第一日广场分组");
            }

            var actors = session.Memory?.Spawner?.Actors;
            if (actors == null) return;
            for (int i = members.Count - 1; i >= 0; i--)
            {
                var member = members[i];
                if (member.Npc != null && member.Npc.IsPresent && actors.Contains(member.Npc)) continue;
                Release(member);
                members.RemoveAt(i);
            }
            for (int i = 0; i < seats.Length; i++)
            {
                var seat = seats[i];
                var npc = actors.FirstOrDefault(a => a != null && a.npcId == seat.Id && a.IsPresent
                    && a.Movement != null && a.Movement.IsReady && a.gameObject.scene == stage.gameObject.scene);
                if (npc == null || members.Any(m => m.Npc == npc)) continue;
                members.Add(new Member { Seat = seat, Npc = npc, Index = i,
                    PreviousSimulation = npc.Movement.SimulateDecisions, PreviousHold = npc.Movement.InteractionHeld,
                    PreviousPartner = npc.ConversationPartner, NextAction = Time.time + 3 + i * .7f });
                npc.Movement.SimulateDecisions = false;
                npc.Movement.SetInteractionHold(true);
            }

            bool paused = session.InputBlocked || Time.timeScale <= 0;
            foreach (var member in members)
            {
                var npc = member.Npc;
                var movement = npc.Movement;
                var partner = Partner(member);
                npc.ConversationPartner = npc.npcId == greeting ? world.Player : partner;
                movement.SimulateDecisions = false;

                if (paused)
                {
                    movement.SetInteractionHold(true);
                    member.Moving = false;
                    member.NextAction = Time.time + 2 + member.Index * .3f;
                    continue;
                }
                if (member.Moving)
                {
                    if (movement.IsMoving) continue;
                    member.Moving = false;
                    movement.SetInteractionHold(true);
                }
                if (Time.time < member.NextAction) continue;
                // Most beats are quiet listening or a change of gaze. Stagger the
                // short steps so a group never marches in unison.
                if (member.Step % 2 == 1)
                {
                    if (members.Any(m => m != member && m.Seat.Group == member.Seat.Group && m.Moving))
                    { member.NextAction = Time.time + 1.3f; continue; }
                    float x = member.Seat.X + (member.Step % 4 == 1 ? member.Seat.Shift : 0);
                    var point = new Vector2(x, npc.transform.position.y);
                    movement.SetInteractionHold(false);
                    int facing = partner != null && partner.position.x < x ? -1 : 1;
                    member.Moving = movement.MoveForConversation(point, facing);
                    if (!member.Moving) movement.SetInteractionHold(true);
                }
                member.Step++;
                member.NextAction = Time.time + 6 + member.Index * .35f + member.Step % 3;
            }
        }

        Transform Partner(Member member)
        {
            var candidates = member.Seat.Partners;
            for (int i = 0; i < candidates.Length; i++)
            {
                string id = candidates[(member.Step / 2 + i) % candidates.Length];
                var other = members.FirstOrDefault(m => m.Seat.Id == id && m.Npc != null && m.Npc.IsPresent);
                if (other != null) return other.Npc.transform;
            }
            return null;
        }

        static void Release(Member member)
        {
            if (member.Npc == null) return;
            member.Npc.ConversationPartner = member.PreviousPartner;
            var movement = member.Npc.Movement;
            if (movement == null) return;
            movement.Interrupt("firstday_gathering_finished");
            movement.SetInteractionHold(member.PreviousHold);
            movement.SimulateDecisions = member.PreviousSimulation;
        }

        public void Reset()
        {
            foreach (var member in members) Release(member);
            members.Clear();
            owner = null;
            stage = null;
            greeting = null;
        }
    }
}
