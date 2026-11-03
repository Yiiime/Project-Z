using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZFromMoon.Player;

namespace ZFromMoon.EditorTools
{
    /// <summary>
    /// 一键搭建码头视差 demo：
    /// 1) 导入并铺好 11 张码头分层（视差 + sortingOrder）；
    /// 2) 把 Z 走路表(2x4) 自动切成 8 帧，生成 Idle/Walk clip 与 Animator；
    /// 3) 生成 ZPlayer prefab（PlayerMotor + PlayerView + Demo 输入）；
    /// 4) 在场景中放好玩家（脚底线）与跟随相机。
    /// 菜单：Z From Moon / Build Dock Parallax Demo
    /// </summary>
    public static class DockParallaxBuilder
    {
        private const string DockDir = "Assets/_Game/Art/Locations/dock";
        private const string ZArtDir = "Assets/_Game/Art/Characters/Z";
        private const string ZSheetPath = "Assets/_Game/Art/Characters/Z/z_walk_sheet.png";
        private const string AnimDir = "Assets/_Game/Animations/Z";
        private const string PrefabDir = "Assets/_Game/Prefabs/Player";
        private const string ScenePath = "Assets/_Game/Scenes/DockParallax.unity";

        private const float Ppu = 100f;
        private const float DesignHeight = 10.8f;
        private const float DesignWidth = 38.4f;
        private const int Cols = 4;
        private const int Rows = 2;

        // 栈道木板上沿（=角色脚底线）的世界 y，进场景可按洋红校准条微调。
        private const float GroundY = -2.4f;

        [MenuItem("Z From Moon/Build Dock Parallax Demo")]
        public static void Build()
        {
            // ---- 1. 角色资产：切片 + clip + animator + prefab ----
            var walkSprites = SliceZSheet();
            var animator = BuildAnimator(walkSprites);
            BuildZPlayerPrefab(animator, walkSprites);

            // ---- 2. 场景 ----
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("DockParallax");

            foreach (var layer in LayerDefs)
            {
                var sprite = LoadOrImportSprite(Path.Combine(DockDir, layer.File));
                if (sprite == null)
                {
                    Debug.LogWarning($"[DockBuilder] 缺少图片：{layer.File}，跳过 {layer.Name}");
                    continue;
                }
                var go = new GameObject(layer.Name);
                go.transform.SetParent(root.transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = layer.OrderInLayer;
                go.transform.localPosition = new Vector3(0f, layer.Y, layer.Z);
                go.AddComponent<ParallaxPlaceholder>().factor = layer.Parallax;
            }

            // ---- 3. 玩家 ----
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path.Combine(PrefabDir, "ZPlayer.prefab"));
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            player.name = "ZPlayer";
            player.transform.position = new Vector3(0f, GroundY, 0f);

            // ---- 4. 相机 + 跟随 ----
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = camGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = DesignHeight / 2f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.09f, 0.23f);
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            var follow = camGo.AddComponent<DemoCameraFollow>();
            follow.Configure(player.transform, 0f, -10f, 0f);

