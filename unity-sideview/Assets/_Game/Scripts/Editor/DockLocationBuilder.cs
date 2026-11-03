using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZFromMoon.Core;
using ZFromMoon.Dialogue;
using ZFromMoon.Interaction;
using ZFromMoon.Player;
using ZFromMoon.UI;
using ZFromMoon.World;

namespace ZFromMoon.EditorTools
{
    /// <summary>
    /// One-click setup for the walkable dock location:
    /// imports the layered dusk art, patches the player visual in App,
    /// wires the camera + dialogue staging, then builds DockLocation.unity.
    /// Menu: Z From Moon / Build Dock Location
    /// </summary>
    public static class DockLocationBuilder
    {
        private const string AppScenePath = "Assets/_Game/Scenes/App.unity";
        private const string ScenePath = "Assets/_Game/Scenes/DockLocation.unity";
        private const string DockArtDir = "Assets/_Game/Art/Locations/dock_dusk";
        private const string PlayerArtDir = "Assets/_Game/Art/Characters/Player";
        private const string ZArtDir = "Assets/_Game/Art/Characters/Z";
        private const string AnimDir = "Assets/_Game/Animations/Player";
        private const string MaterialPath = DockArtDir + "/fx_glow_additive.mat";

        private const float Ppu = 100f;
        private const float GroundY = -3.35f;
        private const float SpawnX = -5f;
        private const float WallLeft = -15.5f;
        private const float WallRight = 15.5f;
        private const float CameraMinX = -8.8f;
        private const float CameraMaxX = 8.8f;
        private const float PlayerHeight = 4.0f;
        private const int CharOrder = 20;

        [MenuItem("Z From Moon/Build Dock Location")]
        public static void Build()
        {
            ImportArt();
            var playerController = BuildWalkController(PlayerArtDir, "PlayerWalkController", new[]
            {
                "player_walk_0", "player_walk_1", "player_walk_2", "player_walk_3",
                "player_walk_4", "player_walk_5", "player_walk_6", "player_walk_7"
            });
            var zController = BuildWalkController(ZArtDir, "ZWalkController", new[]
            {
                "z_walk_0", "z_walk_1", "z_walk_2", "z_walk_3",
                "z_walk_4", "z_walk_5", "z_walk_6", "z_walk_7"
            });

            var app = EditorSceneManager.OpenScene(AppScenePath, OpenSceneMode.Single);
            PatchApp(app, playerController);
            EditorSceneManager.MarkSceneDirty(app);
            EditorSceneManager.SaveScene(app);

            BuildLocationScene(zController);
            AddToBuildSettings();
            AssetDatabase.SaveAssets();

            Debug.Log("[DockLocation] 完成：DockLocation.unity 已生成，App 已接线（玩家/相机/对话演出）。");
        }

        // ---------------- art import ----------------
        private static void ImportArt()
        {
            foreach (var file in Directory.GetFiles(DockArtDir, "*.png"))
                SetSpriteImport(file, SpriteAlignment.Center, fullRect: true);

            for (int i = 0; i < 8; i++)
                SetSpriteImport($"{PlayerArtDir}/player_walk_{i}.png", SpriteAlignment.BottomCenter, fullRect: true);
            for (int i = 0; i < 8; i++)
                SetSpriteImport($"{ZArtDir}/z_walk_{i}.png", SpriteAlignment.BottomCenter, fullRect: true);

            SetSpriteImport("Assets/_Game/Art/FX/shadow_soft.png", SpriteAlignment.Center, fullRect: true);

            AssetDatabase.Refresh();
        }

        private static void SetSpriteImport(string path, SpriteAlignment alignment, bool fullRect)
        {
            path = path.Replace('\\', '/');
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = Ppu;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)alignment;
            settings.spriteMeshType = fullRect ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        // ---------------- animation ----------------
        private static AnimatorController BuildWalkController(string artDir, string controllerName, string[] frameNames)
        {
            EnsureFolder(AnimDir);
            var frames = new List<Sprite>();
            foreach (var name in frameNames)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{artDir}/{name}.png");
                if (sprite != null) frames.Add(sprite);
            }
            if (frames.Count == 0)
            {
                Debug.LogError($"[DockLocation] 缺少角色帧：{artDir}");
                return null;
            }

            string controllerPath = $"{AnimDir}/{controllerName}.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
                controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);

                var machine = controller.layers[0].stateMachine;
                var idle = machine.AddState("Idle");
                var walk = machine.AddState("Walk");
                machine.defaultState = idle;

