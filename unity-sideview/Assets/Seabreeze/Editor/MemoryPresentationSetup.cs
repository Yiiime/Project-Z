using System;
using System.IO;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework.Expression;
using UnityEditor;
using UnityEngine;
namespace Seabreeze.Editor
{
    public static class MemoryPresentationSetup
    {
        const string ModelRoot="Assets/Seabreeze/Live2D/Models/Z";
        const string ProfilePath="Assets/Seabreeze/Resources/Memory/Portraits/Z.asset";
        [MenuItem("Seabreeze/Configure island dialogue presentation")]
        public static void Setup()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Configure portraits outside Play mode.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ModelRoot+"/Model.model3.json",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ModelRoot+"/Model.prefab");
            if(prefab==null||prefab.GetComponent<CubismModel>()==null||!File.Exists(ModelRoot+"/Model.asset"))throw new InvalidOperationException("Z Cubism import has not completed.");
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath));AssetDatabase.Refresh();
            var profile=AssetDatabase.LoadAssetAtPath<Live2DModelProfile>(ProfilePath);
            if(profile==null){profile=ScriptableObject.CreateInstance<Live2DModelProfile>();AssetDatabase.CreateAsset(profile,ProfilePath);}
            profile.prefab=prefab;profile.characterId="z_story";profile.rendererIndex=1;profile.renderLayer=30;
            profile.autoFrame=true;profile.visibleHeight=.78f;profile.verticalFocus=.62f;profile.textureHeight=1200;
            profile.modelScale=1;profile.proceduralIdle=true;profile.proceduralTalk=true;
            profile.expressions=new[]{Bind("neutral",null),Bind("happy","smile"),Bind("sad","z_aggrieved"),Bind("serious","z_serious"),Bind("shy","z_shy"),Bind("happy_wave","z_happy_wave")};
            profile.authoringNotes="Dialogue model imported from PR32 commit 70be1c747054dace87d1390314723e4770e5fa66. Only Z is bound. Reuses the existing Cubism SDK and renderer index 1; does not alter the live-room model. Text speaking drives procedural mouth motion, not voice lip sync.";
            EditorUtility.SetDirty(profile);AssetDatabase.SaveAssets();
            Debug.Log("Island dialogue portrait ready: "+ProfilePath);
        }
        static Live2DModelProfile.ExpressionBinding Bind(string key,string file)
        {
            return new Live2DModelProfile.ExpressionBinding{emotion=key,expression=file==null?null:AssetDatabase.LoadAssetAtPath<CubismExpressionData>(ModelRoot+"/"+file+".exp3.asset")};
        }
    }
}
