using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZFromMoon.Core;
using ZFromMoon.Dialogue;
using ZFromMoon.Interaction;
using ZFromMoon.World;

namespace ZFromMoon.EditorTools
{
    /// <summary>
    /// Builds the walkable locations from the Figma-derived backgrounds and wires
    /// the travel graph App -> Loc_Dock -> Loc_HarborCabin -> Loc_TownStreet.
    /// Menu: Z From Moon / Build World Locations
    /// </summary>
    public static class WorldBuilder
    {
        private const string AppScenePath = "Assets/_Game/Scenes/App.unity";
        private const string SceneDir = "Assets/_Game/Scenes/Locations";
        private const string ArtDir = "Assets/_Game/Art/Locations/gen";
        private const string ShadowPath = "Assets/_Game/Art/FX/shadow_soft.png";
        private const string ZSpritePath = "Assets/_Game/Art/Characters/Z/z_walk_0.png";
        private const string ZControllerPath = "Assets/_Game/Animations/Player/ZWalkController.controller";
        private const float Ppu = 100f;
        private const float ViewHeight = 10.8f;
        private const int CharOrder = 20;

        private sealed class LocationSpec
        {
            public string SceneName;
            public string Id;
            public string DisplayName;
            public string ArtFile;
            public float GroundY;
            public float WalkMinX;
            public float WalkMaxX;
            public float CameraRange;
            public string LeftScene;      // target scene path or null
            public string RightScene;
            public float NpcX;            // 0 = no NPC
        }

        private static readonly LocationSpec[] Specs =
        {
            new()
            {
                SceneName = "Loc_Dock", Id = "dock_dusk", DisplayName = "码头",
                ArtFile = "loc_dock_dusk.png", GroundY = -3.02f, WalkMinX = -10.5f, WalkMaxX = 10.5f, CameraRange = 3.0f,
                RightScene = "Assets/_Game/Scenes/Locations/Loc_HarborCabin.unity",
            },
            new()
            {
                SceneName = "Loc_HarborCabin", Id = "harbor_cabin", DisplayName = "港湾木屋",
                ArtFile = "loc_harbor_dusk.png", GroundY = -1.35f, WalkMinX = -1.4f, WalkMaxX = 8.6f, CameraRange = 0f,
                LeftScene = "Assets/_Game/Scenes/Locations/Loc_Dock.unity",
                RightScene = "Assets/_Game/Scenes/Locations/Loc_TownStreet.unity",
            },
            new()
            {
                SceneName = "Loc_TownStreet", Id = "town_street", DisplayName = "小镇街道",
                ArtFile = "loc_street_dusk.png", GroundY = -3.02f, WalkMinX = -10.5f, WalkMaxX = 10.5f, CameraRange = 3.0f,
                LeftScene = "Assets/_Game/Scenes/Locations/Loc_HarborCabin.unity",
                RightScene = "Assets/_Game/Scenes/Locations/Loc_Plaza.unity",
                NpcX = 3.2f,
            },
            new()
            {
                SceneName = "Loc_Plaza", Id = "plaza_dusk", DisplayName = "中央广场",
                ArtFile = "loc_plaza_dusk.png", GroundY = -3.02f, WalkMinX = -10.5f, WalkMaxX = 10.5f, CameraRange = 3.0f,
                LeftScene = "Assets/_Game/Scenes/Locations/Loc_TownStreet.unity",
                RightScene = "Assets/_Game/Scenes/Locations/Loc_Garden.unity",
            },
            new()
            {
                SceneName = "Loc_Garden", Id = "garden_dusk", DisplayName = "花园小屋",
                ArtFile = "loc_garden_dusk.png", GroundY = -3.02f, WalkMinX = -8.0f, WalkMaxX = 8.0f, CameraRange = 0f,
                LeftScene = "Assets/_Game/Scenes/Locations/Loc_Plaza.unity",
                RightScene = "Assets/_Game/Scenes/Locations/Loc_Cliff.unity",
            },
            new()
            {
                SceneName = "Loc_Cliff", Id = "cliff_dusk", DisplayName = "崖边小屋",
                ArtFile = "loc_cliff_dusk.png", GroundY = -3.02f, WalkMinX = -8.0f, WalkMaxX = 8.0f, CameraRange = 0f,
                LeftScene = "Assets/_Game/Scenes/Locations/Loc_Garden.unity",
                RightScene = "Assets/_Game/Scenes/Locations/Loc_Observatory.unity",
            },
            new()
            {
                SceneName = "Loc_Observatory", Id = "observatory_dusk", DisplayName = "天文台",
                ArtFile = "loc_observatory_dusk.png", GroundY = -3.02f, WalkMinX = -8.0f, WalkMaxX = 8.0f, CameraRange = 0f,
                LeftScene = "Assets/_Game/Scenes/Locations/Loc_Cliff.unity",
                RightScene = "Assets/_Game/Scenes/Locations/Loc_Lighthouse.unity",
            },
            new()
            {
                SceneName = "Loc_Lighthouse", Id = "lighthouse_dusk", DisplayName = "灯塔",
                ArtFile = "loc_lighthouse_dusk.png", GroundY = -3.02f, WalkMinX = -8.0f, WalkMaxX = 8.0f, CameraRange = 0f,
                LeftScene = "Assets/_Game/Scenes/Locations/Loc_Observatory.unity",
                RightScene = "Assets/_Game/Scenes/Locations/Loc_Viewpoint.unity",
            },
            new()
            {
                SceneName = "Loc_Viewpoint", Id = "viewpoint_dusk", DisplayName = "山顶观景台",
                ArtFile = "loc_viewpoint_dusk.png", GroundY = -3.02f, WalkMinX = -8.0f, WalkMaxX = 8.0f, CameraRange = 0f,
                LeftScene = "Assets/_Game/Scenes/Locations/Loc_Lighthouse.unity",
            },
            new()
            {
                SceneName = "Loc_Forest", Id = "forest_dusk", DisplayName = "林间小径",
                ArtFile = "loc_forest_dusk.png", GroundY = -3.02f, WalkMinX = -8.0f, WalkMaxX = 8.0f, CameraRange = 0f,
            },
            new()
            {
                SceneName = "Loc_Cafe", Id = "cafe_dusk", DisplayName = "海上咖啡馆",
                ArtFile = "loc_cafe_dusk.png", GroundY = -3.02f, WalkMinX = -8.0f, WalkMaxX = 8.0f, CameraRange = 0f,
            },
        };

