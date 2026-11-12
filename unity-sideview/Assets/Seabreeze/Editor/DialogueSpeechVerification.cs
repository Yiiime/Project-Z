using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Seabreeze.Editor
{
    [InitializeOnLoad]
    public static class DialogueSpeechVerification
    {
        const string Key="Z.DialogueSpeechVerification";
        static int phase;static double deadline,changed,nextDiagnostic;static string output,actor;
        static JObject report;
        static DialogueSpeechVerification()
        {
            EditorApplication.update+=Tick;
            EditorApplication.playModeStateChanged+=state=>
            {
                if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Key,false))
                {EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
            };
        }
        public static void Begin()
        {
            if(Application.isBatchMode)throw new Exception("Run with a visible Game view: dialogue backdrop capture waits for end of frame.");
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-speechVerificationOutput");
            if(i<0||i+1>=args.Length)throw new Exception("Pass -speechVerificationOutput");
            string path=Path.GetFullPath(args[i+1]),save=Environment.GetEnvironmentVariable("Z_MEMORY_SAVE_ROOT")??"";
            if(!path.StartsWith("/private/tmp/",StringComparison.Ordinal)||!save.StartsWith("/private/tmp/",StringComparison.Ordinal))throw new Exception("Use isolated temporary output and save directories");
            SessionState.SetString(Key+".Output",path);SessionState.SetBool(Key,true);
            EditorSceneManager.OpenScene("Assets/Seabreeze/Scenes/Street.unity");
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            EditorApplication.EnterPlaymode();
        }
        static TextMeshProUGUI Caption(MemoryGameController m) => m.View.GetComponentsInChildren<TextMeshProUGUI>().Last(t=>t.name=="Message"&&t.text.StartsWith("语音验证："));
        static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false))return;
            try
            {
                if(report==null){output=SessionState.GetString(Key+".Output","");report=new JObject();deadline=EditorApplication.timeSinceStartup+240;Application.runInBackground=true;AudioListener.volume=1;}
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Speech verification timeout at phase "+phase);
                var game=GameSession.Instance;var m=game?.Memory;
                if(EditorApplication.timeSinceStartup>=nextDiagnostic)
                {
                    nextDiagnostic=EditorApplication.timeSinceStartup+10;
                    Debug.Log("Speech verification phase="+phase+" play="+EditorApplication.isPlaying+" compile="+EditorApplication.isCompiling+
                        " ready="+game?.IsReady+" service="+m?.Client?.Status+" error="+m?.Client?.LastError+" speech="+m?.Client?.SpeechStatus+
                        " chat="+m?.PlayerChatOpen+" input="+m?.View?.PlayerInputReady+" blocked="+game?.NonPlayerInputBlocked+
                        " actors="+(m?.Spawner==null?"none":string.Join(",",m.Spawner.Actors.Select(n=>n.npcId+":"+n.IsPresent+":"+n.Movement?.IsReady))));
                }
                if(!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
                if(m?.Client==null||m.Spawner==null||m.View==null||!game.IsReady)return;
                if(phase==0&&m.Client.CanGenerate&&!game.NonPlayerInputBlocked&&!WorldDirector.Instance.IsTransitioning&&Time.timeScale>0)
                {
                    var npc=m.Spawner.Actors.FirstOrDefault(n=>n!=null&&n.npcId=="z"&&n.IsPresent&&n.Movement!=null&&n.Movement.IsReady);
                    if(npc==null)return;actor=npc.npcId;WorldDirector.Instance.Player.position=npc.transform.position+Vector3.left;
                    Require(m.BeginPlayerChat(actor,false),"Could not open player chat");phase=1;return;
                }
                if(phase==1&&m.View.PlayerInputReady&&m.Client.CanGenerate){m.Conversations.SendPlayer("你好。");phase=2;return;}
                if(phase==2&&m.Client.SpeechBusy&&!m.Client.SpeechPlaying)
                {
                    Require(Caption(m).maxVisibleCharacters==0,"Caption ran ahead while audio was pending");
                    report["captionWaitsForAudio"]=true;
                }
                if(phase==2&&m.Client.SpeechPlaying)
                {
                    Require(m.LastReply?.actorId==actor,"Wrong speaker");Require(m.Client.Synchronized,"Voice before dialogue commit");
                    report["committedReplyPlayback"]=true;changed=EditorApplication.timeSinceStartup;phase=3;return;
                }
                if(phase==3&&EditorApplication.timeSinceStartup-changed>.8)
                {
                    Require(m.Client.GetComponents<AudioSource>().Any(a=>a.isPlaying&&a.timeSamples>0),"Audio sample cursor did not advance");
                    var caption=Caption(m);int count=caption.textInfo.characterCount;
                    Require(caption.maxVisibleCharacters>0&&caption.maxVisibleCharacters<count,"Caption did not follow audio progressively");
                    int expected=MemoryDialogueView.SpeechVisibleCharacters(count,m.Client.SpeechPosition,m.Client.SpeechDuration);
                    Require(Mathf.Abs(caption.maxVisibleCharacters-expected)<=1,"Caption drifted from playback clock");
                    Require(m.View.GetComponentInChildren<Live2DCharacterView>().HasSpeechDrive,"Portrait is not audio driven");
                    report["captionTracksAudioWithBoundedLead"]=true;report["portraitUsesAudioDrive"]=true;
                    var mute=m.View.GetComponentsInChildren<Button>().First(b=>b.name=="声音");mute.onClick.Invoke();
                    Require(!m.Client.SpeechPlaying&&!m.Client.SpeechBusy,"Mute did not stop voice");mute.onClick.Invoke();
                    report["muteStopsPlayback"]=true;m.Conversations.SendPlayer("再说一句。");phase=4;return;
                }
                if(phase==4&&m.Client.SpeechPlaying)
                {
                    m.Conversations.Cancel("语音验收结束");Require(!m.Client.SpeechPlaying&&!m.Client.SpeechBusy,"Close did not stop voice");
                    report["closeStopsPlayback"]=true;phase=5;return;
                }
                if(phase==5&&!m.PlayerChatOpen&&m.Client.CanGenerate){m.BeginPlayerChat(actor);phase=6;return;}
                if(phase==6&&m.View.PlayerInputReady&&m.Client.CanGenerate){m.Conversations.SendPlayer("延迟语音。");phase=7;return;}
                if(phase==7&&!m.Conversations.Sending&&m.Client.SpeechBusy&&m.Client.Synchronized)
                {
                    m.Conversations.Cancel("取消准备中的语音");changed=EditorApplication.timeSinceStartup;phase=8;return;
                }
                if(phase==8&&EditorApplication.timeSinceStartup-changed>4)
                {
                    Require(!m.Client.SpeechPlaying&&!m.Client.SpeechBusy,"Late audio played after close");report["lateResponseDiscarded"]=true;
                    if(!m.Client.CanGenerate)return;m.BeginPlayerChat(actor);phase=9;return;
                }
                if(phase==9&&m.View.PlayerInputReady&&m.Client.CanGenerate){m.Conversations.SendPlayer("故障语音。");phase=10;return;}
                if(phase==10&&!m.Conversations.Sending&&!m.Client.SpeechBusy&&m.Client.SpeechStatus.Contains("失败"))
                {
                    Require(m.Client.CanGenerate,"Speech failure blocked text input");report["failureKeepsTextAvailable"]=true;
                    m.Conversations.SendPlayer("恢复语音。");phase=11;return;
                }
                if(phase==11&&m.Client.SpeechPlaying){phase=12;return;}
                if(phase==12&&!m.Client.SpeechBusy)
                {
                    // Let the view observe playback completion on the next frame.
                    changed=EditorApplication.timeSinceStartup;phase=13;return;
                }
                if(phase==13&&EditorApplication.timeSinceStartup-changed>.2)
                {
                    Require(Caption(m).maxVisibleCharacters>=Caption(m).textInfo.characterCount,"Caption incomplete after playback");
                    Require(!m.View.GetComponentInChildren<Live2DCharacterView>().HasSpeechDrive,"Portrait speech drive survived playback");
                    report["playbackCompletesCaptionAndMouth"]=true;m.Conversations.SendPlayer("重载语音。");phase=14;return;
                }
                if(phase==14&&m.Client.SpeechPlaying)
                {
                    Require(game.Save(),"Could not save isolated state");game.LoadSavedGame();
                    Require(!m.Client.SpeechPlaying,"Reload did not stop voice");report["reloadStopsPlayback"]=true;
                    report["passed"]=true;report["actor"]=actor;Finish(0);
                }
            }
            catch(Exception e){if(report==null)report=new JObject();report["passed"]=false;report["error"]=e.ToString();report["phase"]=phase;Finish(1);}
        }
        static void Finish(int code)
        {
            SessionState.SetBool(Key,false);
            if(!string.IsNullOrEmpty(output))File.WriteAllText(output,report.ToString());
            EditorApplication.Exit(code);
        }
    }
}
