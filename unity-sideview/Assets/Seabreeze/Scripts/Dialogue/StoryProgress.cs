using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Seabreeze
{
    [Serializable] public sealed class StoryProgressSettings
    {
        public int taskPoints = 10, chatPoints = 2, maximum = 100;
        public int[] milestones = {25,50,75};
        public void Validate()
        {
            if(taskPoints<=0||chatPoints<=0||taskPoints<=chatPoints||maximum<=0||milestones==null||milestones.Any(v=>v<=0||v>=maximum)||!milestones.SequenceEqual(milestones.Distinct().OrderBy(v=>v)))
                throw new InvalidOperationException("Invalid story progression settings.");
        }
    }
    [Serializable] public sealed class StoryPersonProgress { public string actor; public int points,tasks,chats; }
    [Serializable] public sealed class StoryPlaytestQuest
    {
        public string id,actor,title,request,item,container,scene,assetMode,error,itemUrl,containerUrl;
        public float itemX,targetX;
        public bool ready,accepted,picked,completed;
    }
    [Serializable] public sealed class StoryProgressState
    {
        public List<StoryPersonProgress> people = new List<StoryPersonProgress>();
        public List<string> receipts = new List<string>(), chatSlots = new List<string>(), queuedClips = new List<string>(), watchedClips = new List<string>();
        public string winner="",liveId="";
        public bool livePending,endingSeen;
        public StoryPlaytestQuest quest;
        public void Sanitize()
        {
            if(people==null)people=new List<StoryPersonProgress>();
            if(receipts==null)receipts=new List<string>();
            if(chatSlots==null)chatSlots=new List<string>();
            if(queuedClips==null)queuedClips=new List<string>();
            if(watchedClips==null)watchedClips=new List<string>();
            if(winner==null)winner="";
        }
        public StoryPersonProgress Person(string actor)
        {
            var p=people.Find(v=>v.actor==actor);
            if(p==null){p=new StoryPersonProgress{actor=actor};people.Add(p);}return p;
        }
        public bool CreditTask(string actor,string receipt,StoryProgressSettings config)
        {
            config.Validate();
            if(string.IsNullOrEmpty(actor)||string.IsNullOrEmpty(receipt)||winner!=""||receipts.Contains("task:"+receipt))return false;
            receipts.Add("task:"+receipt);Person(actor).tasks++;Add(actor,config.taskPoints,config);return true;
        }
        public bool CreditChat(string actor,string conversation,int day,int period,StoryProgressSettings config)
        {
            config.Validate();string slot=actor+":"+day+":"+period;
            if(string.IsNullOrEmpty(actor)||string.IsNullOrEmpty(conversation)||winner!=""||chatSlots.Contains(slot))return false;
            chatSlots.Add(slot);receipts.Add("chat:"+conversation);Person(actor).chats++;Add(actor,config.chatPoints,config);return true;
        }
        void Add(string actor,int amount,StoryProgressSettings config)
        {
            var p=Person(actor);int before=p.points;p.points=(int)Math.Min(config.maximum,(long)p.points+amount);
            for(int i=0;i<config.milestones.Length;i++)if(before<config.milestones[i]&&p.points>=config.milestones[i])Queue(actor+"_"+(i+1));
            if(p.points>=config.maximum){winner=actor;livePending=false;Queue(actor+"_ending");}
        }
        void Queue(string id){if(!queuedClips.Contains(id)&&!watchedClips.Contains(id))queuedClips.Add(id);}
        public void FinishClip(string id){queuedClips.Remove(id);if(!watchedClips.Contains(id))watchedClips.Add(id);if(id==winner+"_ending")endingSeen=true;}
    }
    public static class StoryPlaytest
    {
        public static bool Verifying=>Array.IndexOf(Environment.GetCommandLineArgs(),"-storySmokeOutput")>=0;
        public static bool Enabled=>Application.identifier=="com.projectz.storyplaytest"||Environment.GetEnvironmentVariable("Z_STORY_PLAYTEST")=="1"||Array.IndexOf(Environment.GetCommandLineArgs(),"-storyPlaytest")>=0;
        static StoryProgressSettings settings;
        public static StoryProgressSettings Settings
        {
            get {if(settings!=null)return settings;var a=Resources.Load<TextAsset>("StoryPlaytest/settings");settings=a==null?new StoryProgressSettings():JsonUtility.FromJson<StoryProgressSettings>(a.text);settings.Validate();return settings;}
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void Reset(){settings=null;}
    }
}
