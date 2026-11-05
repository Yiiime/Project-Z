using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Seabreeze.Candidates.GaitRefinement.Editor
{
    public static class CandidatePreviewBuilder
    {
        const string Root = "Assets/Seabreeze/Candidates/SkeletalPlayerGaitRefinement";
        const string BaselineRoot = "Assets/Seabreeze/Candidates/SkeletalPlayerTrailingSupport";
        public const string ScenePath = Root + "/GaitRefinementComparison.unity";

        [MenuItem("Seabreeze/Candidates/Gait Refinement/3. Build Comparison Scene")]
        public static void BuildPreview()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Leave Play Mode before authoring the candidate scene.");
            var previous = SceneManager.GetActiveScene();
            if (previous.isDirty) throw new System.InvalidOperationException("Save the current scene before rebuilding the candidate.");
            string previousPath = previous.path;
            // Build in isolation: additive global 2D lights conflict across preview scenes.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            try
            {
                var camera = new GameObject("Comparison camera").AddComponent<Camera>();
                camera.tag = "MainCamera"; camera.orthographic = true; camera.orthographicSize = 3.1f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.50f,.61f,.67f);
                camera.transform.position = new Vector3(-.2f,1.8f,-10);
                camera.GetUniversalAdditionalCameraData();
                var light = new GameObject("Preview daylight").AddComponent<Light2D>();
                light.lightType = Light2D.LightType.Global; light.intensity = 1;

                // A familiar scene reference, without bootstrapping the game or its save lifecycle.
                var background = Resources.Load<Sprite>("Art/street");
                if (background)
                {
                    var backdrop = new GameObject("Existing street art (preview only)").AddComponent<SpriteRenderer>();
                    backdrop.sprite = background; backdrop.sortingOrder = -100;
                    backdrop.color = new Color(.64f,.72f,.78f,1);
                    float scale = 42f / background.bounds.size.x;
                    backdrop.transform.localScale = Vector3.one * scale;
                    backdrop.transform.position = new Vector3(1,background.bounds.size.y*scale*.335f,2);
                }
                var floor = new GameObject("Preview ground - layer 8"); floor.layer = 8;
                floor.transform.position = new Vector3(0,-.25f,0);
                floor.AddComponent<BoxCollider2D>().size = new Vector2(100,.5f);
                var square = WhiteSprite();
                Bar("Ground baseline",new Vector2(0,-.025f),new Vector2(100,.05f),new Color(.78f,.87f,.88f),square,0);
                for(int i=-80;i<=80;i++)
                    Bar("Half-unit ground mark " + i,new Vector2(i*.5f,-.12f),new Vector2(.014f,i%2==0?.22f:.1f),new Color(.65f,.80f,.81f,.8f),square,0);
                Wall("Left preview wall",-30); Wall("Right preview wall",30);

                var physical = new GameObject("Candidate motion root (independent of Player)");
                physical.transform.position = new Vector3(-3,.02f,0);
                var collider = physical.AddComponent<CapsuleCollider2D>(); collider.size = new Vector2(.5f,2.35f);collider.offset = new Vector2(0,1.2f);
                var original = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Seabreeze/Resources/Prefabs/Player.prefab");
                if(original && original.GetComponent<CapsuleCollider2D>()) collider.sharedMaterial=original.GetComponent<CapsuleCollider2D>().sharedMaterial;
                physical.AddComponent<Rigidbody2D>();
                var motor = physical.AddComponent<CandidatePreviewMotor>();
                if(original && original.GetComponent<PlayerController>())
                {
                    var source = original.GetComponent<PlayerController>();
                    motor.acceleration=source.acceleration;
                    motor.deceleration=source.deceleration;motor.accelerationRamp=source.accelerationRamp;
                }
                motor.speed=1.65f;motor.autoWalk=true;motor.autoChangeSeconds=3f;motor.autoRestSeconds=1f;
                var candidate = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/PlayerRigCandidate.prefab"),scene);
                candidate.transform.SetParent(physical.transform,false);
                motor.driver=candidate.GetComponent<RigAnimatorDriver>();
                candidate.name="Gait refinement candidate (sustained extension)";
                var baseline = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BaselineRoot+"/Prefabs/PlayerRigCandidate.prefab"),scene);
                baseline.name="Trailing support baseline (unchanged)";
                baseline.transform.SetParent(physical.transform,false);
                baseline.transform.localPosition=Vector3.right*5.6f;
                motor.baselineDriver=baseline.GetComponent<Seabreeze.Candidates.TrailingSupport.RigAnimatorDriver>();
                // Both prefab roots keep scale 1. Their shared physical parent supplies identical motion.
                var comparison = new GameObject("Comparison presentation").AddComponent<CandidateComparisonView>();
                comparison.motor=motor;comparison.previewCamera=camera;
                comparison.cameraOffset=new Vector3(2.8f,1.8f,-10);

                var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root+"/Preview/PreviewFont.asset");
                WorldLabel("新版 · 后伸保持",physical.transform,new Vector2(0,3.55f),font,new Color(.81f,.98f,.99f));
                WorldLabel("基准 · 后侧支撑",physical.transform,new Vector2(5.6f,3.55f),font,new Color(1,.88f,.67f));
                var canvas=new GameObject("Candidate preview notes",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
                var previewCanvas=canvas.GetComponent<Canvas>();
                previewCanvas.renderMode=RenderMode.ScreenSpaceCamera;previewCanvas.worldCamera=camera;previewCanvas.planeDistance=5;
                var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
                var title=ScreenLabel(canvas.transform,"骨骼步态对比 · 同屏同尺度 / 相同实际位移 · 正式 Player 未替换",font,30);
                title.rectTransform.anchorMin=new Vector2(0,1);title.rectTransform.anchorMax=new Vector2(1,1);title.rectTransform.pivot=new Vector2(.5f,1);
                title.rectTransform.anchoredPosition=new Vector2(0,-28);title.rectTransform.sizeDelta=new Vector2(-80,50);
                var readout=ScreenLabel(canvas.transform,"",font,25);
                readout.rectTransform.anchorMin=new Vector2(0,0);readout.rectTransform.anchorMax=new Vector2(1,0);readout.rectTransform.pivot=new Vector2(.5f,0);
                readout.rectTransform.anchoredPosition=new Vector2(0,26);readout.rectTransform.sizeDelta=new Vector2(-80,90);
                comparison.readout=readout;
                Directory.CreateDirectory(Root);
                EditorSceneManager.SaveScene(scene,ScenePath);
            }
            finally
            {
                if (!string.IsNullOrEmpty(previousPath) && previousPath != ScenePath)
                    EditorSceneManager.OpenScene(previousPath);
            }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        }
        static void Wall(string name,float x)
        {
            var wall=new GameObject(name);wall.layer=8;wall.transform.position=new Vector3(x,3,0);wall.AddComponent<BoxCollider2D>().size=new Vector2(.5f,6);
        }
        static Sprite WhiteSprite()
        {
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Seabreeze/Candidates/SkeletalPlayer/Art/preview-rule.png");
            if(!sprite) throw new System.InvalidOperationException("Missing shared baseline ruler sprite.");
            return sprite;
        }
        static void Bar(string name,Vector2 position,Vector2 size,Color color,Sprite sprite,int order)
        {
            var bar=new GameObject(name).AddComponent<SpriteRenderer>();bar.sprite=sprite;bar.color=color;bar.sortingOrder=order;
            bar.transform.position=position;bar.transform.localScale=new Vector3(size.x,size.y,1);
        }
        static void WorldLabel(string text,Transform parent,Vector2 position,TMP_FontAsset font,Color color)
        {
            var label=new GameObject(text).AddComponent<TextMeshPro>();label.transform.SetParent(parent,false);label.transform.localPosition=new Vector3(position.x,position.y,-1);
            label.font=font;label.text=text;label.fontSize=3.1f;label.alignment=TextAlignmentOptions.Center;label.color=color;
            label.rectTransform.sizeDelta=new Vector2(4,.55f);label.GetComponent<MeshRenderer>().sortingOrder=100;
        }
        static TextMeshProUGUI ScreenLabel(Transform parent,string text,TMP_FontAsset font,float size)
        {
            var label=new GameObject("Note",typeof(RectTransform)).AddComponent<TextMeshProUGUI>();label.transform.SetParent(parent,false);
            label.font=font;label.text=text;label.fontSize=size;label.color=new Color(.92f,.96f,.98f);label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;
            return label;
        }
        [MenuItem("Seabreeze/Candidates/Gait Refinement/Open Comparison")]
        public static void OpenPreview()
        {
            if(EditorApplication.isPlaying) throw new System.InvalidOperationException("Leave Play Mode first.");
            if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }
        const string SavedStartScene = "Seabreeze.GaitRefinementCandidate.PreviousStartScene";
        const string PreviewPending = "Seabreeze.GaitRefinementCandidate.RestoreStartScene";
        [InitializeOnLoadMethod]
        static void InstallPreviewLifecycle()
        {
            EditorApplication.playModeStateChanged -= RestoreStartScene;
            EditorApplication.playModeStateChanged += RestoreStartScene;
        }
        static void RestoreStartScene(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(PreviewPending,false)) return;
            string path=SessionState.GetString(SavedStartScene,"");
            EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(path)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            SessionState.EraseBool(PreviewPending);SessionState.EraseString(SavedStartScene);
        }
        [MenuItem("Seabreeze/Candidates/Gait Refinement/Play Comparison")]
        public static void PlayPreview()
        {
            if(EditorApplication.isPlaying) throw new System.InvalidOperationException("Leave Play Mode first.");
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            var previous=EditorSceneManager.playModeStartScene;
            SessionState.SetString(SavedStartScene,previous?AssetDatabase.GetAssetPath(previous):"");
            SessionState.SetBool(PreviewPending,true);
            EditorSceneManager.OpenScene(ScenePath);
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            EditorApplication.isPlaying=true;
        }
    }
}
