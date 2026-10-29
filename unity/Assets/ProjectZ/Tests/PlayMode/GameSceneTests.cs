using System;
using System.Collections;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using ProjectZ.Core;
using ProjectZ.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProjectZ.Tests
{
    public sealed class GameSceneTests
    {
        private ProjectZGame game;
        private T Field<T>(string name) => (T)typeof(ProjectZGame).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
        private void Call(string name, params object[] arguments) => typeof(ProjectZGame)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, arguments);

        [UnitySetUp]
        public IEnumerator StartIsolatedGame()
        {
            Assert.That(Array.IndexOf(Environment.GetCommandLineArgs(), "-z-save-root"), Is.GreaterThanOrEqualTo(0),
                "PlayMode tests must receive an isolated save directory.");
            game = UnityEngine.Object.FindFirstObjectByType<ProjectZGame>();
            if (game == null) game = new GameObject("Project Z test").AddComponent<ProjectZGame>();
            yield return null;
            Assert.That(Field<bool>("initialized"), Is.True);
            Call("NewGame");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SceneHasChineseFontPlayerCameraAndFace()
        {
            Assert.That(Resources.Load<Font>("Fonts/NotoSansCJKsc-Regular").HasCharacter('你'), Is.True);
            var island = Field<IslandScene>("island");
            Assert.That(island.Player.GetComponent<CharacterController>().enabled, Is.True);
            Assert.That(island.Camera.GetComponent<Camera>().enabled, Is.True);
            Assert.That(island.Z.Face.sharedMesh.blendShapeCount, Is.GreaterThan(40));
            Assert.That(island.Z.SourceAvatar.isHuman, Is.True);
            Assert.That(island.Z.MotionAnimator.runtimeAnimatorController, Is.Not.Null);
            // Placement must retain FBX axis/unit conversion; otherwise the bench becomes 100x too small.
            var bench = GameObject.Find("CoveBench").GetComponentInChildren<Renderer>().bounds;
            Assert.That(bench.size.y, Is.InRange(.9f, 1.3f));
            Assert.That(bench.size.x, Is.GreaterThan(2));
            Assert.That(island.Locations.ContainsKey("cove") && island.Locations.ContainsKey("map_table"), Is.True);
            var restingArm = island.Z.RightArm.localRotation;
            island.Z.Perform("wave", "warm");
            yield return new WaitForSeconds(1.1f);
            Assert.That(island.Z.SmileIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(island.Z.Face.GetBlendShapeWeight(island.Z.SmileIndex), Is.GreaterThan(15));
            Assert.That(Quaternion.Angle(island.Z.RightArm.localRotation, restingArm), Is.GreaterThan(20));
        }

        [UnityTest]
        public IEnumerator GestureTransitionsKeepFeetPlantedAndReturnToIdle()
        {
            var z = Field<IslandScene>("island").Z;
            z.GazeTarget = null;
            var transforms = z.GetComponentsInChildren<Transform>();
            var feet = transforms.Where(t => t.name == "J_Bip_L_Foot" || t.name == "J_Bip_R_Foot").ToArray();
            var index = transforms.First(t => t.name == "J_Bip_R_Index1");
            yield return new WaitForSeconds(.3f);
            var anchors = feet.Select(t => t.position).ToArray();
            var relaxedFinger = index.localRotation;
            var handAtRest = z.RightHand.position;
            z.Perform("wave", "warm");
            yield return new WaitForSeconds(1.15f);
            Assert.That(Vector3.Distance(handAtRest, z.RightHand.position), Is.GreaterThan(.15f));
            Assert.That(Quaternion.Angle(relaxedFinger, index.localRotation), Is.GreaterThan(4));
            for (var i = 0; i < feet.Length; i++)
                Assert.That(Vector3.Distance(anchors[i], feet[i].position), Is.LessThan(.008f));
            var beforeInterruption = z.RightHand.position;
            z.Perform("nod", "thoughtful");
            yield return null;
            Assert.That(Vector3.Distance(beforeInterruption, z.RightHand.position), Is.LessThan(.12f),
                "Interrupting a gesture must blend from the current pose.");
            yield return new WaitForSeconds(2.8f);
            Assert.That(z.MotionAnimator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), Is.True);
            for (var i = 0; i < feet.Length; i++)
                Assert.That(Vector3.Distance(anchors[i], feet[i].position), Is.LessThan(.008f));
        }

        [UnityTest]
        public IEnumerator CharacterControllerCanReachBothInteractionLocations()
        {
            var island = Field<IslandScene>("island");
            var controller = island.Player.GetComponent<CharacterController>();
            foreach (var destination in new[] { island.Locations["cove"], island.Locations["map_table"] })
            {
            var start = controller.transform.position;
            var simulated = 0f;
            var moves = 0;
            var deadline = Time.realtimeSinceStartup + 6;
            while (Vector3.Distance(controller.transform.position, destination) > 1.5f && Time.realtimeSinceStartup < deadline)
            {
                var direction = destination - controller.transform.position;
                direction.y = 0;
                controller.Move(direction.normalized * Time.deltaTime * 4.2f);
                simulated += Time.deltaTime;
                moves++;
                yield return null;
            }
            Assert.That(Vector3.Distance(controller.transform.position, destination), Is.LessThan(2.7f),
                "start=" + start + " end=" + controller.transform.position + " target=" + destination +
                " simulated=" + simulated + " moves=" + moves + " grounded=" + controller.isGrounded);
            }
        }

        [UnityTest]
        public IEnumerator GameHandlersSaveReloadAndContinueBothChoiceHistories()
        {
            foreach (var choice in new[] { "verify_notice", "decide_for_z" })
            {
                Call("NewGame");
                var session = Field<GameSession>("session");
                var definition = session.Definition("map_check");
                Call("OpenEpisode", definition);
                Call("Send", session.Choose(definition.id, choice));
                yield return new WaitUntil(() => !Field<bool>("busy"));
                Assert.That(Field<SaveStore>("saves").Exists, Is.True);
                var slot = session.Snapshot().slotId;
                Call("Continue");
                session = Field<GameSession>("session");
                Assert.That(session.Snapshot().slotId, Is.EqualTo(slot));
                Assert.That(session.Progress(definition.id).choiceId, Is.EqualTo(choice));
                session.NextDay();
                Call("Save");
                Call("OpenEpisode", definition);
                yield return new WaitUntil(() => !Field<bool>("busy"));
                Assert.That(session.Progress(definition.id).followupSeen, Is.True);
                var expected = definition.choices.Find(c => c.id == choice).followup;
                Assert.That(Field<string>("dialogue"), Is.EqualTo(expected));
                var island = Field<IslandScene>("island");
                yield return null;
                Assert.That(island.Camera.ConversationTarget, Is.EqualTo(island.Z.transform));
                Assert.That(Vector3.Distance(island.Camera.transform.position, island.Z.transform.position), Is.LessThan(5));
            }
            Call("NewGame");
            Assert.That(Field<GameSession>("session").Snapshot().events, Is.Empty);
        }
    }
}
