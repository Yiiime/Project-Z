#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Seabreeze.Editor {
// Focused transition fixture, separate from the real end-to-end journey driver.
[InitializeOnLoad]
public static class Day01ArrivalPreloadVerification {
 const string Flag="Seabreeze.Day01ArrivalPreloadVerification";
 static bool entered,done;static string conversation,output;static double deadline;
 static Day01ArrivalPreloadVerification(){EditorApplication.update+=Tick;}
 public static void Run(){
  output=Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT");
  if(string.IsNullOrEmpty(output)||!output.StartsWith("/private/tmp/"))throw new Exception("Isolated temporary fixture required");
  if(!File.Exists(Environment.GetEnvironmentVariable("Z_LIVE_CONNECTION_FILE")))throw new Exception("Explicit local test service required");
  Directory.CreateDirectory(output);
  var state=new SaveData{started=true,playerName="小海",place=nameof(PlaceId.Harbor),playerX=-10,period=(int)TimePeriod.Afternoon,firstDay=FirstDayState.NewJourney()};
  state.firstDay.phase=FirstDayPhase.Arrival;state.firstDay.invitationAccepted=true;state.firstDay.introSeen=true;
  if(!SaveRepository.Write(state))throw new Exception("Could not write fixture");
  SessionState.SetBool(Flag,true);EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity");
  var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));view.Show();view.Focus();EditorApplication.EnterPlaymode();
 }
 static void Tick(){
  if(done||!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying)return;
  try{
   output=Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT");if(deadline==0)deadline=EditorApplication.timeSinceStartup+120;
   if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Arrival preload timeout");
   var game=GameSession.Instance;if(game==null||!game.IsReady)return;
   if(game.IsAtTitle){if(!entered){typeof(StartPageView).GetMethod("Choose",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(game.GetComponent<StartPageView>(),new object[]{false});entered=true;}return;}
   var memory=game.Memory;if(memory?.Conversations==null)return;
   if(game.FirstDay.FilmActive){
    if(memory.Conversations.CommittedNpcLines!=0)throw new Exception("Greeting was published before film completion");
    if(memory.PlayerChatOpen)conversation=memory.Conversations.ActiveId;
    return;
   }
   if(game.FirstDay.BlocksInput||string.IsNullOrEmpty(conversation))return;
   if(!memory.PlayerChatOpen||memory.Conversations.ActiveId!=conversation)throw new Exception("Prepared conversation was cancelled at the film boundary");
   if(!string.IsNullOrEmpty(memory.Client.LastError))throw new Exception(memory.Client.LastError);
   if(memory.View.CinemaHidden)throw new Exception("Dialogue remained hidden");
   if(memory.Conversations.CommittedNpcLines==0||!memory.Client.SpeechPlaying)return;
   Finish(true,null);
  }catch(Exception e){Finish(false,e.Message);}
 }
 static void Finish(bool passed,string error){
  done=true;SessionState.SetBool(Flag,false);
  File.WriteAllText(Path.Combine(output,"arrival-preload-result.json"),new JObject{["passed"]=passed,["sameConversationAcrossFilm"]=passed,["speechPlaying"]=passed,["error"]=error}.ToString());
  Debug.Log("DAY01_ARRIVAL_PRELOAD_"+(passed?"PASSED":"FAILED "+error));EditorApplication.Exit(passed?0:1);
 }
}
}
#endif
