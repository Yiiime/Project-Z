#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Seabreeze.Editor
{
    public static class ProjectBuilder
    {
        const string Root = "Assets/Seabreeze/";
        [MenuItem("Seabreeze/Rebuild authored scenes and assets")]
        public static void Build()
        {
            Directory.CreateDirectory(Root + "Resources/Prefabs");
            Directory.CreateDirectory(Root + "Resources/Fonts");
            Directory.CreateDirectory(Root + "Scenes");
            AssetDatabase.Refresh();
            ImportArt();
            SpriteSheetImporter.ImportAvailable();
            SetupFont();
            StoryContentBuilder.Build();
            SetupRenderPipeline();
            SetupPlayer();
            foreach (PlaceId p in Enum.GetValues(typeof(PlaceId)))
                BuildScene(p);
            EditorBuildSettings.scenes = Enum.GetValues(typeof(PlaceId)).Cast<PlaceId>().Select(p => new EditorBuildSettingsScene(Root + "Scenes/" + p + ".unity", true)).ToArray();
            PlayerSettings.companyName = "Seabreeze Studio";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.seabreezestudio.letter");
            PlayerSettings.SetArchitecture(UnityEditor.Build.NamedBuildTarget.Standalone, 2);
            PlayerSettings.productName = "海风捎来的信";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            var ps = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            ps.FindProperty("activeInputHandler").intValue = 1;
            ps.ApplyModifiedPropertiesWithoutUndo();
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            tags.FindProperty("layers").GetArrayElementAtIndex(8).stringValue = "Ground";
            tags.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(Root + "Scenes/Street.unity");
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Root + "Scenes/Street.unity");
            Debug.Log("SEABREEZE_BUILD_OK: 3 authored scenes, player prefab, URP2D, Chinese TMP, story assets.");
        }

        static void ImportArt()
        {
            foreach (string path in Directory.GetFiles(Root + "Resources/Art", "*.png"))
            {
                var t = AssetImporter.GetAtPath(path) as TextureImporter;
                if (t == null)
                    continue;
                t.textureType = TextureImporterType.Sprite;
                t.spriteImportMode = SpriteImportMode.Single;
                t.spritePixelsPerUnit = 100;
                t.mipmapEnabled = false;
                t.alphaIsTransparency = true;
                t.maxTextureSize = 4096;
                t.textureCompression = TextureImporterCompression.CompressedHQ;
                t.SaveAndReimport();
            }
        }

        static void SetupRenderPipeline()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/UniversalRP.asset");
            if (asset == null)
            {
                var renderer = ScriptableObject.CreateInstance<Renderer2DData>();
                AssetDatabase.CreateAsset(renderer, Root + "Settings/Renderer2D.asset");
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, Root + "Settings/Universal2D.asset");
            }

            GraphicsSettings.defaultRenderPipeline = asset;
            QualitySettings.renderPipeline = asset;
            asset.msaaSampleCount = 1;
            asset.renderScale = 1;
            EditorUtility.SetDirty(asset);
            string litPath = Root + "Resources/Art/SpriteLit.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(litPath) == null)
                AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default")), litPath);
        }

        static void SetupFont()
        {
            string path = Root + "Resources/Fonts/SeabreezeChinese.asset";
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (fontAsset != null)
                return;
            var font = AssetDatabase.LoadAssetAtPath<Font>(Root + "Resources/Fonts/NotoSansSC-Regular.ttf");
            if (font == null)
                throw new Exception("Chinese font missing");
            fontAsset = TMP_FontAsset.CreateFontAsset(font, 40, 5, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            fontAsset.name = "SeabreezeChinese";
            AssetDatabase.CreateAsset(fontAsset, path);
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            foreach (var texture in fontAsset.atlasTextures)
                AssetDatabase.AddObjectToAsset(texture, fontAsset);
            string chars = string.Concat(Directory.GetFiles(Root + "Scripts", "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText)) + string.Concat(Directory.GetFiles(Root + "Editor", "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
            fontAsset.TryAddCharacters(new string (chars.Distinct().ToArray()), out string missing);
            EditorUtility.SetDirty(fontAsset);
        }

        static void SetupPlayer()
        {
            var g = new GameObject("Player");
            var body = g.AddComponent<Rigidbody2D>();
            body.gravityScale = 2.5f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            var c = g.AddComponent<CapsuleCollider2D>();
            c.size = new Vector2(.5f, 2.35f);
            c.offset = new Vector2(0, 1.2f);
            var material = new PhysicsMaterial2D("No wall friction")
            {friction = 0, bounciness = 0};
            string mp = Root + "Settings/PlayerPhysics.physicsMaterial2D";
            if (AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(mp) == null)
                AssetDatabase.CreateAsset(material, mp);
            c.sharedMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(mp);
            var vis = new GameObject("Character sprite rig");
            vis.transform.SetParent(g.transform);
            vis.AddComponent<CharacterVisual>();
            g.AddComponent<PlayerVisualSetup>();
            g.AddComponent<PlayerController>();
            g.AddComponent<PlayerInteraction>();
            PrefabUtility.SaveAsPrefabAsset(g, Root + "Resources/Prefabs/Player.prefab");
            UnityEngine.Object.DestroyImmediate(g);
            var marker = new GameObject("Interactable");
            marker.AddComponent<Interactable>();
            PrefabUtility.SaveAsPrefabAsset(marker, Root + "Resources/Prefabs/Interactable.prefab");
            UnityEngine.Object.DestroyImmediate(marker);
        }

        static void BuildScene(PlaceId p)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Game lifecycle").AddComponent<GameBootstrap>();
            var root = new GameObject(p + " - authored 2D stage");
            var stage = root.AddComponent<WorldScene>();
            stage.place = p;
            stage.width = p == PlaceId.Cafe ? 20 : p == PlaceId.Lighthouse ? 28 : 32;
            stage.streetSpawn = p == PlaceId.Street ? -8 : p == PlaceId.Cafe ? -7 : -11.5f;
            stage.cameraSize = p == PlaceId.Cafe ? 5.6f : p == PlaceId.Lighthouse ? 7.875f : 6.5f;
            stage.cameraY = p == PlaceId.Cafe ? 3.75f : p == PlaceId.Lighthouse ? 5.28f : 4.25f;
            root.AddComponent<SceneAtmosphere>();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "Resources/Art/" + p.ToString().ToLower() + ".png");
            if (sprite == null) throw new FileNotFoundException("Missing required scene artwork: " + p);
            var bg = new GameObject("Hand-painted environment - replaceable artwork");
            bg.transform.SetParent(root.transform);
            stage.backdrop = bg.AddComponent<SpriteRenderer>();
            stage.backdrop.sprite = sprite;
            stage.backdrop.sortingOrder = -20;
            if (sprite != null)
            {
                float scale = stage.width / sprite.bounds.size.x;
                bg.transform.localScale = Vector3.one * scale;
                float h = sprite.bounds.size.y * scale;
                bg.transform.position = new Vector3(0, h * (.5f - stage.floorImageFraction), 0);
            }

            Collider("Continuous walkable pavement", new Vector2(0, -.45f), new Vector2(stage.width, .9f), root.transform);
            Collider("Left boundary", new Vector2(-stage.width / 2 - .2f, 5), new Vector2(.4f, 12), root.transform);
            Collider("Right boundary", new Vector2(stage.width / 2 + .2f, 5), new Vector2(.4f, 12), root.transform);
            if (p == PlaceId.Street)
            {
                Door(root, "cafe_door", "进入「潮汐」咖啡馆", -9.15f, PlaceId.Cafe);
                Door(root, "to_lighthouse", "沿海堤去灯塔", 15.0f, PlaceId.Lighthouse);
                Npc(root, "npc_mio", "Z", -2, 1, p);
                Npc(root, "npc_chen", "陈伯", 8, 3, p);
                Marker(root, "postcard", "长椅上的明信片", 13.1f);
                Marker(root, "noticeboard", "街角告示板", -13.2f);
                Marker(root, "bicycle", "旧自行车", -4);
                Marker(root, "sea", "听一会儿海", 3);
                Marker(root, "bench", "坐一会儿", 14.0f);
                Collider("Low bench platform", new Vector2(14.5f, .625f), new Vector2(3f, 1.25f), root.transform);
                var benchPlatform = root.transform.Find("Low bench platform");
                benchPlatform.GetComponent<BoxCollider2D>().usedByEffector = true;
                var oneWay = benchPlatform.gameObject.AddComponent<PlatformEffector2D>();
                oneWay.useOneWay = true;
                oneWay.surfaceArc = 150;
            }

            if (p == PlaceId.Cafe)
            {
                Door(root, "cafe_exit", "回到海边小街", -8.3f, PlaceId.Street);
                Npc(root, "npc_aya", "阿遥", 2.4f, 2, p);
                Marker(root, "radio", "旧收音机", -5.5f);
                Marker(root, "guestbook", "留言本", 4.6f);
                Marker(root, "coffee", "慢慢喝完咖啡", -3.7f);
            }

            if (p == PlaceId.Lighthouse)
            {
                Door(root, "lighthouse_exit", "回到海边小街", -13.2f, PlaceId.Street);
                Npc(root, "npc_mio", "Z", 2.6f, 1, p);
                Npc(root, "npc_aya", "阿遥", -.5f, 2, p);
                Npc(root, "npc_chen", "陈伯", -3, 3, p);
                Marker(root, "letterbox", "海堤信箱", 5.2f);
                Marker(root, "telescope", "望远镜", 10.5f);
                Marker(root, "lighthouse_bench", "等下一阵海风", -11.3f);
            }

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Root + "Scenes/" + p + ".unity");
        }

        static void Collider(string name, Vector2 point, Vector2 size, Transform parent)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent);
            g.transform.position = point;
            g.layer = 8;
            g.AddComponent<BoxCollider2D>().size = size;
        }

        static Interactable Marker(GameObject root, string id, string name, float x)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Resources/Prefabs/Interactable.prefab");
            var g = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            g.name = name + " [" + id + "]";
            g.transform.SetParent(root.transform);
            g.transform.position = new Vector3(x, 0, 0);
            var i = g.GetComponent<Interactable>();
            i.id = id;
            i.label = name;
            i.promptOffset = new Vector3(0, 2.2f, 0);
            i.radius = 1.45f;
            g.AddComponent<WorldProp>();
            return i;
        }

        static void Door(GameObject root, string id, string name, float x, PlaceId place)
        {
            var i = Marker(root, id, name, x);
            i.isDoor = true;
            i.destination = place;
            i.promptOffset = new Vector3(0, 3.2f, 0);
        }

        static void Npc(GameObject root, string id, string name, float x, int kind, PlaceId p)
        {
            var i = Marker(root, id, name, x);
            i.npc = true;
            i.radius = 1.9f;
            i.promptOffset = new Vector3(0, 3.3f, 0);
            var n = i.gameObject.AddComponent<NpcPresence>();
            n.npcId = id;
            n.character = kind;
            n.location = p;
        }

        [MenuItem("Seabreeze/Build macOS playable")]
        public static void BuildMac()
        {
            var result = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, "Builds/macOS/Seabreeze.app", BuildTarget.StandaloneOSX, BuildOptions.None);
            Directory.CreateDirectory("Docs/Verification");
            var report = new System.Text.StringBuilder();
            report.AppendLine("Result: " + result.summary.result);
            report.AppendLine("Unity: " + Application.unityVersion);
            report.AppendLine("Target: macOS Universal (Intel x64 + Apple silicon)");
            report.AppendLine("Output: " + result.summary.outputPath);
            report.AppendLine("Duration: " + result.summary.totalTime);
            report.AppendLine("Bytes: " + result.summary.totalSize);
            report.AppendLine("Errors: " + result.summary.totalErrors);
            report.AppendLine("Warnings: " + result.summary.totalWarnings);
            foreach (var step in result.steps)
                foreach (var message in step.messages)
                    if (message.type == LogType.Error || message.type == LogType.Exception || message.type == LogType.Warning)
                        report.AppendLine(message.type + ": " + message.content);
            File.WriteAllText("Docs/Verification/build-report.txt", report.ToString());
            if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception("Build failed " + result.summary.result);
            Debug.Log("SEABREEZE_MAC_BUILD_OK");
        }
    }
}
#endif

