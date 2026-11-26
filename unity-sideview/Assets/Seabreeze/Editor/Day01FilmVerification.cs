#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Video;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace Seabreeze.Editor {
[InitializeOnLoad]
public static class Day01FilmVerification {
 static FirstDayCinema cinema; static int index, callbacks; static double deadline; static bool captured; static string output;
 static int framesChecked;
 static readonly string[] films={"intro","arrival","fireworks"};
 static Day01FilmVerification(){EditorApplication.playModeStateChanged+=Mode;}
 public static void Run(){
  output=Path.GetFullPath("TestResults/Day01Films");Directory.CreateDirectory(output);
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  SessionState.SetBool("Day01FilmVerification",true);EditorApplication.EnterPlaymode();
 }
 static void Mode(PlayModeStateChange state){if(state!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("Day01FilmVerification",false))return;
  output=Path.GetFullPath("TestResults/Day01Films");
  new GameObject("Verification camera",typeof(Camera),typeof(AudioListener));
  UIPrimitives.Font=Resources.Load<TMPro.TMP_FontAsset>("Fonts/SeabreezeChinese");
  cinema=new GameObject("Film verification").AddComponent<FirstDayCinema>();cinema.Initialize(null);
  cinema.gameObject.AddComponent<Day01FilmClock>();Next();
 }
 static void Next(){
  if(index==films.Length && framesChecked!=3){Debug.LogError("Missing decoded frames");EditorApplication.Exit(1);return;}
  if(index==films.Length){SessionState.SetBool("Day01FilmVerification",false);File.WriteAllText(Path.Combine(output,"result.json"),"{\"passed\":true,\"films\":3,\"callbacks\":"+callbacks+"}");Debug.Log("DAY01_FILMS_PASSED "+output);EditorApplication.Exit(0);return;}
  captured=false;deadline=EditorApplication.timeSinceStartup+110;Action done=()=>{callbacks++;index++;Next();};
  if(index==0)cinema.PlayIntro(done);else if(index==1)cinema.PlayArrival(done);else cinema.PlayFireworks(done);
 }
 public static void Tick(){try{
  if(cinema==null)return;if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Film timeout: "+films[index]);
  var player=cinema.GetComponent<VideoPlayer>();
  if(player!=null && player.time>2 && !captured){
   if(player.width!=1920 || player.height!=1080)throw new Exception("Unexpected dimensions");
   var audio=cinema.GetComponent<AudioSource>();if(audio==null || !audio.isPlaying)throw new Exception("Film audio did not start");
   var prior=RenderTexture.active; RenderTexture.active=player.targetTexture;
   var image=new Texture2D(1920,1080,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
   File.WriteAllBytes(Path.Combine(output,films[index]+".png"),image.EncodeToPNG());UnityEngine.Object.Destroy(image);RenderTexture.active=prior;
   framesChecked++;captured=true;
  }
 }catch(Exception e){SessionState.SetBool("Day01FilmVerification",false);Debug.LogException(e);EditorApplication.Exit(1);}}
}
public class Day01FilmClock : MonoBehaviour { void Update(){Day01FilmVerification.Tick();} }
}
#endif
