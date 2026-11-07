import {CALL_STORY} from './call-contract.js';
import {CALL_VOICE_DEFAULTS as POLICY,readRecognitionResults,filterCallRecognitionResults,turnDisposition,CallTranscriptWindow} from './call-voice-policy.js';
import {CallRecognition} from './voiceprint-recognition.js';
import {CALL_THINKING_TEXT} from './speech-director.js';

export function mountCallUI({engine,director,toast,unpause,Recognition=CallRecognition}){
  const make=(tag,id,text='')=>{const el=document.createElement(tag);el.id=id;el.textContent=text;return el;};
  const form=document.getElementById('chat-form'),input=document.getElementById('chat-input'),speechBox=document.getElementById('speech-box');
  const button=make('button','call-mic');button.type='button';button.className='call-toggle';
  button.innerHTML='<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="9" y="2" width="6" height="12" rx="3"/><path d="M5 10v2a7 7 0 0 0 14 0v-2M12 19v3M8 22h8"/></svg><span class="call-indicator" aria-hidden="true"></span>';
  form.insertBefore(button,document.getElementById('send-message'));
  const panel=make('section','call-panel');panel.className='call-panel';panel.hidden=true;panel.setAttribute('aria-label','连麦对话');
  const rows=make('div','call-transcript');rows.className='call-transcript';rows.setAttribute('role','log');rows.setAttribute('aria-label','最近两条对话');panel.append(rows);speechBox.append(panel);
  const storyBox=make('section','call-story-card');storyBox.className='call-story-card';storyBox.hidden=true;
  storyBox.innerHTML='<span>今晚的小片段</span><p id="call-story-question"></p><div id="call-story-choices"></div>';
  document.querySelector('.sidebar').prepend(storyBox);
  const captions=new CallTranscriptWindow();
  let recognition=null,epoch=0,timeout=null,rearm=null,settle=null,joining=false,closing=false,disposed=false,finishing=false;
  let sessionId=null,lastStory='',lastSpeechId=null,guestId=null,pendingReplyId=null,pendingGuestId=null,draftBase='';
  let finalText='',interimText='',uncertain=false,reviewInput=null,reviewReason='',expanded=new Set(),acoustic={available:false};
  const spokenEchoes=new Map();
  let voiceCommitting=false;
  let thinkingTimer=null,lastThinkingAt=-Infinity;
  const cancelThinking=()=>{clearTimeout(thinkingTimer);thinkingTimer=null;void director.stopCallThinking?.(80);};
  function scheduleThinking(){
    clearTimeout(thinkingTimer);const callId=engine.getState().call.id;
    thinkingTimer=setTimeout(()=>{thinkingTimer=null;const s=engine.getState();
      if(disposed||s.paused||document.hidden||!s.call?.active||s.call.id!==callId||s.call.input!=='idle'||audioBusy(s)||s.currentSpeechId||!engine.pending||Date.now()-lastThinkingAt<20000)return;
      if(director.playCallThinking?.())lastThinkingAt=Date.now();
    },700);
  }
  const textNow=()=>[finalText,interimText].filter(Boolean).join('').trim();
  const audioBusy=s=>['preparing','ready','starting','playing','paused'].includes(s.media?.phase);
  const displayDraft=()=>[draftBase,textNow()].filter(Boolean).join('\n');
  function placeholder(status='listening'){
    if(!pendingReplyId)pendingReplyId='reply-'+crypto.randomUUID();
    captions.upsert({id:pendingReplyId,role:'assistant',text:status==='thinking'?CALL_THINKING_TEXT:'正在听…',status});
  }
  function paintRows(){
    const oldScroll=rows.scrollTop,atBottom=rows.scrollHeight-rows.clientHeight-oldScroll<24;
    rows.replaceChildren();
    for(const row of captions.snapshot()){
      const item=make('div','call-row-'+row.id);item.className='call-dialogue-row';item.dataset.role=row.role;item.dataset.status=row.status;
      const avatar=make('span','call-avatar-'+row.id,row.role==='guest'?'你':'Z');avatar.className='speaker-z call-avatar';avatar.setAttribute('aria-label',row.role==='guest'?'玩家头像':'Z 的头像');
      const content=make('p','call-content-'+row.id);content.className='call-bubble';
      const interruptible=row.role==='assistant'&&row.status==='speaking'&&row.id===engine.getState().currentSpeechId;
      const text=make(interruptible?'button':'span','call-text-'+row.id,row.text);text.className='call-bubble-text';
      if(interruptible){text.type='button';text.title='可以直接开口，也可以点击插话';text.setAttribute('aria-label',row.text+'；点击插话');text.onclick=takeFloor;}
      const long=Array.from(row.text).length>POLICY.previewChars;
      if(expanded.has(row.id))text.classList.toggle('expanded',true);
      content.append(text);
      if(long){const more=make('button','call-expand-'+row.id,expanded.has(row.id)?'收起':'展开整句');more.type='button';more.className='call-expand';more.setAttribute('aria-expanded',String(expanded.has(row.id)));more.onclick=()=>{if(expanded.has(row.id))expanded.delete(row.id);else expanded.add(row.id);paintRows();};content.append(more);}
      if(row.status==='interrupted'){const hint=make('small','call-interrupted-'+row.id,'已打断');hint.className='call-inline-note';content.append(hint);}
      if(row.role==='guest'&&row.status==='review'){
        const hint=make('small','call-review-reason',reviewReason);hint.className='call-inline-note';content.append(hint);
        const actions=make('span','call-review-actions');actions.className='call-review-actions';
        for(const [id,label,fn] of [['call-commit','发送这句',confirmDraft],['call-continue','继续说',continueDraft],['call-discard','重说',discardDraft]]){const action=make('button',id,label);action.type='button';action.onclick=fn;actions.append(action);}content.append(actions);
      }
      item.append(avatar);item.append(content);rows.append(item);
    }
    expanded=new Set([...expanded].filter(id=>captions.rows.some(row=>row.id===id)));
    rows.scrollTop=expanded.size||!atBottom?oldScroll:rows.scrollHeight;
  }
  function clearTimers(){clearTimeout(timeout);clearTimeout(rearm);clearTimeout(settle);timeout=null;rearm=null;settle=null;}
  function stopMic(){epoch++;clearTimers();finishing=false;const rec=recognition;recognition=null;if(rec){rec.onresult=null;rec.onend=null;rec.onerror=null;try{rec.abort();}catch{}}acoustic={available:false};}
  function resetDraft(){finalText='';interimText='';uncertain=false;guestId=null;draftBase='';reviewInput=null;reviewReason='';}
  function leave(message){
    if(closing)return;closing=true;cancelThinking();stopMic();resetDraft();engine.endCall();joining=false;closing=false;render();if(message)toast(message);
  }
  function scheduleListen(){
    if(settle||rearm||recognition||joining||disposed||closing)return;
    const s=engine.getState();if(!s.call?.active||s.paused||document.hidden||audioBusy(s)||s.call.input==='reviewing')return;
    const id=s.call.id;rearm=setTimeout(()=>{rearm=null;const now=engine.getState();if(now.call?.id===id&&!audioBusy(now)&&!now.paused&&!document.hidden&&now.call.input!=='reviewing')listen();},POLICY.restartMs);
  }
  function reviewDraft(message){
    if(!textNow())return;
    if(!guestId)guestId='guest-'+crypto.randomUUID();
    captions.upsert({id:guestId,role:'guest',text:displayDraft(),status:'review'});reviewReason=message;
    // Preserve the entire draft even when it exceeds the existing text input's capacity.
    if(textNow().length<=(input.maxLength>0?input.maxLength:120)){input.value=textNow();reviewInput=textNow();}else reviewInput=null;
    placeholder();engine.setCallInput('reviewing');paintRows();render();
  }
  function submitDraft(explicit=false){
    const disposition=turnDisposition({text:textNow(),final:explicit||!interimText,uncertain:explicit?false:uncertain,manual:explicit});
    if(disposition==='empty'||disposition==='filler'){discardDraft();return;}
    if(disposition!=='send'){reviewDraft(disposition==='incomplete'?'这句好像还没说完':textNow().length>POLICY.maxText?'这句较长，请在输入框分段发送；原文已保留':'这句没听完整，确认一下文字');return;}
    const value=explicit?textNow():finalText.trim();
    // The engine's message event is the single commit point, shared with typed input.
    if(engine.sendMessage(value)===false){reviewDraft('这句未发送，文字已保留');return;}
    input.value='';render();
  }
  function finishRecognition(explicit=false){
    if(!recognition){if(explicit)submitDraft(true);return;}
    if(finishing)return;finishing=explicit?'explicit':'automatic';clearTimeout(settle);clearTimeout(timeout);settle=null;timeout=null;
    try{recognition.stop();}catch{stopMic();reviewDraft('识别暂时中断，确认一下文字');return;}
    if(recognition)timeout=setTimeout(()=>{if(recognition){stopMic();reviewDraft('没有收到定稿，确认一下文字');}},8000);
  }
  function armSettle(){
    clearTimeout(settle);settle=null;if(finishing||!textNow())return;
    if(recognition?.persistent)return; // MMPG performs semantic end-of-turn detection.
    const incomplete=turnDisposition({text:textNow(),uncertain:false})==='incomplete';
    settle=setTimeout(()=>{
      settle=null;
      if(recognition&&acoustic.available&&acoustic.voiceLikely){uncertain=true;settle=setTimeout(()=>{settle=null;finishRecognition();},POLICY.maxNoiseWaitMs);}
      else finishRecognition();
    },incomplete?POLICY.incompletePauseMs:POLICY.pauseMs);
  }
  function listen(){
    const s=engine.getState();if(disposed||recognition||!Recognition||!s.call?.active||audioBusy(s)||s.call.input==='reviewing')return;
    clearTimeout(rearm);rearm=null;const id=s.call.id;let rec;
    try{rec=new Recognition();}catch{leave('语音暂不可用，可直接打字');return;}
    const token=++epoch;recognition=rec;finishing=false;
    rec.onacoustic=value=>{if(current())acoustic=value;};
    rec.lang='zh-CN';rec.continuous=true;rec.interimResults=true;rec.maxAlternatives=1;
    let carried=finalText,carriedUncertain=uncertain,lastConversationId=null;
    const ignoredEchoes=new Set();
    const current=()=>!disposed&&recognition===rec&&epoch===token&&engine.getState().call?.id===id&&engine.getState().call.active&&!engine.getState().paused&&!document.hidden;
    rec.onsetup=()=>{
      if(!current())return;clearTimeout(timeout);timeout=null;
    };
    rec.onstart=()=>{if(!current()||!rec.persistent)return;clearTimeout(timeout);timeout=null;render();};
    rec.ondiscard=()=>{if(current()&&guestId){discardDraft();}};
    rec.onresult=e=>{
      if(!current())return;
      if(rec.persistent){
        if(e.speakerVerified!==true)return;
        if(e.conversationId!==lastConversationId){ignoredEchoes.clear();lastConversationId=e.conversationId;}
      }
      const previous=textNow(),state=engine.getState();
      const echoTexts=[...spokenEchoes.values()].filter(x=>x.id===state.currentSpeechId||Date.now()-x.at<2500).map(x=>x.text);
      const results=e.speakerVerified?e.results:filterCallRecognitionResults(e.results,echoTexts,ignoredEchoes),transcript=readRecognitionResults(results);
      const incoming=(carried+transcript.finalText+transcript.interimText).trim();
      if(state.currentSpeech&&!engine.callYield&&!guestId){
        if((!e.speakerVerified&&incoming.replace(/[^\p{L}\p{N}]/gu,'').length<2)||turnDisposition({text:incoming})==='filler')return;
        const last=results.at(-1),confidence=Number(last?.[0]?.confidence);
        if(last?.isFinal&&confidence>0&&confidence<POLICY.minConfidence)return;
      }
      if(!incoming)return;
      finalText=carried+transcript.finalText;interimText=transcript.interimText;uncertain=carriedUncertain||transcript.uncertain||acoustic.noisy;
      const value=textNow(),disposition=turnDisposition({text:value});
      if(!value||disposition==='empty'||disposition==='filler')return;
      if(!guestId){
        clearTimeout(timeout);timeout=setTimeout(()=>{if(current()){stopMic();reviewDraft('这句还没定稿，确认后再发');}},POLICY.maxTurnMs);
        const previousGuest=pendingReplyId&&captions.rows.find(row=>row.id===pendingGuestId);
        guestId=previousGuest?.id||'guest-'+crypto.randomUUID();draftBase=previousGuest?.text||'';
      }
      // First lexical evidence owns the floor. Arming a microphone is never an interruption.
      cancelThinking();
      if(state.currentSpeech&&!engine.callYield)void engine.yieldCallFloor();else engine.setCallInput('listening');
      captions.upsert({id:guestId,role:'guest',text:displayDraft(),status:'draft'});placeholder();paintRows();if(value!==previous)armSettle();render();
      if(rec.persistent&&e.turnFinal){
        engine.audience?.dialogue?.log({stage:'call.input.final',conversationId:e.conversationId,receivedAt:Date.now()});
        clearTimeout(timeout);timeout=null;voiceCommitting=true;
        try{submitDraft(false);}finally{voiceCommitting=false;}
        if(!textNow()){carried='';carriedUncertain=false;}
        else if(engine.getState().call.input==='reviewing')stopMic();
      }
    };
    rec.onerror=e=>{
      if(!current())return;
      if(e.error==='voiceprint_setup_required'){leave('请先到设置里完成「连麦声音设置」');return;}
      if(e.error==='no-speech'){stopMic();if(textNow())reviewDraft('没有收到定稿，确认一下文字');else{resetDraft();engine.setCallInput('idle');render();}return;}
      const asrErrors={asr_not_configured:'豆包识别尚未配置，请填写火山识别配置.env；可先打字',asr_permission:'豆包 ASR 权限不足，请开通流式识别 2.0 并检查凭证',asr_quota:'豆包识别额度或并发不足，请检查控制台',asr_connect_timeout:'豆包识别连接超时，请稍后重新上麦',asr_final_timeout:'豆包识别定稿超时，已下麦',asr_provider_rejected:'豆包识别请求被拒绝，请检查资源权限和额度'};
      const voiceErrors={voiceprint_not_configured:'声纹连麦配置尚未完整，请检查火山声纹配置.env',voiceprint_enrollment_too_quiet:'录到的讲话不够，请在安静处靠近麦克风，重新上麦读完引导文字',voiceprint_registration_failed:'声音登记未成功，请确认录音清晰后重新上麦',voiceprint_verification_inactive:'声纹验证未生效，已下麦；请检查声纹登记状态',voiceprint_permission:'声纹服务权限不足，请检查火山应用',voiceprint_provider_rejected:'声纹服务请求被拒绝，请检查资源权限或额度',voiceprint_session_expired:'本次连麦已满 30 分钟，请重新上麦'};
      const failureMessage=voiceErrors[e.error]||asrErrors[e.error]||({'not-allowed':'未获得麦克风权限，已下麦','service-not-allowed':'浏览器未开放语音识别，已下麦','audio-capture':'未找到麦克风，已下麦','network':'语音连接中断，已下麦'})[e.error]||'语音暂不可用，已下麦';
      if(textNow()){stopMic();reviewDraft('识别中断，已保留文字；可发送或重说');toast(failureMessage);return;}
      leave(failureMessage);
    };
    rec.onend=()=>{
      if(!current())return;
      const explicit=finishing==='explicit',wanted=!!finishing;stopMic();
      if(!textNow()){resetDraft();engine.setCallInput('idle');render();return;}
      const incomplete=turnDisposition({text:textNow(),uncertain:false})==='incomplete';
      // Lifecycle end alone is not evidence of a complete sentence.
      settle=setTimeout(()=>{settle=null;submitDraft(explicit);},wanted?0:incomplete?POLICY.incompletePauseMs:POLICY.pauseMs);render();
    };
    try{rec.start();timeout=setTimeout(()=>{if(current()){stopMic();if(textNow())reviewDraft('这句还没定稿，确认后再发');else render();}},POLICY.maxTurnMs);}catch{leave('语音暂不可用，可直接打字');}
    render();
  }
  async function takeFloor(){
    if(!engine.getState().call?.active)return;
    await engine.yieldCallFloor();listen();render();
  }
  function confirmDraft(){
    if(reviewInput!==null&&input.value.trim()!==reviewInput){
      if(!input.value.trim()){reviewReason='文字已清空，可以重说或重新输入';paintRows();return;}
      finalText=input.value.trim();interimText='';uncertain=false;
    }
    finishRecognition(true);
  }
  function continueDraft(){
    if(!textNow()||recognition)return;clearTimeout(settle);settle=null;
    if(interimText){uncertain=true;finalText+=interimText;interimText='';}
    input.value='';reviewInput=null;engine.setCallInput('listening');captions.update(guestId,{status:'draft'});listen();paintRows();
  }
  function discardDraft(){
    stopMic();const previous=draftBase;
    if(previous)captions.update(guestId,{text:previous,fullText:previous,status:'final'});else captions.remove(guestId);
    if(!previous){captions.remove(pendingReplyId);pendingReplyId=null;}
    resetDraft();input.value='';engine.setCallInput('idle');
    if(previous&&engine.inbox?.some(m=>m.mine)){placeholder('thinking');engine._wakeCall();}
    paintRows();render();
  }
  function render(){
    const s=engine.getState(),c=s.call||{active:false};
    if(c.active&&(s.paused||document.hidden)&&!closing){leave('已暂停连麦');return;}
    if(c.active&&c.id!==sessionId){sessionId=c.id;captions.clear();expanded.clear();spokenEchoes.clear();resetDraft();lastSpeechId=null;pendingReplyId=null;pendingGuestId=null;paintRows();}
    if(!c.active){cancelThinking();stopMic();}
    else if(recognition&&audioBusy(s)){stopMic();if(textNow()&&guestId)reviewDraft('这句没收完整，确认一下文字');else resetDraft();}
    if(lastSpeechId&&lastSpeechId!==s.currentSpeechId){const row=captions.rows.find(x=>x.id===lastSpeechId);if(row&&row.status!=='completed'){if(row.text&&row.status==='speaking')captions.update(lastSpeechId,{status:'interrupted'});else captions.remove(lastSpeechId);}lastSpeechId=null;paintRows();}
    button.disabled=!c.active&&!engine.enabled;button.setAttribute('aria-pressed',String(c.active));button.setAttribute('aria-label',c.active?'下麦':'上麦');button.title=c.active?'已上麦 · 再点一下下麦':'点击上麦';button.classList.toggle('is-listening',!!recognition);
    panel.hidden=!c.active||!captions.rows.length;speechBox.classList.toggle('call-active',c.active);speechBox.classList.toggle('call-empty',c.active&&!captions.rows.length);
    document.getElementById('reply-status')?.classList.toggle('call-hidden',c.active);
    input.placeholder=c.active?'也可以打字，发送给 Z…':'和 Z 聊聊……';
    const story=c.active?c.story:null,key=JSON.stringify(story);storyBox.hidden=!story||story.status!=='offered';
    if(story&&key!==lastStory){lastStory=key;const q=storyBox.querySelector('#call-story-question'),choices=storyBox.querySelector('#call-story-choices');q.textContent=CALL_STORY.steps[story.step].question;choices.replaceChildren();if(story.status==='offered')for(const choice of CALL_STORY.steps[story.step].choices){const b=document.createElement('button');b.type='button';b.textContent=choice.label;b.onclick=()=>{stopMic();resetDraft();engine.chooseCallStory(choice.id,story.revision);};choices.append(b);}}
    if(!story)lastStory='';scheduleListen();
  }
  button.onclick=()=>{
    if(disposed)return;if(engine.getState().call?.active){leave();return;}if(joining)return;
    if(!Recognition||Recognition.isSupported&&!Recognition.isSupported()){toast('请用支持麦克风和 AudioWorklet 的浏览器打开本地 Demo；可先打字');return;}
    unpause();joining=true;try{Promise.resolve(director.setEnabled(true)).then(()=>{if(!disposed&&engine.getState().call?.active)void director.prepareCallThinking?.();}).catch(()=>{});}catch{}
    if(engine.joinCall())listen();joining=false;render();
  };
  const edit=()=>{if(engine.getState().call?.active){cancelThinking();stopMic();engine.setCallInput('reviewing');}};
  const blur=()=>{if(engine.getState().call?.active&&!input.value.trim()&&!textNow()){engine.setCallInput('idle');if(engine.inbox?.some(m=>m.mine))engine._wakeCall();}};
  const guestMessage=({detail})=>{
    if(!engine.getState().call?.active||!detail?.mine||detail.type!=='chat')return;
    const previous=pendingReplyId&&captions.rows.find(row=>row.id===pendingGuestId);
    const id=guestId||previous?.id||detail.id,text=[guestId?draftBase:previous?.text,detail.text].filter(Boolean).join('\n');
    if(voiceCommitting&&recognition?.persistent){clearTimers();finishing=false;}else stopMic();
    captions.upsert({id,role:'guest',text,status:'final'});pendingGuestId=id;resetDraft();placeholder('thinking');paintRows();scheduleThinking();
  };
  const suspend=()=>{if(engine.getState().call?.active)leave('已暂停连麦');else stopMic();};
  const visibility=()=>{if(document.hidden)suspend();};
  const speechStart=({detail})=>{
    if(!engine.getState().call?.active)return;lastSpeechId=detail.id;
    clearTimeout(thinkingTimer);thinkingTimer=null;
    // Swap the waiting bubble in place; never use detail.text (unplayed speech).
    if(pendingReplyId&&detail.source==='call-yield'){captions.update(pendingReplyId,{id:detail.id,status:'preparing',text:'正在听…',fullText:''});pendingReplyId=detail.id;}
    if(pendingReplyId&&detail.source!=='call-yield'){captions.update(pendingReplyId,{id:detail.id,status:'preparing',text:CALL_THINKING_TEXT,fullText:''});pendingReplyId=null;pendingGuestId=null;}
    paintRows();render();
  };
  const speechText=({detail})=>{
    if(!engine.getState().call?.active||detail.id!==engine.getState().currentSpeechId||!detail.text)return;
    spokenEchoes.set(detail.id,{id:detail.id,text:detail.text,at:Date.now()});while(spokenEchoes.size>3)spokenEchoes.delete(spokenEchoes.keys().next().value);
    captions.upsert({id:detail.id,role:'assistant',text:detail.text,status:'speaking'});
    if(engine.getState().currentSource==='call-yield'&&pendingReplyId){const waiting=captions.rows.find(r=>r.id===pendingReplyId);if(waiting){captions.remove(waiting.id);captions.upsert(waiting);}}
    paintRows();render();
  };
  const speechComplete=({detail})=>{captions.update(detail.id,{status:'completed'});if(detail.id===pendingReplyId)placeholder(engine.getState().call.input==='idle'?'thinking':'listening');if(lastSpeechId===detail.id)lastSpeechId=null;paintRows();render();};
  const feedback=({detail})=>{if(detail.phase==='speaking')spokenEchoes.set(detail.id,{id:detail.id,text:detail.text,at:Date.now()});};
  director.addEventListener?.('call-feedback',feedback);
  input.addEventListener('focus',edit);input.addEventListener('blur',blur);
  engine.addEventListener('message',guestMessage);engine.addEventListener('speak',speechStart);director.addEventListener?.('text',speechText);director.addEventListener?.('complete',speechComplete);
  engine.addEventListener('state',render);document.addEventListener('visibilitychange',visibility);window.addEventListener('pagehide',suspend);render();
  return {destroy(){disposed=true;cancelThinking();stopMic();engine.removeEventListener('state',render);engine.removeEventListener('message',guestMessage);engine.removeEventListener('speak',speechStart);director.removeEventListener?.('text',speechText);director.removeEventListener?.('complete',speechComplete);director.removeEventListener?.('call-feedback',feedback);input.removeEventListener('focus',edit);input.removeEventListener('blur',blur);document.removeEventListener('visibilitychange',visibility);window.removeEventListener('pagehide',suspend);button.remove();panel.remove();storyBox.remove();speechBox.classList.toggle('call-active',false);speechBox.classList.toggle('call-empty',false);document.getElementById('reply-status')?.classList.toggle('call-hidden',false);},captions};
}