                var toWalk = idle.AddTransition(walk);
                toWalk.hasExitTime = false;
                toWalk.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");
                var toIdle = walk.AddTransition(idle);
                toIdle.hasExitTime = false;
                toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");
            }

            var idleState = FindState(controller, "Idle");
            var walkState = FindState(controller, "Walk");
            if (idleState != null)
                idleState.motion = MakeFrameClip(AnimDir, controllerName + "_idle", new[] { frames[Mathf.Min(2, frames.Count - 1)] });
            if (walkState != null)
                walkState.motion = MakeFrameClip(AnimDir, controllerName + "_walk", frames.ToArray());

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimatorState FindState(AnimatorController controller, string stateName)
        {
            foreach (var state in controller.layers[0].stateMachine.states)
                if (state.state.name == stateName) return state.state;
            return null;
        }

        private static AnimationClip MakeFrameClip(string folder, string clipName, Sprite[] frames)
        {
            string path = $"{folder}/{clipName}.anim";
            AssetDatabase.DeleteAsset(path);
            var clip = new AnimationClip { frameRate = 12f };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = "",
                propertyName = "m_Sprite",
            };
            var keys = new ObjectReferenceKeyframe[frames.Length];
            for (int i = 0; i < frames.Length; i++)
                keys[i] = new ObjectReferenceKeyframe { time = i / 12f, value = frames[i] };
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

            AssetDatabase.CreateAsset(clip, path);
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }

        // ---------------- App wiring ----------------
        private static void PatchApp(Scene app, AnimatorController playerController)
        {
            var motor = Object.FindAnyObjectByType<PlayerMotor>();
            if (motor == null)
            {
                Debug.LogError("[DockLocation] App 中找不到 PlayerMotor。");
                return;
            }

            var root = motor.transform;
            var visual = root.Find("Visual");
            var animated = visual != null ? visual.Find("Animated") : null;
            if (animated != null) animated.gameObject.SetActive(false);

            var spriteTransform = visual.Find("Sprite");
            if (spriteTransform == null)
            {
                var go = new GameObject("Sprite");
                go.transform.SetParent(visual, false);
                spriteTransform = go.transform;
            }

            var renderer = spriteTransform.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = spriteTransform.gameObject.AddComponent<SpriteRenderer>();
            var firstFrame = AssetDatabase.LoadAssetAtPath<Sprite>($"{PlayerArtDir}/player_walk_0.png");
            renderer.sprite = firstFrame;
            renderer.sortingOrder = CharOrder;

            var animator = spriteTransform.GetComponent<Animator>();
            if (animator == null) animator = spriteTransform.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = playerController;
            animator.applyRootMotion = false;

            float scale = PlayerHeight / (firstFrame != null ? firstFrame.rect.height / Ppu : 8.36f);
            spriteTransform.localScale = Vector3.one * scale;
            spriteTransform.localPosition = Vector3.zero;
            spriteTransform.localRotation = Quaternion.identity;

            CreateShadow(root);

            var bob = spriteTransform.GetComponent<SpriteBob>();
            if (bob == null) bob = spriteTransform.gameObject.AddComponent<SpriteBob>();
            bob.Configure(0.012f, 1.3f, 0f);

            var view = visual.GetComponent<PlayerView>();
            if (view != null)
            {
                var so = new SerializedObject(view);
                so.FindProperty("visualRoot").objectReferenceValue = visual;
                so.FindProperty("animator").objectReferenceValue = animator;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var capsule = root.GetComponent<CapsuleCollider2D>();
            if (capsule != null)
            {
                capsule.direction = CapsuleDirection2D.Vertical;
                capsule.size = new Vector2(1.0f, 3.7f);
                capsule.offset = new Vector2(0f, 1.85f);
            }

            var camera = Object.FindAnyObjectByType<Camera>();
            if (camera != null)
            {
                camera.transform.position = new Vector3(SpawnX, 0f, -10f);
                if (camera.GetComponent<CameraFollow>() == null)
                    camera.gameObject.AddComponent<CameraFollow>();
            }

            var controller = Object.FindAnyObjectByType<DialogueController>();
            if (controller != null && controller.GetComponent<DialogueStaging>() == null)
                controller.gameObject.AddComponent<DialogueStaging>();

            var session = Object.FindAnyObjectByType<GameSession>();
            if (session != null)
            {
                var so = new SerializedObject(session);
                so.FindProperty("initialScenePath").stringValue = ScenePath;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ---------------- location scene ----------------
        private static void BuildLocationScene(AnimatorController zController)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("DockLocation");

            var spawn = new GameObject("FromShore").transform;
            spawn.SetParent(root.transform, false);
            spawn.position = new Vector3(SpawnX, GroundY, 0f);

            var context = root.AddComponent<SceneContext>();
            SetContext(context, "dock_dusk", "海边码头", "from_shore", spawn);

            var art = new GameObject("Art").transform;
            art.SetParent(root.transform, false);
            CreateLayer(art, "bg_skysea", -100, 0.25f);
            CreateLayer(art, "island_far", -90, 0.45f);
            CreateLayer(art, "boat", -80, 0.75f);
            CreateLayer(art, "house", -70, 0.75f);
            CreateLayer(art, "ground", 10, 1f);
            CreateLayer(art, "foreground", 30, 1.04f);
            CreateGlowLayer(art);

            CreateCollision(root.transform);
            CreateIslander(root.transform, zController);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void SetContext(SceneContext context, string id, string display, string entry, Transform spawn)
        {
            var so = new SerializedObject(context);
            so.FindProperty("locationId").stringValue = id;
            so.FindProperty("displayName").stringValue = display;
            so.FindProperty("entryId").stringValue = entry;
            so.FindProperty("initialSpawn").objectReferenceValue = spawn;
            so.FindProperty("cameraBoundsX").vector2Value = new Vector2(CameraMinX, CameraMaxX);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateLayer(Transform parent, string spriteName, int order, float depth)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{DockArtDir}/{spriteName}.png");
            if (sprite == null)
            {
                Debug.LogWarning($"[DockLocation] 缺少图层 {spriteName}");
                return;
            }

            var go = new GameObject(spriteName);
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            var parallax = go.AddComponent<ParallaxLayer>();
            parallax.Configure(null, depth, SpawnX);
        }

        private static void CreateGlowLayer(Transform parent)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{DockArtDir}/fx_glow.png");
            if (sprite == null) return;

            var go = new GameObject("fx_glow");
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 40;
            renderer.sharedMaterial = GetGlowMaterial();
            renderer.color = new Color(1f, 1f, 1f, 0.22f);
            var pulse = go.AddComponent<GlowPulse>();
            var so = new SerializedObject(pulse);
            so.FindProperty("target").objectReferenceValue = renderer;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.AddComponent<ParallaxLayer>().Configure(null, 1f, SpawnX);
        }

        private static Material GetGlowMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;
            var shader = Shader.Find("ZFromMoon/SpriteAdditive");
            if (shader == null)
            {
                Debug.LogWarning("[DockLocation] 找不到 ZFromMoon/SpriteAdditive shader，使用默认材质。");
                return null;
            }
            material = new Material(shader) { name = "fx_glow_additive" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        private static void CreateCollision(Transform parent)
        {
            var collision = new GameObject("Collision").transform;
            collision.SetParent(parent, false);

            CreateBox(collision, "Ground", new Vector2(0f, GroundY - 0.5f), new Vector2(46f, 1f));
            CreateBox(collision, "LeftBoundary", new Vector2(WallLeft, 2f), new Vector2(1f, 24f));
            CreateBox(collision, "RightBoundary", new Vector2(WallRight, 2f), new Vector2(1f, 24f));
        }

        private static void CreateBox(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.layer = 9; // WorldSolid
            var box = go.AddComponent<BoxCollider2D>();
            box.size = size;
        }

        private static void CreateIslander(Transform parent, AnimatorController zController)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{ZArtDir}/z_walk_0.png");
            var go = new GameObject("Islander");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(1.8f, GroundY, 0f);
            go.layer = 10; // Interactable

            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            float scale = 3.6f / (sprite != null ? sprite.rect.height / Ppu : 13.12f);
            visual.transform.localScale = new Vector3(-scale, scale, 1f);
            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = CharOrder;
            visual.AddComponent<SpriteBob>().Configure(0.014f, 1.1f, 1.7f);
            if (zController != null)
            {
                var animator = visual.AddComponent<Animator>();
                animator.runtimeAnimatorController = zController;
            }

            CreateShadow(go.transform);

            var trigger = go.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(1.0f, 3.4f);
            trigger.offset = new Vector2(0f, 1.7f);

            var interactable = go.AddComponent<Interactable>();
            var definition = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(
                "Assets/_Game/Data/Dialogue/IslanderDialogue.asset");
            var actor = go.AddComponent<DialogueActor>();

            var point = new GameObject("InteractionPoint").transform;
            point.SetParent(go.transform, false);
            point.localPosition = new Vector3(-0.6f, 1.8f, 0f);
            var prompt = new GameObject("PromptAnchor").transform;
            prompt.SetParent(go.transform, false);
            prompt.localPosition = new Vector3(0f, 3.9f, 0f);

            var so = new SerializedObject(interactable);
            so.FindProperty("interactionId").stringValue = "dock_islander";
            so.FindProperty("displayName").stringValue = "岛民";
            so.FindProperty("actionLabel").stringValue = "交谈";
            so.FindProperty("interactionPoint").objectReferenceValue = point;
            so.FindProperty("promptAnchor").objectReferenceValue = prompt;
            so.FindProperty("priority").intValue = 0;
            so.FindProperty("interactionEnabled").boolValue = true;
            so.FindProperty("responseText").stringValue = string.Empty;
            so.ApplyModifiedPropertiesWithoutUndo();

            var actorSo = new SerializedObject(actor);
            actorSo.FindProperty("definition").objectReferenceValue = definition;
            actorSo.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateShadow(Transform parent)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Game/Art/FX/shadow_soft.png");
            if (sprite == null) return;

            var existing = parent.Find("Shadow");
            var go = existing != null ? existing.gameObject : new GameObject("Shadow");
            if (existing == null) go.transform.SetParent(parent, false);

            go.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            go.transform.localScale = new Vector3(0.85f, 0.5f, 1f);
            var renderer = go.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = CharOrder - 1;
            renderer.color = new Color(1f, 1f, 1f, 0.42f);
        }

        private static void AddToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var entry in scenes)
                if (entry.path == ScenePath) return;
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
