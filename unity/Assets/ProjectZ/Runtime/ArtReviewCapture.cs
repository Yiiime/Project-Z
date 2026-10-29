#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace ProjectZ.Runtime
{
    // Opt-in camera review of the real scene, using an explicit output directory.
    public sealed class ArtReviewCapture : MonoBehaviour
    {
        private IslandScene island;
        private ProjectZGame game;
        private string reviewStage;
        public void Initialize(IslandScene scene, ProjectZGame owner) { island = scene; game = owner; }

        private IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-z-art-review");
            if (index < 0 || index + 1 >= args.Length || !Path.IsPathRooted(args[index + 1]) ||
                Array.IndexOf(args, "-z-save-root") < 0)
                throw new ArgumentException("Art review requires absolute output and isolated save directories.");
            var output = args[index + 1];
            Directory.CreateDirectory(output);
            game.HideInterfaceForArtReview = true;
            island.Camera.enabled = false;
            island.Z.GazeTarget = null;
            island.Z.transform.rotation = Quaternion.Euler(0, -75, 0);
            foreach (var renderer in island.Player.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            var camera = island.Camera.GetComponent<Camera>();
            camera.fieldOfView = 35;
            yield return new WaitForSeconds(2);
            if (Array.IndexOf(args, "-z-motion-review") >= 0)
            {
                yield return RecordMotion(camera, output);
                Application.Quit();
                yield break;
            }
            foreach (var angle in new[] { 0f, 45f, 100f, 180f })
            {
                var direction = Quaternion.Euler(0, angle, 0) * island.Z.transform.forward;
                camera.transform.position = island.Z.transform.position + direction * 3.1f + Vector3.up * 1.3f;
                camera.transform.LookAt(island.Z.transform.position + Vector3.up * .90f);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(output, "z-angle-" + angle + ".png"));
                yield return null;
            }
            camera.transform.position = island.Z.transform.position + island.Z.transform.forward * 2.0f + Vector3.up * 1.5f;
            camera.transform.LookAt(island.Z.transform.position + Vector3.up * 1.28f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "z-portrait.png"));
            island.Z.Perform("wave", "warm");
            yield return new WaitForSeconds(1.25f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "z-wave.png"));
            camera.transform.position = new Vector3(13, 7, -13);
            camera.transform.LookAt(new Vector3(3, .6f, -1));
            camera.fieldOfView = 48;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "cove.png"));
            yield return new WaitForSeconds(1);
            Debug.Log("PROJECT_Z_ART_REVIEW_CAPTURED: " + output);
            Application.Quit();
        }
        private IEnumerator RecordMotion(Camera camera, string output)
        {
            var folder = Path.Combine(output, "frames");
            Directory.CreateDirectory(folder);
            var focus = new GameObject("Motion review gaze target");
            var z = island.Z;
            z.GazeTarget = focus.transform;
            camera.fieldOfView = 38;
            Time.captureFramerate = 30;
            for (var frame = 0; frame < 450; frame++)
            {
                var seconds = frame / 30f;
                var close = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.7f, 3.1f, seconds)) *
                    (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(11.5f, 13, seconds)));
                camera.transform.position = z.transform.position + z.transform.forward * Mathf.Lerp(3.05f, 2.12f, close) +
                    Vector3.up * Mathf.Lerp(1.28f, 1.47f, close);
                camera.transform.LookAt(z.transform.position + Vector3.up * Mathf.Lerp(.98f, 1.24f, close));
                var shift = Mathf.SmoothStep(0, .85f, Mathf.InverseLerp(2, 3.2f, seconds)) *
                    (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(11, 12.5f, seconds)));
                focus.transform.position = z.transform.position + z.transform.forward * 2.5f + z.transform.right * shift;
                if (frame == 0) { z.Perform("look", "neutral"); reviewStage = "01   IDLE"; }
                if (frame == 60) reviewStage = "02   GAZE";
                if (frame == 120) { z.Perform("wave", "warm"); reviewStage = "03   WAVE"; }
                if (frame == 240) { z.Perform("nod", "thoughtful"); reviewStage = "04   NOD"; }
                if (frame == 330) { z.Perform("look", "neutral"); reviewStage = "05   SETTLE"; }
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "frame-" + frame.ToString("D4") + ".png"));
            }
            Time.captureFramerate = 0;
            yield return new WaitForSeconds(1);
            Debug.Log("PROJECT_Z_MOTION_REVIEW_CAPTURED: 450 frames at 30 fps; " + output);
        }
        private void OnGUI()
        {
            if (string.IsNullOrEmpty(reviewStage)) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(26, 20, 250, 40), reviewStage, style);
        }
    }
}
#endif