        [MenuItem("Z From Moon/Build World Locations")]
        public static void Build()
        {
            LocationRouteRepair.RequireEditableScenes();
            EnsureFolder(SceneDir);
            EnsureRoot();
            ImportArt();

            var app = EditorSceneManager.OpenScene(AppScenePath, OpenSceneMode.Single);
            PatchApp(app);
            EditorSceneManager.MarkSceneDirty(app);
            EditorSceneManager.SaveScene(app);

            foreach (var spec in Specs) BuildLocation(spec);
            AddToBuildSettings();

            AssetDatabase.SaveAssets();
            Debug.Log($"[WorldBuilder] 完成：{Specs.Length} 个地点已生成，App 路由已接线。");
        }

        private static void EnsureRoot()
        {
            var dir = new DirectoryInfo(SceneDir);
            if (!dir.Exists) Directory.CreateDirectory(SceneDir);
        }

        private static void ImportArt()
        {
            foreach (var file in Directory.GetFiles(ArtDir))
            {
                if (!file.EndsWith(".png") && !file.EndsWith(".jpg")) continue;
                SetSpriteImport(file, SpriteAlignment.Center);
            }
            SetSpriteImport(ShadowPath, SpriteAlignment.Center);
            AssetDatabase.Refresh();
        }

        private static void SetSpriteImport(string path, SpriteAlignment alignment)
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
            importer.maxTextureSize = 8192;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)alignment;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static void PatchApp(Scene app)
        {
            var session = LocationRouteRepair.FindSingle<GameSession>(app);
            var so = new SerializedObject(session);
            so.FindProperty("initialScenePath").stringValue = $"{SceneDir}/Loc_Dock.unity";
            so.ApplyModifiedPropertiesWithoutUndo();
            LocationRouteRepair.WireAppRouter(app);
        }

