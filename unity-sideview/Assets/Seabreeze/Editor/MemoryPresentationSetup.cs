using System;
using System.IO;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework.Expression;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
namespace Seabreeze.Editor
{
    public static class MemoryPresentationSetup
    {
        const string ModelRoot="Assets/Seabreeze/Live2D/Models/Z";
        const string TexturePath=ModelRoot+"/texture_00.png";
        const string ControllerPath=ModelRoot+"/Model.controller";
        const string ProfilePath="Assets/Seabreeze/Resources/Memory/Portraits/Z.asset";
        const string SpeechParameter="ParamZSpeech";
        static readonly string[] PortraitMotionKeys={"cry","bawl","surprise","starry_curious"};
        [MenuItem("Seabreeze/Configure island dialogue presentation")]
        public static void Setup()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Configure portraits outside Play mode.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureTextureImport();
            AssetDatabase.ImportAsset(ModelRoot+"/Model.model3.json",ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var controller=ConfigurePortraitMotionController();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ModelRoot+"/Model.prefab");
            if(prefab==null||prefab.GetComponent<CubismModel>()==null||!File.Exists(ModelRoot+"/Model.asset"))throw new InvalidOperationException("Z Cubism import has not completed.");
            if(!HasParameter(prefab,SpeechParameter))throw new InvalidOperationException("Z Cubism import is missing the native speech parameter: "+SpeechParameter);
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath));AssetDatabase.Refresh();
            var profile=AssetDatabase.LoadAssetAtPath<Live2DModelProfile>(ProfilePath);
            if(profile==null){profile=ScriptableObject.CreateInstance<Live2DModelProfile>();AssetDatabase.CreateAsset(profile,ProfilePath);}
            profile.prefab=prefab;profile.characterId="z";profile.rendererIndex=1;profile.renderLayer=30;
            profile.autoFrame=true;profile.visibleHeight=.78f;profile.verticalFocus=.62f;profile.textureHeight=1200;
            profile.modelScale=1;profile.proceduralIdle=true;profile.proceduralTalk=true;
            profile.animatorController=controller;
            profile.idleState="Idle";profile.talkState="Talk";
            profile.mouthParameter=SpeechParameter;
            profile.expressions=new[]{Bind("neutral",null),Bind("happy","smile"),Bind("sad","z_aggrieved"),Bind("serious","z_serious"),Bind("shy","z_shy"),Bind("happy_wave","z_happy_wave")};
            profile.authoringNotes="Z dialogue model imported from the local r5-daily-speech source using the target Cubism SDK. Only Z is bound; the live-room model is untouched. Text speaking drives the native ParamZSpeech mouth parameter, not voice lip sync.";
            EditorUtility.SetDirty(profile);AssetDatabase.SaveAssets();
            Debug.Log("Island dialogue portrait ready: "+ProfilePath);
        }

        static AnimatorController ConfigurePortraitMotionController()
        {
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if(controller==null)throw new InvalidOperationException("Z Cubism animator controller is unavailable: "+ControllerPath);
            if(controller.layers==null||controller.layers.Length==0)throw new InvalidOperationException("Z Cubism animator controller has no layer: "+ControllerPath);

            var stateMachine=controller.layers[0].stateMachine;
            var idle=FindOrAddState(stateMachine,"Idle");
            FindOrAddState(stateMachine,"Talk");
            foreach(var key in PortraitMotionKeys)
            {
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ModelRoot+"/"+key+".anim");
                if(clip==null)throw new InvalidOperationException("Z Cubism motion clip is unavailable: "+ModelRoot+"/"+key+".anim");
                FindOrAddState(stateMachine,key).motion=clip;
            }
            if(stateMachine.defaultState==null)stateMachine.defaultState=idle;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        static AnimatorState FindOrAddState(AnimatorStateMachine stateMachine,string name)
        {
            foreach(var child in stateMachine.states)
                if(child.state!=null&&child.state.name==name)return child.state;
            return stateMachine.AddState(name);
        }

        static void ConfigureTextureImport()
        {
            var importer=AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            if(importer==null)throw new InvalidOperationException("Z texture importer is unavailable: "+TexturePath);
            importer.textureType=TextureImporterType.Default;
            importer.spriteImportMode=SpriteImportMode.None;
            importer.mipmapEnabled=false;
            importer.maxTextureSize=4096;
            importer.filterMode=FilterMode.Bilinear;
            importer.npotScale=TextureImporterNPOTScale.ToNearest;
            importer.SaveAndReimport();
        }

        static bool HasParameter(GameObject prefab,string id)
        {
            if(prefab==null)return false;
            foreach(var parameter in prefab.GetComponentsInChildren<CubismParameter>(true))
                if(parameter!=null&&parameter.gameObject.name==id)return true;
            return false;
        }

        static Live2DModelProfile.ExpressionBinding Bind(string key,string file)
        {
            return new Live2DModelProfile.ExpressionBinding{emotion=key,expression=file==null?null:AssetDatabase.LoadAssetAtPath<CubismExpressionData>(ModelRoot+"/"+file+".exp3.asset")};
        }
    }
}
