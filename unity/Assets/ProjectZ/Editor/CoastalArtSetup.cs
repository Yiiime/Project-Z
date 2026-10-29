using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using ProjectZ.Runtime;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProjectZ.Editor
{
    public static class CoastalArtSetup
    {
        const string Root = "Assets/ProjectZ/Art/";
        const string CatalogPath = "Assets/ProjectZ/Resources/Art/CoastalArtCatalog.asset";
        [Serializable] sealed class SourceMaterial { public string name; public float[] color; public string texture; }
        [Serializable] sealed class SourceBone { public string bone; public int node; }
        [Serializable] sealed class Metadata { public SourceMaterial[] materials; public SourceBone[] humanBones; public string[] nodes; }
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        public static void Ensure()
        {
            if (AssetDatabase.LoadAssetAtPath<CoastalArtCatalog>(CatalogPath) == null) Rebuild();
        }
        public static void Inspect()
        {
            foreach(var path in Directory.GetFiles(Root+"Prefabs","*.prefab"))
            {
                var obj=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                var bounds=new Bounds(obj.transform.position,Vector3.zero);
                foreach(var r in obj.GetComponentsInChildren<Renderer>())bounds.Encapsulate(r.bounds);
                Debug.Log("ART_BOUNDS "+obj.name+" size="+bounds.size+" scale="+obj.transform.localScale);
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }
        static Color Hex(string value) { ColorUtility.TryParseHtmlString(value, out var c); return c; }
        static Material Surface(string name, Color color, Texture texture = null)
        {
            if (cache.TryGetValue(name, out var found)) return found;
            var path = Root + "Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("ProjectZ/Illustrated Surface")); AssetDatabase.CreateAsset(m,path); }
            m.SetColor("_BaseColor",color); m.SetTexture("_BaseMap",texture);
            m.SetColor("_ShadeColor",new Color(.82f,.86f,1));
            m.SetFloat("_Softness",.10f); m.SetFloat("_UnlitBlend",.02f);
            m.SetFloat("_Smoothness",.22f);m.SetFloat("_Specular",.07f);m.SetFloat("_Toon",0);
            m.SetFloat("_ReceiveShadow",1);m.SetFloat("_OcclusionStrength",1);
            EditorUtility.SetDirty(m); cache.Add(name,m); return m;
        }
        static Material Special(string name,string shader)
        {
            var path=Root+"Materials/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(m,path);}return m;
        }
        static void ConfigureTextures()
        {
            foreach (var path in Directory.GetFiles(Root,"*.*",SearchOption.AllDirectories)
                .Where(p=>p.EndsWith(".png",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".jpg",StringComparison.OrdinalIgnoreCase)))
            {
                var t=(TextureImporter)AssetImporter.GetAtPath(path);
                if(t==null)continue;
                if(t.alphaIsTransparency && t.maxTextureSize==2048)continue;
                t.textureType=TextureImporterType.Default;t.sRGBTexture=true;t.alphaIsTransparency=true;
                t.maxTextureSize=2048;t.mipmapEnabled=true;t.textureCompression=TextureImporterCompression.CompressedHQ;
                t.SaveAndReimport();
            }
        }
        static GameObject Prefab(string path,Func<Material,Material> resolve,bool collision=false)
        {
            var original=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(original==null)throw new BuildFailedException("Missing art model: "+path);
            var instance=UnityEngine.Object.Instantiate(original);instance.name=Path.GetFileNameWithoutExtension(path);
            foreach(var r in instance.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterials=r.sharedMaterials.Select(resolve).ToArray();
                if(collision && r is MeshRenderer)
                    r.gameObject.AddComponent<MeshCollider>().sharedMesh=r.GetComponent<MeshFilter>().sharedMesh;
            }
            var bounds=new Bounds(instance.transform.position,Vector3.zero);
            foreach(var r in instance.GetComponentsInChildren<Renderer>())bounds.Encapsulate(r.bounds);
            Debug.Log("ART_MODEL "+instance.name+" size="+bounds.size);
            var saved=PrefabUtility.SaveAsPrefabAsset(instance,Root+"Prefabs/"+instance.name+".prefab");
            UnityEngine.Object.DestroyImmediate(instance);return saved;
        }
        static Material NatureMaterial(Material source)
        {
            var name=source.name;var lower=name.ToLowerInvariant();string texture;
            if(lower.Contains("bark"))texture="Bark_NormalTree";
            else if(lower.Contains("twisted"))texture="Leaves_TwistedTree_C";
            else if(lower.Contains("normaltree"))texture="Leaves_NormalTree_C";
            else if(lower.Contains("leave"))texture="Leaves";
            else if(lower.Contains("flower"))texture="Flowers";
            else if(lower.Contains("path"))texture="PathRocks_Diffuse";
            else if(lower.Contains("rock"))texture="Rocks_Diffuse";
            else texture="Grass";
            var m=Surface("Nature_"+name,Color.white,AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Nature/Textures/"+texture+".png"));
            var vegetation=texture=="Grass"||texture=="Flowers"||texture.StartsWith("Leaves");
            m.SetFloat("_AlphaClip",vegetation?1:0);m.SetFloat("_Cutoff",.30f);m.SetFloat("_Wind",vegetation?.055f:0);
            if(texture.StartsWith("Leaves"))
            {m.SetFloat("_Recolor",.88f);m.SetColor("_BaseColor",Hex("#82996D"));}
            if(texture=="Flowers"){m.SetFloat("_Recolor",.55f);m.SetColor("_BaseColor",Hex("#D8C7DE"));}
            m.SetFloat("_UnlitBlend",vegetation?.12f:0);return m;
        }
        static GameObject[] Nature(params string[] names)=>names.Select(n=>Prefab(Root+"Nature/Models/"+n+".fbx",NatureMaterial)).ToArray();

        [MenuItem("Project Z/Art/Rebuild Coastal Art")]
        public static void Rebuild()
        {
            cache.Clear();Directory.CreateDirectory(Root+"Materials");Directory.CreateDirectory(Root+"Prefabs");
            Directory.CreateDirectory("Assets/ProjectZ/Resources/Art");AssetDatabase.Refresh();ConfigureTextures();
            var metadata=JsonUtility.FromJson<Metadata>(File.ReadAllText(Root+"Characters/Z/Z_metadata.json"));
            var modelPath=Root+"Characters/Z/Z_ArtSlice.fbx";
            var importer=(ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.importBlendShapes=true;importer.importAnimation=false;importer.isReadable=false;
            importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
            var description=importer.humanDescription;
            description.human=metadata.humanBones.Select(b=>new HumanBone {
                boneName=metadata.nodes[b.node],humanName=HumanTrait.BoneName[(int)Enum.Parse(typeof(HumanBodyBones),b.bone,true)],
                limit=new HumanLimit{useDefaultValues=true}}).ToArray();
            importer.humanDescription=description;importer.SaveAndReimport();
            Material ZMaterial(Material source)
            {
                var def=metadata.materials.FirstOrDefault(m=>m.name==source.name);
                if(def==null)throw new BuildFailedException("Unmapped Z material: "+source.name);
                var texture=string.IsNullOrEmpty(def.texture)?null:AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Characters/Z/Textures/"+def.texture);
                var color=texture!=null?Color.white:new Color(def.color[0],def.color[1],def.color[2],1);
                if(def.name=="Z_Cardigan_Knit")color=Hex("#A18EB9");
                if(def.name=="Z_Cardigan_Rib")color=Hex("#9983AC");
                if(def.name=="Z_Skirt_Floral")color=Hex("#273249");
                if(def.name=="Z_BareLeg")color=Hex("#EFBEA9");
                var m=Surface(def.name,color,texture);m.SetFloat("_AlphaClip",texture!=null?1:0);m.SetFloat("_Cutoff",.2f);
                m.SetFloat("_Toon",.72f);m.SetFloat("_Specular",.04f);
                m.SetFloat("_Pattern",def.name.EndsWith("Knit")?1:def.name.EndsWith("Floral")?2:def.name.EndsWith("Rib")?3:0);
                if(def.name=="Z_Cardigan_Knit" || def.name=="Z_Skirt_Floral")
                {
                    var knit=def.name=="Z_Cardigan_Knit";
                    m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"Characters/Z/Textures/"+(knit?"Z_Knit_v2.jpg":"Z_Floral_v2.jpg")));
                    m.SetTextureScale("_BaseMap",knit?new Vector2(3.5f,3):new Vector2(7,4));
                    m.SetFloat("_Pattern",0);m.SetFloat("_AlphaClip",0);
                    if(!knit)m.SetColor("_BaseColor",Color.white);
                }
                if(def.name.Contains("HAIR"))
                {m.SetFloat("_Recolor",1);m.SetColor("_BaseColor",Hex("#282630"));m.SetFloat("_Smoothness",.58f);m.SetFloat("_Specular",.16f);}
                if(def.name.Contains("Face")||def.name.Contains("Eye"))
                {m.SetFloat("_UnlitBlend",.45f);m.SetFloat("_Toon",0);m.SetFloat("_Softness",.20f);m.SetFloat("_Specular",0);
                    m.SetFloat("_ReceiveShadow",0);m.SetFloat("_OcclusionStrength",0);}
                if(def.name.Contains("SKIN"))m.SetColor("_BaseColor",Hex("#F2DFD5"));
                if(def.name.Contains("Silver")||def.name.Contains("Pearl"))
                {m.SetFloat("_Smoothness",.75f);m.SetFloat("_Specular",.55f);}
                return m;
            }
            var zModel=Prefab(modelPath,ZMaterial);
            var z=new GameObject("Z");var visual=(GameObject)PrefabUtility.InstantiatePrefab(zModel);visual.transform.SetParent(z.transform,false);
            // FBX imported from Blender +Y faces Unity -Z.
            visual.transform.localRotation=Quaternion.Euler(0,180,0);
            visual.transform.localScale=Vector3.one*1.09f;
            var animator=visual.GetComponentInChildren<Animator>();
            if(animator==null||animator.avatar==null||!animator.avatar.isValid||!animator.avatar.isHuman)
                throw new BuildFailedException("Z's humanoid avatar is not valid.");
            animator.enabled=false;
            var transforms=visual.GetComponentsInChildren<Transform>();
            Transform Bone(string n)=>transforms.First(t=>t.name==n);
            var performance=z.AddComponent<ZPerformance>();
            performance.Head=Bone("J_Bip_C_Head");performance.Chest=Bone("J_Bip_C_Chest");
            performance.RightArm=Bone("J_Bip_R_UpperArm");performance.LeftArm=Bone("J_Bip_L_UpperArm");
            performance.RightForearm=Bone("J_Bip_R_LowerArm");performance.LeftForearm=Bone("J_Bip_L_LowerArm");
            performance.RightHand=Bone("J_Bip_R_Hand");
            performance.Face=visual.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name=="Z_Face");
            performance.SourceAvatar=animator.avatar;
            var motion=z.AddComponent<Animator>();
            motion.runtimeAnimatorController=ZMotionAuthoring.Build(z);
            motion.applyRootMotion=false;motion.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            performance.MotionAnimator=motion;
            var zPrefab=PrefabUtility.SaveAsPrefabAsset(z,Root+"Prefabs/Z.prefab");UnityEngine.Object.DestroyImmediate(z);
            var catalog=AssetDatabase.LoadAssetAtPath<CoastalArtCatalog>(CatalogPath);
            if(catalog==null){catalog=ScriptableObject.CreateInstance<CoastalArtCatalog>();AssetDatabase.CreateAsset(catalog,CatalogPath);}
            catalog.Z=zPrefab;
            Material PropMaterial(Material source)
            {
                var palette=new Dictionary<string,string>{{"Coast_WarmWood","#9C7D63"},{"Coast_PaleWood","#C8AB82"},
                    {"Coast_Iron","#48676D"},{"Coast_Plaster","#E9E1CC"},{"Coast_Slate","#71909E"},{"Coast_Glass","#BDD7D3"}};
                var m=Surface(source.name,palette.TryGetValue(source.name,out var hex)?Hex(hex):source.color);
                if(source.name.Contains("Wood"))m.SetFloat("_Pattern",4);
                return m;
            }
            catalog.Bench=Prefab(Root+"Props/CoveBench.fbx",PropMaterial,true);
            catalog.Cottage=Prefab(Root+"Props/IslandCottage.fbx",PropMaterial,true);
            catalog.MapTable=Prefab(Root+"Props/MapTable.fbx",PropMaterial,true);
            catalog.Trees=Nature("CommonTree_1","CommonTree_3","CommonTree_4");
            catalog.Bushes=Nature("Bush_Common","Bush_Common_Flowers","Fern_1","Plant_1");
            catalog.Grasses=Nature("Grass_Common_Short","Grass_Wispy_Short");
            catalog.Flowers=Nature("Flower_3_Group","Flower_4_Group");
            catalog.Rocks=Nature("Rock_Medium_1","Rock_Medium_2","Rock_Medium_3","Pebble_Round_1","Pebble_Round_2");
            catalog.Ground=Surface("Coast_Ground",Color.white);catalog.Ground.SetFloat("_VertexColor",1);
            catalog.Path=Surface("Coast_Path",Hex("#CDBFA0"));
            catalog.Water=Special("Coast_Water","ProjectZ/Coastal Water");catalog.Sky=Special("Coast_Sky","ProjectZ/Coastal Sky");
            catalog.Water.SetColor("_Shallow",Hex("#83CBC3"));catalog.Water.SetColor("_Deep",Hex("#5898AA"));
            catalog.Water.SetColor("_Foam",Hex("#EAF3DF"));EditorUtility.SetDirty(catalog.Water);
            ConfigureRendering(catalog);
            EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
            foreach(var path in Directory.GetFiles(Root+"Shaders","*.shader"))
            {
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(path);
                if(ShaderUtil.ShaderHasError(shader))throw new BuildFailedException("Shader failed: "+path);
            }
            Debug.Log("PROJECT_Z_ART_PASSED: humanoid avatar, facial shapes, coastal prefabs and materials.");
        }
        static void ConfigureRendering(CoastalArtCatalog catalog)
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/ProjectZ/Settings/Z-URP.asset");
            pipeline.supportsHDR=true;pipeline.supportsCameraDepthTexture=true;pipeline.shadowDistance=65;
            pipeline.shadowCascadeCount=4;pipeline.mainLightShadowmapResolution=2048;
            EditorUtility.SetDirty(pipeline);
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/ProjectZ/Settings/Z-Renderer.asset");
            ResourceReloader.ReloadAllNullIn(renderer,UniversalRenderPipelineAsset.packagePath);
            var ao=renderer.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().FirstOrDefault();
            if(ao==null)
            {
                ao=ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();ao.name="Coastal contact shadows";
                AssetDatabase.AddObjectToAsset(ao,renderer);renderer.rendererFeatures.Add(ao);
            }
            var serialized=new SerializedObject(ao);var settings=serialized.FindProperty("m_Settings");
            settings.FindPropertyRelative("Source").enumValueIndex=0;
            settings.FindPropertyRelative("AfterOpaque").boolValue=false;
            settings.FindPropertyRelative("Intensity").floatValue=.7f;
            settings.FindPropertyRelative("Radius").floatValue=.16f;
            settings.FindPropertyRelative("Falloff").floatValue=45;
            serialized.ApplyModifiedPropertiesWithoutUndo();ao.SetActive(true);EditorUtility.SetDirty(renderer);
            const string path="Assets/ProjectZ/Settings/Coastal-Post.asset";
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if(profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,path);}
            if(!profile.TryGet<Tonemapping>(out var tone))tone=profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);
            if(!profile.TryGet<ColorAdjustments>(out var color))color=profile.Add<ColorAdjustments>();
            color.postExposure.Override(.05f);color.contrast.Override(8);color.saturation.Override(3);
            if(!profile.TryGet<Bloom>(out var bloom))bloom=profile.Add<Bloom>();
            bloom.threshold.Override(1.2f);bloom.intensity.Override(.12f);bloom.scatter.Override(.55f);
            foreach(var component in profile.components)
                if(!AssetDatabase.Contains(component))AssetDatabase.AddObjectToAsset(component,profile);
            catalog.PostProcessing=profile;EditorUtility.SetDirty(profile);
        }
    }
}