        private static void BuildLocation(LocationSpec spec)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject(spec.SceneName);

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtDir}/{spec.ArtFile}");
            if (sprite == null)
            {
                Debug.LogError($"[WorldBuilder] 缺少背景 {spec.ArtFile}");
                return;
            }
            float unitHeight = sprite.rect.height / sprite.pixelsPerUnit;
            float unitWidth = sprite.rect.width / sprite.pixelsPerUnit;
            float scale = ViewHeight / unitHeight;
            float halfWidth = unitWidth * scale * 0.5f;

            var background = new GameObject("Background");
            background.transform.SetParent(root.transform, false);
            var renderer = background.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 0;
            background.transform.localScale = new Vector3(scale, scale, 1f);

            var spawnStart = CreateEmpty(root.transform, "Entry_Start", new Vector3(spec.WalkMinX + 3f, spec.GroundY, 0f));
            var routes = new List<(string id, Transform anchor)> { ("start", spawnStart) };
            var left = FindSpec(spec.LeftScene);
            var right = FindSpec(spec.RightScene);
            if (left != null)
                routes.Add((LocationRouteRepair.EntryFrom(left.Id),
                    CreateEmpty(root.transform, "Entry_Left", new Vector3(spec.WalkMinX + 2.2f, spec.GroundY, 0f))));
            if (right != null)
                routes.Add((LocationRouteRepair.EntryFrom(right.Id),
                    CreateEmpty(root.transform, "Entry_Right", new Vector3(spec.WalkMaxX - 2.2f, spec.GroundY, 0f))));

            var context = root.AddComponent<SceneContext>();
            var so = new SerializedObject(context);
            so.FindProperty("locationId").stringValue = spec.Id;
            so.FindProperty("displayName").stringValue = spec.DisplayName;
            so.FindProperty("entryId").stringValue = "start";
            so.FindProperty("initialSpawn").objectReferenceValue = spawnStart;
            so.FindProperty("cameraBoundsX").vector2Value = new Vector2(-spec.CameraRange, spec.CameraRange);
            var entries = so.FindProperty("entries");
            entries.arraySize = routes.Count;
            for (int i = 0; i < routes.Count; i++)
            {
                entries.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue = routes[i].id;
                entries.GetArrayElementAtIndex(i).FindPropertyRelative("anchor").objectReferenceValue = routes[i].anchor;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            CreateCollision(root.transform, spec);
            if (left != null)
                CreateExit(root.transform, "Exit_Left", spec.WalkMinX + 0.6f, spec.GroundY, spec.LeftScene, spec.Id, left.DisplayName, true);
            if (right != null)
                CreateExit(root.transform, "Exit_Right", spec.WalkMaxX - 0.6f, spec.GroundY, spec.RightScene, spec.Id, right.DisplayName, false);
            if (spec.NpcX != 0f) CreateIslander(root.transform, spec.NpcX, spec.GroundY);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, $"{SceneDir}/{spec.SceneName}.unity");
        }

        private static LocationSpec FindSpec(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return null;
            foreach (var spec in Specs)
                if ($"{SceneDir}/{spec.SceneName}.unity" == scenePath) return spec;
            throw new System.InvalidOperationException($"Unknown location route: {scenePath}");
        }

        private static Transform CreateEmpty(Transform parent, string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            return go.transform;
        }

        private static void CreateCollision(Transform parent, LocationSpec spec)
        {
            var collision = new GameObject("Collision").transform;
            collision.SetParent(parent, false);
            float width = spec.WalkMaxX - spec.WalkMinX;
            CreateBox(collision, "Ground", new Vector2((spec.WalkMinX + spec.WalkMaxX) * 0.5f, spec.GroundY - 0.5f), new Vector2(width + 6f, 1f), false);
            CreateBox(collision, "LeftWall", new Vector2(spec.WalkMinX - 0.3f, spec.GroundY + 8f), new Vector2(1f, 20f), false);
            CreateBox(collision, "RightWall", new Vector2(spec.WalkMaxX + 0.3f, spec.GroundY + 8f), new Vector2(1f, 20f), false);
        }

        private static void CreateExit(Transform parent, string name, float x, float groundY, string targetScene, string sourceId, string targetDisplayName, bool isLeft)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, groundY + 2.2f, 0f);
            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(1.6f, 6f);
            var exit = go.AddComponent<SceneExit>();
            exit.Configure(targetScene, LocationRouteRepair.EntryFrom(sourceId));
            LocationRouteRepair.ConfigureExitInteraction(exit, sourceId, targetDisplayName, isLeft, groundY);

            var marker = new GameObject("Marker");
            marker.transform.SetParent(go.transform, false);
            marker.transform.localScale = new Vector3(0.12f, 2.4f, 1f);
            var renderer = marker.AddComponent<SpriteRenderer>();
            renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ShadowPath);
            renderer.color = new Color(1f, 0.85f, 0.4f, 0.25f);
            renderer.sortingOrder = 25;
        }

        private static void CreateBox(Transform parent, string name, Vector2 position, Vector2 size, bool trigger)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.layer = 9;
            var box = go.AddComponent<BoxCollider2D>();
            box.size = size;
            box.isTrigger = trigger;
        }

        private static void CreateIslander(Transform parent, float x, float groundY)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ZSpritePath);
            if (sprite == null) return;

            var go = new GameObject("Islander");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, groundY, 0f);
            go.layer = 10;

            var visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, false);
            float scale = 3.3f / (sprite.rect.height / Ppu);
            visual.transform.localScale = new Vector3(-scale, scale, 1f);
            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = CharOrder;
            visual.AddComponent<SpriteBob>().Configure(0.014f, 1.1f, 1.7f);
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ZControllerPath);
            if (controller != null)
            {
                var animator = visual.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
            }
            AddShadow(go.transform);

            var trigger = go.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(1.0f, 3.4f);
            trigger.offset = new Vector2(0f, 1.7f);

            var interactable = go.AddComponent<Interactable>();
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
            so.FindProperty("interactionEnabled").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            var actorSo = new SerializedObject(actor);
            actorSo.FindProperty("definition").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<DialogueDefinition>("Assets/_Game/Data/Dialogue/IslanderDialogue.asset");
            actorSo.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddShadow(Transform parent)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ShadowPath);
            if (sprite == null) return;
            var go = new GameObject("Shadow");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            go.transform.localScale = new Vector3(0.85f, 0.5f, 1f);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = CharOrder - 1;
            renderer.color = new Color(1f, 1f, 1f, 0.4f);
        }

        private static void AddToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var spec in Specs)
            {
                string path = $"{SceneDir}/{spec.SceneName}.unity";
                bool exists = false;
                foreach (var entry in scenes) if (entry.path == path) exists = true;
                if (!exists) scenes.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}