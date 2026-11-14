using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Seabreeze
{
    public sealed partial class GameUI
    {
        [Serializable] class ReviewedThreeActs
        {
            public string choiceLine,voiceResource;
            public string[] fingerprints,sourceRevisions,memoryIdentities;
            public ReviewedAct[] acts;
        }
        [Serializable] class ReviewedAct { public string title,text; }
        ReviewedThreeActs endingReviewedThreeActs;
        bool endingUsesReviewedThreeActs,endingChoiceAudioOwned,endingChoicePreviousAudioPause;
        AudioSource endingChoiceVoice;
        int endingChoiceVoicePlayCount;

        void ApplyReviewedThreeActCopy(string[] fingerprints)
        {
            endingUsesReviewedThreeActs=false;endingReviewedThreeActs=null;
            var asset=Resources.Load<TextAsset>("Dialogue/Ending/looking-back-three-acts");
            if(asset==null)return;
            try { endingReviewedThreeActs=JsonUtility.FromJson<ReviewedThreeActs>(asset.text); }
            catch(Exception e){Debug.LogWarning("Reviewed ending copy could not be read: "+e.Message);return;}
            var copy=endingReviewedThreeActs;
            if(copy?.acts==null||copy.acts.Length!=3||copy.fingerprints==null||endingRecapPages.Length!=3)return;
            bool matches=false;
            foreach(string fingerprint in fingerprints)if(Array.IndexOf(copy.fingerprints,fingerprint)>=0)matches=true;
            // Live game memories can coexist with the exact reviewed story. Hash
            // its original blocks alone, without changing the saved recollections.
            if(!matches&&seventhNight?.recollections!=null)
            {
                var original=new List<EndingDemo.EndingDemoView.Recollection>();
                foreach(var memory in seventhNight.recollections)if(memory!=null&&!IsLiveRecapMemory(memory))original.Add(memory);
                foreach(string fingerprint in RecollectionFingerprints(original.ToArray(),endingPlayerName))
                    if(Array.IndexOf(copy.fingerprints,fingerprint)>=0)matches=true;
                // Legacy Unity saves may already contain replacement characters
                // where an old uploaded source had malformed UTF-8. Fall back
                // only to the deterministic content revision plus the complete,
                // ordered identity/day set. Never match by title, file name, or
                // the machine-specific upload UUID after the revision colon.
                string revision=(endingSourceRevision??"").Split(':')[0];
                if(!matches&&copy.sourceRevisions!=null&&copy.memoryIdentities!=null&&
                    Array.IndexOf(copy.sourceRevisions,revision)>=0&&original.Count==copy.memoryIdentities.Length)
                {
                    matches=true;
                    for(int i=0;i<original.Count;i++)
                        if(original[i].day+":"+original[i].memoryId!=copy.memoryIdentities[i]){matches=false;break;}
                }
            }
            if(!matches)return;
            foreach(var act in copy.acts)if(act==null||string.IsNullOrWhiteSpace(act.text))return;
            // Keep the native layout, chronological ranges, original-source view,
            // reveal effect, previous/next controls and scene-image selection.
            for(int i=0;i<3;i++)
                endingRecapPages[i]=new EndingDemo.EndingDemoView.RecapPage{startDay=endingRecapPages[i].startDay,endDay=endingRecapPages[i].endDay,title=copy.acts[i].title,text=copy.acts[i].text};
            endingUsesReviewedThreeActs=true;
        }
        IEnumerator RevealSpokenChoice()
        {
            var voice=Resources.Load<AudioClip>(endingReviewedThreeActs?.voiceResource??"");
            if(voice==null){yield return RevealBlackOutcome();yield break;}
            var owner=session.State;var run=seventhNight;
            endingChoiceVoicePlayCount=0;endingBlackRevealed=endingBlackDetailsShown=false;endingBlackTransitioning=true;
            endingColumn.gameObject.SetActive(false);if(endingSceneImage!=null)endingSceneImage.gameObject.SetActive(false);
            endingBlackScreen.gameObject.SetActive(true);endingBlackGroup.alpha=1;endingBlackContentGroup.alpha=1;
            endingBlackContentGroup.interactable=false;endingBlackContentGroup.blocksRaycasts=false;
            endingBlackTitle.text="";endingBlackBody.text="";endingBlackWordsGroup.alpha=0;endingBlackNext.gameObject.SetActive(false);
            SetBlackFirstLineLayout();
            if(!endingChoiceAudioOwned){endingChoicePreviousAudioPause=AudioListener.pause;endingChoiceAudioOwned=true;}
            AudioListener.pause=true;
            yield return new WaitForSecondsRealtime(.9f);
            if(session.State!=owner||seventhNight!=run||overlayMode!="z-ending")yield break;
            endingBlackBody.text=endingReviewedThreeActs.choiceLine;endingBlackBody.fontSize=56;endingBlackBody.ForceMeshUpdate();
            if(endingChoiceVoice==null)endingChoiceVoice=endingBlackScreen.gameObject.AddComponent<AudioSource>();
            endingChoiceVoice.clip=voice;endingChoiceVoice.playOnAwake=false;endingChoiceVoice.loop=false;
            endingChoiceVoice.ignoreListenerPause=true;endingChoiceVoice.spatialBlend=0;endingChoiceVoice.volume=1;
            endingChoiceVoice.Play();endingChoiceVoicePlayCount++;
            for(float t=0;t<1;t+=Time.unscaledDeltaTime/.42f)
            {
                if(session.State!=owner||seventhNight!=run||overlayMode!="z-ending")yield break;
                endingBlackWordsGroup.alpha=Mathf.SmoothStep(0,1,t);yield return null;
            }
            endingBlackWordsGroup.alpha=1;
            yield return new WaitForSecondsRealtime(Mathf.Max(1.5f,voice.length));
            if(session.State!=owner||seventhNight!=run||overlayMode!="z-ending")yield break;
            endingBlackRevealed=endingBlackDetailsShown=true;endingBlackTransitioning=false;
            endingBlackContentGroup.interactable=true;endingBlackContentGroup.blocksRaycasts=true;
            endingBlackNext.gameObject.SetActive(true);endingBlackReveal=null;
        }
        void ReleaseEndingChoiceAudio()
        {
            if(endingChoiceVoice!=null)endingChoiceVoice.Stop();
            if(endingChoiceAudioOwned){AudioListener.pause=endingChoicePreviousAudioPause;endingChoiceAudioOwned=false;}
        }
    }
}