            // ---- 5. 脚底线校准条（洋红）----
            var lineGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            lineGo.name = "GroundAnchor_Y_calibrate_me";
            Object.DestroyImmediate(lineGo.GetComponent<Collider>());
            lineGo.transform.localScale = new Vector3(DesignWidth, 0.06f, 1f);
            lineGo.transform.position = new Vector3(0f, GroundY, 5f);
            var lineMat = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0f, 1f, 0.8f) };
            lineGo.GetComponent<MeshRenderer>().sharedMaterial = lineMat;

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log("[DockBuilder] 完成：已切 8 帧 Z 走路、生成 ZPlayer、并生成场景 " + ScenePath +
                      "\n打开场景按 Play，A/D 移动：Z 在栈桥上走、背景视差、相机跟随。洋红条=脚底线(可微调)。");
        }

        // ---------------- 角色切片（不依赖 PSD Importer 数据接口：直接切出 8 张 PNG） ----------------
        private static List<Sprite> SliceZSheet()
        {
            EnsureFolder(AnimDir);
            string path = ZSheetPath;

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError("[DockBuilder] 找不到 Z 走路表：" + path);
                return new List<Sprite>();
            }
            bool needRead = !importer.isReadable;
            if (importer.textureType != TextureImporterType.Sprite) importer.textureType = TextureImporterType.Sprite;
            if (importer.spriteImportMode != SpriteImportMode.Single) importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = Ppu;
            importer.mipmapEnabled = false;
            importer.isReadable = true;
            importer.SaveAndReimport();

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            int cellW = tex.width / Cols, cellH = tex.height / Rows;

            // 1) 从磁盘原始像素切图（像素坐标：行0=上方）
            var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            raw.LoadImage(File.ReadAllBytes(path));
            var frames = new List<Sprite>(Cols * Rows);
            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Cols; c++)
                {
                    int i = r * Cols + c;
                    int px = c * cellW;
                    int py = raw.height - (r + 1) * cellH; // 像素坐标原点在左下
                    var cells = new Texture2D(cellW, cellH, TextureFormat.RGBA32, false);
                    var pix = raw.GetPixels(px, py, cellW, cellH);
                    cells.SetPixels(pix);
                    cells.Apply();
                    byte[] png = cells.EncodeToPNG();
                    Object.DestroyImmediate(cells);

                    string outPath = $"{ZArtDir}/z_walk_{i:00}.png";
                    File.WriteAllBytes(outPath, png);
                }
            }
            Object.DestroyImmediate(raw);
            AssetDatabase.Refresh();

            // 2) 统一导入设置：单 Sprite、底部居中 pivot、PPU=100、关 mipmap
            for (int i = 0; i < Cols * Rows; i++)
            {
                string p = $"{ZArtDir}/z_walk_{i:00}.png";
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = Ppu;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                var settings = new TextureImporterSettings();
                ti.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
                settings.spritePivot = new Vector2(0.5f, 0f);
                ti.SetTextureSettings(settings);
                ti.SaveAndReimport();
            }

            // 还原原始走路表的可读设置
            if (needRead)
            {
                var src = AssetImporter.GetAtPath(path) as TextureImporter;
                src.isReadable = false;
                src.SaveAndReimport();
            }

            for (int i = 0; i < Cols * Rows; i++)
                if (AssetDatabase.LoadAssetAtPath<Sprite>($"{ZArtDir}/z_walk_{i:00}.png") is Sprite sp)
                    frames.Add(sp);

            Debug.Log($"[DockBuilder] Z 走路表切出 {frames.Count}/{Cols * Rows} 帧（cell {cellW}x{cellH}，BottomCenter，PPU100）");
            return frames;
        }

        // ---------------- 动画 ----------------
        private static AnimatorController BuildAnimator(List<Sprite> frames)
        {
            EnsureFolder(AnimDir);
            string ctrlPath = Path.Combine(AnimDir, "ZWalkController.controller").Replace('\\', '/');
            var ac = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);

            var sm = ac.layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            var walk = sm.AddState("Walk");
            sm.defaultState = idle;

            idle.motion = MakeFrameClip(frames, new[] { 0 }, "z_idle", true);
            walk.motion = MakeFrameClip(frames, new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, "z_walk", true);

            var toWalk = idle.AddTransition(walk);
            toWalk.hasExitTime = false;
            toWalk.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");
            var toIdle = walk.AddTransition(idle);
            toIdle.hasExitTime = false;
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");

            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
        }

        private static AnimationClip MakeFrameClip(List<Sprite> frames, int[] indices, string clipName, bool loop)
        {
            string p = Path.Combine(AnimDir, clipName + ".anim").Replace('\\', '/');
            var clip = new AnimationClip { frameRate = 12f };
            var s = AnimationUtility.GetAnimationClipSettings(clip);
            s.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, s);

            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = "",
                propertyName = "m_Sprite",
            };
            var kf = new ObjectReferenceKeyframe[indices.Length];
            for (int i = 0; i < indices.Length; i++)
                kf[i] = new ObjectReferenceKeyframe { time = i / 12f, value = frames[indices[i]] };
            AnimationUtility.SetObjectReferenceCurve(clip, binding, kf);

            AssetDatabase.CreateAsset(clip, p);
            return clip;
        }

        // ---------------- ZPlayer prefab ----------------
        private static void BuildZPlayerPrefab(AnimatorController ac, List<Sprite> frames)
        {
            EnsureFolder(PrefabDir);

            var visualGo = new GameObject("Visual");
            var sr = visualGo.AddComponent<SpriteRenderer>();
            if (frames != null && frames.Count > 0) sr.sprite = frames[0];
            sr.sortingOrder = 15; // props(7) 之上、foreground(20) 之下
            var animator = visualGo.AddComponent<Animator>();
            animator.runtimeAnimatorController = ac;

            var root = new GameObject("ZPlayer");
            var body = root.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            var col = root.AddComponent<CapsuleCollider2D>();
            col.size = new Vector2(1.1f, 3.6f);
            col.direction = CapsuleDirection2D.Vertical;
            var motor = root.AddComponent<PlayerMotor>();
            root.AddComponent<DemoPlayerInput>();
            var view = root.AddComponent<PlayerView>();

            visualGo.transform.SetParent(root.transform, false);
            float frameWorldH = (2625f / Rows) / Ppu; // 单帧高 13.125 单位
            visualGo.transform.localPosition = new Vector3(0f, frameWorldH / 2f * 0.32f, 0f);
            visualGo.transform.localScale = Vector3.one * 0.32f;

            SetPrivate(view, "motor", motor);
            SetPrivate(view, "visualRoot", visualGo.transform);
            SetPrivate(view, "animator", animator);

            string prefabPath = Path.Combine(PrefabDir, "ZPlayer.prefab").Replace('\\', '/');
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
        }

        private static Sprite LoadOrImportSprite(string path)
        {
            path = path.Replace('\\', '/');
            if (!File.Exists(path)) return null;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            bool dirty = false;
            if (importer != null)
            {
                if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; dirty = true; }
                if (importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; dirty = true; }
                if (Mathf.Abs(importer.spritePixelsPerUnit - Ppu) > 0.01f) { importer.spritePixelsPerUnit = Ppu; dirty = true; }
                if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
                if (dirty) { importer.SaveAndReimport(); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); }
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
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

        private static void SetPrivate(object obj, string field, object value)
        {
            var fi = obj.GetType().GetField(field,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            fi?.SetValue(obj, value);
        }

        private struct LayerDef
        {
            public string Name, File;
            public int OrderInLayer;
            public float Y, Z, Parallax;
        }

        private static readonly LayerDef[] LayerDefs =
        {
            new(){ Name="01_sky",        File="loc_dock_sky.png",        OrderInLayer=0,  Y=2.36f, Z=2f,   Parallax=0.10f },
            new(){ Name="02_cloud_far",  File="loc_dock_cloud_far.png",  OrderInLayer=1,  Y=2.0f,  Z=1.5f, Parallax=0.20f },
            new(){ Name="03_cloud_near", File="loc_dock_cloud_near.png", OrderInLayer=2,  Y=1.2f,  Z=1.2f, Parallax=0.35f },
            new(){ Name="04_island_far", File="loc_dock_island_far.png", OrderInLayer=3,  Y=0.2f,  Z=0.8f, Parallax=0.50f },
            new(){ Name="05_sea",        File="loc_dock_sea.png",        OrderInLayer=4,  Y=-1.4f, Z=0.5f, Parallax=0.60f },
            new(){ Name="06_house",      File="loc_dock_house.png",      OrderInLayer=5,  Y=0f,    Z=0.2f, Parallax=0.80f },
            new(){ Name="07_boat",       File="loc_dock_boat.png",       OrderInLayer=6,  Y=0f,    Z=0.15f,Parallax=0.80f },
            new(){ Name="08_props",      File="loc_dock_dock_props.png", OrderInLayer=7,  Y=-0.6f, Z=0.05f,Parallax=0.90f },
            new(){ Name="09_ground",     File="loc_dock_ground.png",     OrderInLayer=10, Y=-3.2f, Z=0f,    Parallax=1.00f },
            new(){ Name="10_foreground", File="loc_dock_foreground.png", OrderInLayer=20, Y=-2.6f, Z=-0.2f,Parallax=1.10f },
            new(){ Name="11_fx_glow",    File="loc_dock_fx_glow.png",    OrderInLayer=30, Y=0f,    Z=-0.3f,Parallax=1.00f },
        };
    }
}
