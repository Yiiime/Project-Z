using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ProjectZ.Editor
{
    // Bakes editable transform clips on the imported skeleton. The source Humanoid
    // avatar is retained separately for future retargeted animation imports.
    public static class ZMotionAuthoring
    {
        private const string Folder = "Assets/ProjectZ/Art/Animation";
        private sealed class Bone
        {
            public Transform Transform;
            public string Path;
            public Vector3 Position;
            public Quaternion Rotation;
            public readonly List<Quaternion> Samples = new List<Quaternion>();
            public readonly List<Vector3> Positions = new List<Vector3>();
        }

        public static RuntimeAnimatorController Build(GameObject actor)
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            var bones = actor.GetComponentsInChildren<Transform>()
                .Where(t => t.name.StartsWith("J_") || t.name.StartsWith("HairJoint") || t.name.StartsWith("Z_Skirt_"))
                .Select(t => new Bone { Transform = t, Path = AnimationUtility.CalculateTransformPath(t, actor.transform),
                    Position = t.localPosition, Rotation = t.localRotation }).ToArray();
            var lookup = bones.ToDictionary(b => b.Transform.name, b => b.Transform);
            Transform B(string name) => lookup[name];
            void Restore()
            {
                foreach (var b in bones) { b.Transform.localPosition = b.Position; b.Transform.localRotation = b.Rotation; }
            }
            var curls = new Dictionary<string, Vector3>();
            foreach (var side in new[] { "L", "R" })
            {
                var hand = B("J_Bip_" + side + "_Hand");
                var width = (B("J_Bip_" + side + "_Index1").position - B("J_Bip_" + side + "_Little1").position).normalized;
                var direction = (B("J_Bip_" + side + "_Middle2").position - hand.position).normalized;
                if (Vector3.Dot(Vector3.Cross(width, direction), -actor.transform.up) < 0) width = -width;
                curls[side] = hand.InverseTransformDirection(width);
            }
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var action in new[] { "Idle", "Wave", "Nod", "Pause" })
            {
                var duration = action == "Idle" ? 8f : action == "Nod" ? 2.2f : 3.4f;
                foreach (var b in bones) { b.Samples.Clear(); b.Positions.Clear(); }
                var frames = Mathf.RoundToInt(duration * 30);
                for (var frame = 0; frame <= frames; frame++)
                {
                    Restore();
                    Pose(actor.transform, lookup, curls, action, frame / 30f);
                    foreach (var b in bones)
                    {
                        var q = b.Transform.localRotation;
                        if (b.Samples.Count > 0 && Quaternion.Dot(q, b.Samples[b.Samples.Count - 1]) < 0)
                            q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        b.Samples.Add(q); b.Positions.Add(b.Transform.localPosition);
                    }
                }
                var clip = new AnimationClip { name = "Z_" + action, frameRate = 30 };
                foreach (var bone in bones)
                {
                    for (var axis = 0; axis < 4; axis++)
                        clip.SetCurve(bone.Path, typeof(Transform), "m_LocalRotation." + "xyzw"[axis],
                            Curve(bone.Samples.Select(q => q[axis]).ToArray(), duration));
                    if (bone.Transform.name == "J_Bip_C_Hips")
                        for (var axis = 0; axis < 3; axis++)
                            clip.SetCurve(bone.Path, typeof(Transform), "m_LocalPosition." + "xyz"[axis],
                                Curve(bone.Positions.Select(p => p[axis]).ToArray(), duration));
                }
                clip.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = action == "Idle"; settings.loopBlend = action == "Idle";
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                var path = Folder + "/Z_" + action + ".anim";
                var stored = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (stored == null) { AssetDatabase.CreateAsset(clip, path); stored = clip; }
                else { EditorUtility.CopySerialized(clip, stored); UnityEngine.Object.DestroyImmediate(clip); }
                clips[action] = stored;
            }
            Restore();
            var controllerPath = Folder + "/Z_Standing.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) ??
                AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            var states = new Dictionary<string, AnimatorState>();
            foreach (var pair in clips)
            {
                var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == pair.Key) ?? machine.AddState(pair.Key);
                state.motion = pair.Value; state.writeDefaultValues = false; states[pair.Key] = state;
            }
            machine.defaultState = states["Idle"];
            foreach (var name in new[] { "Wave", "Nod", "Pause" })
            {
                var transition = states[name].transitions.FirstOrDefault() ?? states[name].AddTransition(states["Idle"]);
                transition.destinationState = states["Idle"]; transition.hasExitTime = true;
                transition.exitTime = .90f; transition.hasFixedDuration = true; transition.duration = .30f;
                transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
            }
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
            return controller;
        }

        private static AnimationCurve Curve(float[] values, float duration)
        {
            if (values.Max() - values.Min() < .000001f) return AnimationCurve.Linear(0, values[0], duration, values[0]);
            var keys = new Keyframe[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                var a = Mathf.Max(0, i - 1); var b = Mathf.Min(values.Length - 1, i + 1);
                var slope = (values[b] - values[a]) * 30 / (b - a);
                keys[i] = new Keyframe(i / 30f, values[i], slope, slope);
            }
            return new AnimationCurve(keys);
        }
        private static AnimationCurve Track(params float[] values)
        {
            var keys = new Keyframe[values.Length / 2];
            for (var i = 0; i < keys.Length; i++) keys[i] = new Keyframe(values[i * 2], values[i * 2 + 1]);
            var curve = new AnimationCurve(keys);
            for (var i = 0; i < keys.Length; i++) curve.SmoothTangents(i, 0);
            return curve;
        }
        private static readonly AnimationCurve Lift = Track(0,0,.18f,.02f,.46f,.36f,.80f,1,1.15f,1,2.12f,1,2.5f,.82f,3,.12f,3.4f,0);
        private static readonly AnimationCurve Elbow = Track(0,12,.18f,19,.48f,82,.82f,120,2.15f,120,2.50f,98,3,28,3.4f,12);
        private static readonly AnimationCurve Nod = Track(0,0,.22f,-2,.54f,8,.82f,6,1.2f,-1,1.65f,0,2.2f,0);
        private static readonly AnimationCurve Stop = Track(0,0,.2f,.05f,.65f,1,1.9f,1,2.7f,.25f,3.4f,0);

        private static void Pose(Transform actor, Dictionary<string, Transform> bones, Dictionary<string, Vector3> curls, string action, float time)
        {
            Transform B(string name) => bones[name];
            void Rotate(Transform t, Vector3 angles)
            { t.rotation = actor.rotation * Quaternion.Euler(angles) * Quaternion.Inverse(actor.rotation) * t.rotation; }
            var cycle = time / 8f * Mathf.PI * 2;
            var breathing = Mathf.Sin(cycle * 2);
            var sway = Mathf.Sin(cycle);
            var wave = action == "Wave" ? Mathf.Clamp01(Lift.Evaluate(time)) : 0;
            var stop = action == "Pause" ? Mathf.Clamp01(Stop.Evaluate(time)) : 0;
            var nod = action == "Nod" ? Nod.Evaluate(time) : 0;
            B("J_Bip_C_Hips").position += actor.TransformVector(new Vector3(.008f * sway - .006f * wave, -.002f, 0));
            Rotate(B("J_Bip_C_Hips"), new Vector3(0, 0, .6f * sway));
            Rotate(B("J_Bip_C_Spine"), new Vector3(breathing * .25f, -wave * 2, -sway * .5f + wave));
            Rotate(B("J_Bip_C_Chest"), new Vector3(breathing * .35f + nod * .12f, -wave * 3 + stop * 2, sway * .4f - wave));
            Rotate(B("J_Bip_C_Neck"), new Vector3(nod * .28f, 0, -wave * 1.5f));
            Rotate(B("J_Bip_C_Head"), new Vector3(nod * .72f, stop * -3, Mathf.Sin(cycle + .3f) * .45f - wave));
            foreach (var sideName in new[] { "L", "R" })
            {
                var prefix = "J_Bip_" + sideName + "_";
                var upper = B(prefix + "UpperArm"); var lower = B(prefix + "LowerArm"); var hand = B(prefix + "Hand");
                var side = Mathf.Sign(actor.InverseTransformVector(lower.position - upper.position).x);
                var active = sideName == "R";
                var lift = active ? wave : 0;
                var pause = active ? stop : 0;
                Rotate(B(prefix + "Shoulder"), new Vector3(0, -side * (lift * 3 + pause), side * lift * 2));
                Rotate(upper, new Vector3(0, -side * 3, side * (-80 + lift * 48 + pause * 25 + breathing * .5f)));
                Rotate(lower, new Vector3(0, 0, side * (active && action == "Wave" ? Elbow.Evaluate(time) : 12 + pause * 82)));
                var twist = -side * (lift * 74 + pause * 66);
                hand.rotation = Quaternion.AngleAxis(twist, (hand.position - lower.position).normalized) * hand.rotation;
                var flutterEnvelope = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.9f, 1.1f, time)) *
                    (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(2.02f, 2.28f, time)));
                var flutter = action == "Wave" && active ? Mathf.Sin((time - 1.05f) * 11) * 8 * flutterEnvelope : 0;
                Rotate(hand, new Vector3(0, 0, side * flutter));
                var fingers = new[] { "Index", "Middle", "Ring", "Little", "Thumb" };
                for (var f = 0; f < fingers.Length; f++)
                {
                    var curl = Mathf.Lerp(f == 4 ? 22 : 27 + f * 6, f == 4 ? 10 : 5 + f * 2, Mathf.Max(lift, pause));
                    var axis = hand.TransformDirection(curls[sideName]);
                    for (var joint = 1; joint <= 3; joint++)
                    {
                        var bone = B(prefix + fingers[f] + joint);
                        var amount = curl * (joint == 1 ? .50f : joint == 2 ? .62f : .35f);
                        bone.rotation = Quaternion.AngleAxis(amount, axis) * bone.rotation;
                    }
                }
            }
        }
    }
}
