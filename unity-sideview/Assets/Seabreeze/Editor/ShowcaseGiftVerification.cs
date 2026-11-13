#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using ZLive;
namespace Seabreeze.Editor {
[InitializeOnLoad] public static class ShowcaseGiftVerification {
 const string Flag="ShowcaseGiftVerification";
 static UnityEngine.SceneManagement.Scene originalScene;static ArrivalGiftView gift;static UIDocument document;static RenderTexture target;static int phase;static double deadline,started,preparedAt;static string output;
 static ShowcaseGiftVerification(){EditorApplication.update+=Tick;}
 public static void Run(){SessionState.SetBool(Flag,true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();}
 static void Tick(){if(!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
 try{
  if(phase==0){output="/private/tmp/z-showcase-gift";Directory.CreateDirectory(output);deadline=EditorApplication.timeSinceStartup+45;Application.runInBackground=true;
   ArrivalGiftView.Prewarm("小");ArrivalGiftView.Prewarm("小海");phase=1;return;}
  if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Gift verification timed out");
  if(phase==1){var warm=(ArrivalGiftView)typeof(ArrivalGiftView).GetField("warming",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);if(!warm.VideoPrepared)return;
   if(warm.Opened||warm.Collected||warm.PlayerName!="小海")throw new Exception("Preload changed story progress or player name");
   originalScene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
   var next=UnityEngine.SceneManagement.SceneManager.CreateScene("Gift scene transition");UnityEngine.SceneManagement.SceneManager.SetActiveScene(next);UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(originalScene);phase=6;return;
  }
  if(phase==6){if(originalScene.isLoaded)return;
   var warm=(ArrivalGiftView)typeof(ArrivalGiftView).GetField("warming",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
   document=new GameObject("Gift verification").AddComponent<UIDocument>();var settings=ScriptableObject.CreateInstance<PanelSettings>();settings.themeStyleSheet=Resources.Load<ThemeStyleSheet>("LiveTheme");target=new RenderTexture(2560,1556,24);target.Create();settings.targetTexture=target;document.panelSettings=settings;
   document.rootVisualElement.style.width=2560;document.rootVisualElement.style.height=1556;
   gift=ArrivalGiftView.TakePrepared(document.rootVisualElement,"小海");if(!ReferenceEquals(gift,warm)||!gift.VideoPrepared)throw new Exception("Prepared video not reused");started=EditorApplication.timeSinceStartup;preparedAt=started;phase=2;
  }
  gift?.Tick(0,0);
  if(phase==2&&gift.PlaybackTime>.03){preparedAt=EditorApplication.timeSinceStartup;phase=3;}
  if(phase==3&&gift.Ready){Capture("gift-ready");gift.ActivateGift();phase=4;}
  if(phase==4&&gift.Revealed){Capture("invitation-hd");Capture("name-layer","names");var rt=(RenderTexture)typeof(ArrivalGiftView).GetField("composite",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(gift);if(rt.width!=2560||rt.height!=1556)throw new Exception("Wrong composite size");gift.Collect();phase=5;}
  if(phase==5&&gift.Collected){File.WriteAllText(output+"/result.json",new JObject{["passed"]=true,["preparedInstanceReused"]=true,["playerName"]="小海",["width"]=2560,["height"]=1556,["firstFrameAfterRevealSeconds"]=preparedAt-started}.ToString());Finish(0);}
 }catch(Exception e){Debug.LogException(e);if(output!=null)File.WriteAllText(output+"/result.json",new JObject{["passed"]=false,["error"]=e.ToString()}.ToString());Finish(1);}}
 static void Capture(string name,string field="composite"){var rt=(RenderTexture)typeof(ArrivalGiftView).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(gift);var previous=RenderTexture.active;var pixels=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);try{RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);pixels.Apply();if(field=="names"){var color=pixels.GetPixel(720,800);if(color.maxColorComponent<.1f)throw new Exception("Named invitation layer was not rendered");}File.WriteAllBytes(output+"/"+name+".png",pixels.EncodeToPNG());}finally{RenderTexture.active=previous;UnityEngine.Object.Destroy(pixels);}}
 static void Finish(int code){SessionState.SetBool(Flag,false);gift?.Dispose();if(target!=null){target.Release();UnityEngine.Object.Destroy(target);}EditorApplication.Exit(code);}
}}
#endif
