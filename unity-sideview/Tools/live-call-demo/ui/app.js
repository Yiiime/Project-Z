import {mountGameReturn} from './game-return.js';
import {CallConversationEngine as LiveEngine} from './call-engine.js';
import {mountCallUI} from './call-ui.js';
import {VoiceprintRecognition} from './voiceprint-recognition.js';
import {AgentClient} from './agent-client.js';
import {mountPlayUI} from './play-ui.js';
import {SpeechDirector} from './speech-director.js';
import {TalentDirector} from './talent-director.js';
import {ChatClient} from './chat-client.js';
import {cardBackArt,tarotArt,rewardArt} from './card-art.js';
import {setupMangaFace} from './manga-face.js';
import {Live2DStage} from './live2d-stage.js';

const $ = id => document.getElementById(id);
// Keep the floating cards above the full-width caption as its text wraps.
const stageColumn=document.querySelector('.stage-column');
function reserveCaptionSpace(){
  const caption=$('speech-box');
  const clearance=Math.ceil(caption.getBoundingClientRect().height+(parseFloat(getComputedStyle(caption).bottom)||0)+12)+'px';
  if(stageColumn.style.getPropertyValue('--speech-clearance')!==clearance)stageColumn.style.setProperty('--speech-clearance',clearance);
}
if(typeof ResizeObserver!=='undefined'){
  const captionLayout=new ResizeObserver(reserveCaptionSpace);
  captionLayout.observe($('speech-box'));captionLayout.observe($('live-stage'));
}
window.addEventListener('resize',reserveCaptionSpace);reserveCaptionSpace();
if(matchMedia('(max-width:800px)').matches)$('audience-panel').open=false;
const engine = new LiveEngine();
// Local, bounded diagnostics; no automatic upload or credentials in this export.
window.exportZDialogueTrace=()=>JSON.stringify({...engine.exportDialogueTrace(),voice:{...director.status,activeMode:director.current?.mode||null,audioContextState:director.context?.state||null}},null,2);
const director = new SpeechDirector();
engine.configureSpeech(director);
const talent = new TalentDirector({speech:director});
engine.configureTalent(talent);
engine.setSpeechDriven(true);
const transportTimings=[];
const chatClient = new AgentClient({onTiming:detail=>{engine.audience.dialogue.log({stage:'transport.timing',...detail});transportTimings.push(detail);if(transportTimings.length>12)transportTimings.shift();$('speech-box').dataset.transportTimings=JSON.stringify(transportTimings);}});
let chatAvailable = false;
let state = engine.getState(), danmakuOn = true, lowMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;
let typingText = '', elapsed = 0;
let fortuneWasVisible = false, fortuneReviewOpen = false;
let pendingMessages = false, userPaused = false, pageSuspended = false, composing = false;
const embeddedInUnity=new URLSearchParams(location.search).get('embed')==='unity';
let hostPaused=embeddedInUnity;
const characterStage = new Live2DStage({stage:$('live-stage'),host:$('live2d-host'),room:$('live2d-room'),foreground:$('live2d-foreground'),notice:$('live2d-status'),noticeText:$('live2d-status-text'),retryButton:$('live2d-retry'),reducedMotion:lowMotion});
function syncCharacterSettings(){
  const character=characterStage.getState();
  if($('setting-character'))$('setting-character').value=character.preferred;
  if($('character-connection-note'))$('character-connection-note').textContent=character.message;
  if($('setting-character-retry'))$('setting-character-retry').hidden=character.preferred!=='live2d'||character.status!=='error';
}
characterStage.addEventListener('change',syncCharacterSettings);
const number = n => Math.round(n).toLocaleString('en-US');
const reduced = () => lowMotion || matchMedia('(prefers-reduced-motion: reduce)').matches;
const iconPaths = {
  pause:'<path d="M8 5v14M16 5v14"/>', play:'<path d="m9 5 11 7-11 7z"/>',
  fullscreen:'<path d="M8 3H3v5m13-5h5v5M3 16v5h5m13-5v5h-5"/>',
  settings:'<path d="m10 3-1 3-3 1-3 3 2 2-1 4 4 1 2 4 3-2 4 1 1-4 3-3-2-3-1-3-4-1z"/><circle cx="12" cy="12" r="3"/>',
  share:'<path d="M12 15V3m-4 4 4-4 4 4M5 12v8h14v-8"/>'
  ,sound:'<path d="M11 5 6 9H3v6h3l5 4z"/><path d="M15 8q5 4 0 8m3-11q8 7 0 14"/>'
  ,muted:'<path d="M11 5 6 9H3v6h3l5 4z"/><path d="m16 9 6 6m0-6-6 6"/>'
};
function icon(name){return `<svg viewBox="0 0 24 24" aria-hidden="true">${iconPaths[name]}</svg>`;}
for(const [id,name] of [['pause','pause'],['fullscreen','fullscreen'],['settings','settings'],['share','share']]) $(id).innerHTML = icon(name);

function toast(text){
  const el=document.createElement('div'); el.className='toast'; el.textContent=text; $('toasts').append(el);
  while($('toasts').children.length>3) $('toasts').firstElementChild.remove();
  setTimeout(()=>{el.classList.add('out');setTimeout(()=>el.remove(),350);},3200);
}
function showDialog(content,setup){
  $('dialog-content').innerHTML=content;
  if(!$('dialog').open) $('dialog').showModal();
  setup?.();
}
function closeDialog(){$('dialog').close();}
$('dialog-close').onclick=closeDialog;
$('dialog').addEventListener('click',event=>{if(event.target===$('dialog')){const r=$('dialog').getBoundingClientRect();if(event.clientX<r.left||event.clientX>r.right||event.clientY<r.top||event.clientY>r.bottom)closeDialog();}});

const leaders=[{name:'晚风',amount:42,symbol:'风',asset:'avatar-wind.svg'},{name:'纸月亮',amount:26,symbol:'月',asset:'avatar-moon.svg'},{name:'海盐',amount:18,symbol:'海',asset:'avatar-moon.svg'}];
function leaderRow(person,index){
  const button=document.createElement('button');button.className='leader-row';
  button.innerHTML=`<span class="leader-avatar" aria-hidden="true"></span><span class="number">${String(index+1).padStart(2,'0')}</span><span class="leader-name"></span><span class="amount"></span>`;
  const art=avatarFor(person.name,person.mine);button.querySelector('.leader-avatar').innerHTML=art.svg;button.querySelector('.leader-avatar').style.background=art.background;
  button.querySelector('.leader-name').textContent=person.name;
  button.querySelector('.amount').textContent=`${person.amount} 分钟`;
  button.onclick=()=>profileDialog(person);return button;
}
function updateLeaders(){
  const list=[...leaders];
  list.sort((a,b)=>b.amount-a.amount);$('leaderboard').replaceChildren(...list.slice(0,3).map(leaderRow));return list;
}
updateLeaders();
function profileDialog(person){
  showDialog('<p class="dialog-eyebrow">MOON ISLAND RESIDENT</p><div class="profile-symbol">☾</div><h2 class="dialog-title" id="profile-name"></h2><p class="dialog-description">在月亮小岛收集一点星光，也把温柔留给认真生活的人。</p><div class="stat-grid"><div><b>Lv. 12</b><small>小岛居民</small></div><div><b id="profile-amount"></b><small>一起看月亮</small></div><div><b>☾</b><small>月亮守护者</small></div></div><button class="dialog-primary" id="profile-hello">向 TA 打招呼</button>',()=>{
    $('profile-name').textContent=person.name;$('profile-amount').textContent=`${person.amount} 分钟`;$('profile-hello').onclick=()=>{send(`@${person.name} 晚上好，一起听 Z 占卜吧！`);closeDialog();};
  });
}
function audienceDialog(){
  showDialog('<p class="dialog-eyebrow">MOON ISLAND FRIENDS</p><h2 class="dialog-title">今晚的小岛同伴</h2><p class="dialog-description">谢谢每一位来到小屋的朋友。此刻有 <span id="modal-viewers"></span> 人和你一起看月亮。</p><div class="leader-modal" id="modal-leaders"></div><p class="demo-note">观众人数与相伴时光为演示数据</p>',()=>{$('modal-viewers').textContent=number(state.viewers);$('modal-leaders').replaceChildren(...updateLeaders().map(leaderRow));});
}
for(const id of ['rank','viewers-button'])$(id).onclick=audienceDialog;
$('host-profile').onclick=()=>showDialog('<p class="dialog-eyebrow">MEET YOUR HOST</p><div class="profile-symbol">☾</div><h2 class="dialog-title">嗨，我是 Z。</h2><p class="dialog-description">我是 Z，住在月亮小岛。今天没有固定节目，我们随便聊聊。</p><p class="dialog-description">可以聊你的日常、心事，或一个突然想到的问题。</p><button class="dialog-primary" id="host-greet">和 Z 说声晚上好</button>',()=>{$('host-greet').onclick=()=>{closeDialog();send('晚上好，Z！很高兴认识你。');};});

function avatarFor(name,mine=false){
  // Fixed inline illustrations: a name picks a stable avatar, never SVG markup.
  const people=['晚风来信','一颗小橘子','奶油云朵','月亮邮差','小熊软糖','不熬夜的猫','海盐汽水','星星收藏家'];
  const drawings=[
    '<path d="M8 22Q19 8 30 12Q31 24 19 27L13 25" fill="#d2e1cf"/><path d="M10 29L26 15M16 22L15 16M20 19L26 20"/>',
    '<circle cx="20" cy="22" r="11" fill="#edbd83"/><path d="M20 11Q22 5 28 7Q27 12 20 11" fill="#a4c0a6"/><path d="M16 21h.5m7 0h.5M17 26q3 2 6 0"/>',
    '<path d="M10 27C2 24 7 15 13 17C12 6 28 6 28 17C36 16 37 28 28 29H12" fill="#fff9e9"/><path d="M15 22h.5m9 0h.5M18 26q2 2 4 0"/>',
    '<path d="M25 8C10 5 7 28 24 30C13 24 16 15 25 8Z" fill="#f0d9a1"/><path d="M27 18v8m-4-4h8"/>',
    '<circle cx="11" cy="12" r="5" fill="#d6b7ad"/><circle cx="29" cy="12" r="5" fill="#d6b7ad"/><ellipse cx="20" cy="23" rx="13" ry="11" fill="#e6c9b4"/><path d="M14 21h.5m11 0h.5M18 26l2 2 2-2M20 28v2"/>',
    '<path d="M8 26V9L16 15Q20 13 24 15L32 9V26Q20 38 8 26Z" fill="#d8d3e9"/><path d="M13 23h.5m13 0h.5M18 28q2 2 4 0M5 26l7 1m16 0 7-1"/>',
    '<path d="M14 8h12v6l4 5v14H10V19l4-5Z" fill="#c4e3e4"/><path d="M11 24q4-3 9 0t9 0M14 12h12"/><circle cx="17" cy="29" r="1" fill="#f9f8ed"/><circle cx="23" cy="19" r="1" fill="#f9f8ed"/>',
    '<path d="M20 6L24 16L35 17L27 24L29 35L20 29L11 35L13 24L5 17L16 16Z" fill="#ead7a4"/><path d="M16 21h.5m8 0h.5M17 25q3 2 6 0"/>'
  ];
  const aliases={'晚风':'晚风来信','纸月亮':'月亮邮差','海盐':'海盐汽水'};
  const known=people.indexOf(Object.hasOwn(aliases,name)?aliases[name]:name),hash=Array.from(name||'').reduce((sum,c)=>sum+c.codePointAt(0),0);
  const i=known<0?hash%drawings.length:known;
  const colors=['#e5ece4','#f6e6d1','#eee9f4','#e6e9f1','#f1e2e3','#e5e3f0','#e1eeee','#f2eddb'];
  const portrait=mine?'<path d="M11 33Q9 26 12 17Q20 5 28 17L30 32" fill="#c4b9d9"/><ellipse cx="20" cy="21" rx="8" ry="10" fill="#fae4d2"/><path d="M12 19Q15 8 26 13L29 20L23 15L17 20" fill="#645a70"/><path d="M16 22h.5m7 0h.5M18 27q2 2 4 0"/><path d="M9 36Q20 27 31 36" fill="#b9d6df"/>':drawings[i];
  return {background:mine?'#e8dff2':colors[i],svg:`<svg viewBox="0 0 40 40" aria-hidden="true" focusable="false"><g stroke="#69778b" stroke-width="1.15" stroke-linecap="round" stroke-linejoin="round" fill="none">${portrait}</g></svg>`};
}
function receiptText(status){
  return {seen:'已送达',pending:'已收到 · 正在准备回应',queued:'待回应 · 等 Z 说完这段',replying:'Z 正在回应',replied:'Z 已完整回应'}[status]||'已送达';
}
function renderMessage(message,scroll=true){
  if(message.type==='z')return;
  const log=$('chat-scroll'),nearBottom=log.scrollHeight-log.clientHeight-log.scrollTop<65;
  const system=message.type==='system',mine=message.mine&&!system;
  const row=document.createElement('div');row.dataset.messageId=message.id;row.className=`chat-message ${system?'system':'chat'}${mine?' mine':''}`;
  const avatar=document.createElement('span');avatar.className=system?'system-mark':'message-avatar';avatar.setAttribute('aria-hidden','true');
  if(system)avatar.textContent='✦';else {const art=avatarFor(message.name,mine);avatar.innerHTML=art.svg;avatar.style.background=art.background;}
  const name=document.createElement('div');name.className='message-name';name.textContent=system?'小屋通知':message.name;
  if(mine){const tag=document.createElement('small');tag.className='self-badge';tag.textContent='你';name.append(tag);}
  const body=document.createElement('p');body.className='message-text';body.textContent=message.text;
  row.append(avatar,name,body);if(mine){const receipt=document.createElement('small');receipt.className='message-receipt';receipt.textContent=receiptText(message.replyStatus);row.append(receipt);}$('messages').append(row);
  while($('messages').children.length>80)$('messages').firstElementChild.remove();
  if(scroll&&(nearBottom||message.mine))requestAnimationFrame(()=>{log.scrollTop=log.scrollHeight;});
  else if(scroll){pendingMessages=true;$('new-messages').hidden=false;}
  if(message.type!=='z'&&message.type!=='system')floatMessage(message);
}
$('chat-scroll').addEventListener('scroll',()=>{const el=$('chat-scroll');if(el.scrollHeight-el.clientHeight-el.scrollTop<65){pendingMessages=false;$('new-messages').hidden=true;}});
$('new-messages').onclick=()=>{$('chat-scroll').scrollTo({top:$('chat-scroll').scrollHeight,behavior:reduced()?'instant':'smooth'});};
let lane=0;
function floatMessage(message){
  if(!danmakuOn||state.paused||reduced())return;
  const layer=$('danmaku-layer');if(layer.children.length>=10)layer.firstElementChild.remove();
  const el=document.createElement('span');el.className=`danmaku-item${message.mine?' mine':''}`;
  el.textContent=`${message.mine?'你':message.name}：${message.text}`;
  el.style.top=`${(lane++%5)*28}px`;el.style.animationDuration=`${10+Math.random()*3}s`;layer.append(el);el.addEventListener('animationend',()=>el.remove(),{once:true});setTimeout(()=>el.remove(),14000);
}

function speak(detail){
  typingText=detail.text;
  characterStage.setSpeech(detail);
  $('character').dataset.mood=detail.mood;$('character').dataset.gesture=detail.gesture;
  $('speech-box').dataset.source=detail.source||'local';
  $('speech-box').dataset.speechId=String(detail.id);
  $('speech-box').dataset.scope=detail.scope||'phase';
  $('mood-label').textContent={happy:'小屋元气补给中',serious:'认真解读中',thinking:'认真接住你的心事',warm:'温柔也有一点俏皮',neutral:'安静陪你聊聊',shy:'有一点不好意思',sad:'陪你慢慢说',surprised:'听到一个小意外'}[detail.mood]||'月光悄悄话';
  $('character-emotion').textContent={happy:'✦',serious:'✧',thinking:'· · ·',warm:'♡',neutral:'☾',shy:'☺',sad:'·',surprised:'!'}[detail.mood]||'✦';
  director.speak(detail);
}
director.addEventListener('text',({detail})=>{if(detail.id===state.currentSpeechId){$('speech-text').textContent=detail.text;engine.recordSpeechProgress(detail.id,detail.text);}});
director.addEventListener('activity',({detail})=>{if(detail.id===state.currentSpeechId){$('character').classList.toggle('speaking',detail.speaking);characterStage.setActivity(detail);}});
director.addEventListener('call-feedback',({detail})=>{
  if(detail.phase==='speaking'&&state.call?.active&&!state.currentSpeechId){characterStage.setSpeech({id:detail.id,mood:'thinking',gesture:'none'});characterStage.setPerformance({id:detail.id,phase:'speaking'});}
  if(characterStage.speech.id!==detail.id)return;
  if(detail.phase==='activity'){$('character').classList.toggle('speaking',detail.speaking);characterStage.setActivity(detail);}
  if(detail.phase==='ended'){$('character').classList.remove('speaking');characterStage.setActivity({id:detail.id,speaking:false,level:0});characterStage.setPerformance({id:detail.id,phase:'listening'});}
});
let lastAudioCompleteAt=null;
const audioHandoffs=[];
director.addEventListener('performance',({detail})=>{
  if(detail.phase==='speaking'&&detail.mode==='audio'&&!detail.resumed){
    const startedAt=Date.now();audioHandoffs.push({speechId:detail.id,startedAt,gapMs:lastAudioCompleteAt===null?null:startedAt-lastAudioCompleteAt});
    if(audioHandoffs.length>8)audioHandoffs.shift();
    $('speech-box').dataset.audioHandoffs=JSON.stringify(audioHandoffs);
  }
  engine.audience.dialogue.log({stage:'speech.playback',speechId:detail.id,phase:detail.phase,mode:detail.mode||director.current?.mode||null});if(detail.id===state.currentSpeechId)characterStage.setPerformance(detail);});
director.addEventListener('complete',({detail})=>{if(director.current?.mode==='audio')lastAudioCompleteAt=Date.now();engine.completeSpeech(detail.id);});
let talentCueId=null;
const talentStatus=document.createElement('div');talentStatus.id='talent-status';talentStatus.className='talent-status';talentStatus.hidden=true;
const talentLabel=document.createElement('span'),talentStop=document.createElement('button');
talentStop.type='button';talentStop.textContent='停止表演';talentStop.onclick=()=>engine.stopTalent();talentStatus.append(talentLabel,talentStop);$('speech-box').append(talentStatus);
const talentPhases={preparing:'正在准备声音',ready:'准备好了，等当前这句说完',starting:'准备播放',playing:'表演中',paused:'表演已暂停',completed:'本段已播放完',interrupted:'表演已停止',failed:'声音暂时没准备好'};
talent.addEventListener('state',({detail})=>{
  talentStatus.hidden=!detail;
  if(detail){talentLabel.textContent=`${detail.title} · ${talentPhases[detail.phase]||detail.phase}`;talentStop.hidden=['completed','failed','interrupted'].includes(detail.phase);}
  if(!detail||['completed','failed','interrupted'].includes(detail.phase)){
    talentCueId=null;characterStage.setSpeech({id:null});$('character').classList.remove('speaking');
  }
  syncVoiceControls();
});
talent.addEventListener('cue',({detail})=>{
  talentCueId=detail.id;characterStage.setSpeech({...detail,transition:'smooth'});characterStage.setPerformance({id:detail.id,phase:'speaking'});
  $('speech-text').textContent=detail.text;$('speech-box').dataset.source='talent';$('speech-box').dataset.scope='talent';
  $('character').dataset.mood=detail.mood;$('character').dataset.gesture=detail.gesture;
  $('mood-label').textContent=talent.current?.kind==='singing'?'听 Z 清唱一段':'Z 的单声线演绎';
});
talent.addEventListener('activity',({detail})=>{
  if(!talentCueId)return;characterStage.setActivity({...detail,id:talentCueId});$('character').classList.toggle('speaking',detail.speaking);
});
const voiceButton=document.createElement('button');voiceButton.id='voice-toggle';voiceButton.className='icon-btn';voiceButton.type='button';
document.querySelector('.scene-controls').prepend(voiceButton);
function syncVoiceControls(){
  const on=director.enabled,media=talent.current;
  const status=media?{...director.status,mode:media.phase,message:talentPhases[media.phase]||director.status.message}:director.status;
  voiceButton.innerHTML=icon(on?'sound':'muted');voiceButton.classList.toggle('active',on);voiceButton.dataset.state=status.mode;
  voiceButton.setAttribute('aria-pressed',String(on));voiceButton.setAttribute('aria-label',on?'静音 Z 的语音':'开启 Z 的 Vivi 2.0 语音');
  voiceButton.title=status.message||(on?'Vivi 2.0 已开启':'声音已静音，点击开启');
  const setting=$('setting-voice');if(setting){setting.classList.toggle('on',on);setting.setAttribute('aria-checked',String(on));}
  if($('voice-connection-note'))$('voice-connection-note').textContent=director.available?'Vivi 2.0 · 点击声音按钮后播放':'语音尚未配置，文字互动照常进行';
  if($('voice-volume-value'))$('voice-volume-value').textContent=`${Math.round(director.volume*100)}%`;
}
let lastVoiceNotice='';
director.addEventListener('status',({detail})=>{
  syncVoiceControls();
  if(!director.enabled||director.volume===0)talent.cancel('muted');
  if(director.enabled&&detail.reason&&detail.reason!==lastVoiceNotice){lastVoiceNotice=detail.reason;toast(detail.message);}
});
voiceButton.onclick=async()=>{
  const wanted=!director.enabled;voiceButton.disabled=true;
  try{const enabled=await director.setEnabled(wanted);if(enabled)lastVoiceNotice='';toast(enabled?'Vivi 2.0 已开启，从下一段开始说话':wanted?director.status.message:'声音已静音');}
  finally{voiceButton.disabled=false;syncVoiceControls();}
};
syncVoiceControls();
async function connectText(){
  const availability=await chatClient.configure();chatAvailable=availability.enabled;
  if(engine._destroyed)return availability;
  engine.configureAgent(chatClient,availability);
  if(availability.singingBackground&&!document.getElementById('singing-workbench')){const link=document.createElement('a');link.id='singing-workbench';link.href='/singing';link.target='_blank';link.rel='noopener';link.textContent='点歌准备与试听';link.style.cssText='display:inline-block;margin:8px;color:#72538c;font-size:13px';document.querySelector('.stage-column').append(link);}
  $('text-status').textContent=chatAvailable?'✦ 主 Agent 已连接':'☾ 主 Agent 未连接';
  $('text-status').title='查看主 Agent 与五项玩法的连接状态';
  return availability;
}
$('text-status').onclick=()=>showDialog('<p class="dialog-eyebrow">LET’S TALK UNDER THE MOON</p><h2 class="dialog-title">把心事说给 Z 听</h2><p class="dialog-description" id="text-connection-note"></p><div id="agent-capabilities" class="agent-capabilities"></div><button class="dialog-primary" id="reconnect-text">重新检查连接</button>',()=>{
  $('text-connection-note').textContent=state.agent?.errorCode==='agent_insufficient_balance'?'DeepSeek 返回余额不足，Z 暂时无法生成新内容。补充额度后，点击下方重新检查连接即可继续。':chatAvailable?'主 Agent 自主决定开场、接话、话题过渡和等待。当前才艺只开放唱歌，可以在聊天里点歌。模拟观众贯穿整场直播，围绕已公开的话题自然聊天。':'当前使用本地演示回应。连接 DeepSeek 后启用主 Agent 与全程模拟观众弹幕；当前才艺只开放唱歌。';
  const labels={'entertainment-tarot':'娱乐塔罗','simple-drawing':'简笔画','singing':'角色清唱','voice-performance':'配音表演','audience-reaction':'观众弹幕'};
  const descriptions={
    'entertainment-tarot':'Z 自己选，或由你亲自选；支持单张与三张牌阵。',
    'simple-drawing':'根据你的描述生成简笔草图，可调整颜色、姿态和局部细节，按需打开画板。',
    'audience-reaction':chatAvailable?'全程在线，围绕公开对话自然接话。':'连接模型后全程开放，当前不生成模拟观众弹幕。',
    'voice-performance':director.enabled&&director.available?'可用：Vivi 单声线原创／给定台词演绎，配合 Live2D。':'执行器已接入；开启声音并连接 TTS 后可以演绎。',
    'singing':state.agent?.capabilities?.find(c=>c.id==='singing')?.reason||'预制曲库播放器已接入，等待音频素材。',
  };
  const capabilities=state.agent?.capabilities||[];
  for(const item of capabilities){const row=document.createElement('p');row.textContent=`${labels[item.id]||item.id}：${descriptions[item.id]||item.reason||item.status}`;$('agent-capabilities').append(row);}
  $('reconnect-text').onclick=async()=>{const button=$('reconnect-text');button.disabled=true;button.textContent='正在连接…';const result=await connectText();closeDialog();toast(result.enabled?'Z 已经准备好，开始聊聊吧':'文字服务尚未配置，继续体验剧情模式');};
});

function updateCards(){
  $('current-card-chip').hidden=!state.currentCard||Boolean(state.play);
  if(state.currentCard){$('current-card-chip').firstElementChild.textContent=state.currentCard.symbol;$('current-card-chip').querySelector('small').textContent='今日指引';$('current-card-chip').querySelector('b').textContent=`${state.currentCard.title} · 正位`;}
}
const voteLabels=['月光','星海','晚风'];
const cardEffects=new WeakMap();
function cardFeedback(button,kind,duration){
  if(reduced())return;
  let timers=cardEffects.get(button);if(!timers){timers={};cardEffects.set(button,timers);}
  clearTimeout(timers[kind]);button.classList.add(kind);
  timers[kind]=setTimeout(()=>button.classList.remove(kind),duration);
}
for(let index=0;index<3;index++){
  const button=document.createElement('button');button.className='fortune-option';button.setAttribute('aria-label',`投票选${'ABC'[index]}：${voteLabels[index]}`);
  button.innerHTML=`<span class="vote-card-shell"><span class="vote-card-inner"><span class="vote-card-back"><span class="vote-letter">${'ABC'[index]}</span><span class="card-back-art">${cardBackArt('fortune',index)}</span></span><span class="vote-card-front"><span class="vote-illustration"></span><small class="vote-card-english"></small><strong class="vote-card-symbol">✦</strong><b class="vote-card-title"></b></span></span><span class="card-feedback-badge reveal-badge" hidden></span></span><span class="vote-choice-name"><b>${'ABC'[index]} · ${voteLabels[index]}</b><small class="my-choice-tag" hidden>已选中</small></span><span class="vote-track"><i></i></span><span class="vote-numbers"><span class="vote-count">—</span><b class="vote-percent">—</b></span>`;
  button.onclick=()=>{const fortune=state.fortune;if(!fortune)return;if(state.paused){toast('先继续直播，再把你的直觉投出来吧');return;}if(fortune.phase==='voting'){if(engine.vote(index))toast(`收到！你选了 ${'ABC'[index]} · ${voteLabels[index]}，锁票前还可以改选`);}else if(fortune.options[index]?.revealed){cardDialog(fortune.options[index].card);}else toast('Z 会按 A、B、C 的顺序翻牌，下一张马上就到');};
  $('fortune-choices').append(button);
}
function votePercents(options,total){
  if(!total)return [0,0,0];
  const raw=options.map(option=>option.votes/total*100),rounded=raw.map(Math.floor);let remainder=100-rounded.reduce((a,b)=>a+b,0);
  const order=raw.map((v,i)=>({i,f:v-rounded[i]})).sort((a,b)=>b.f-a.f);
  for(let i=0;i<remainder;i++)rounded[order[i%order.length].i]++;
  return rounded;
}
function renderFortune(){
  if(state.play){$('fortune-panel').hidden=true;$('today-start').textContent='☾ 塔罗';return;}
  const fortune=state.stage==='fortune'||fortuneReviewOpen?state.fortune:null;
  $('today-start').textContent=fortuneReviewOpen?'☾ 收起今日运势':'☾ 今日运势';
  const enteringFortune=Boolean(fortune)&&!fortuneWasVisible;fortuneWasVisible=Boolean(fortune);
  $('fortune-panel').hidden=!fortune||Boolean(state.lottery?.open);$('live-stage').classList.toggle('fortune-mode',Boolean(fortune));
  document.querySelector('.stage-column').classList.toggle('has-fortune',Boolean(fortune));
  if(!fortune)return;
  if(enteringFortune&&matchMedia('(max-width: 800px)').matches)requestAnimationFrame(()=>{if(!fortuneWasVisible)return;$('fortune-panel').scrollIntoView({block:'center',behavior:reduced()?'instant':'smooth'});});
  const phase=fortune.phase,options=fortune.options,percents=votePercents(options,fortune.totalVotes);$('fortune-panel').dataset.phase=phase;
  $('fortune-title').textContent=phase==='inviting'?'今晚，直觉站哪边？':phase==='voting'?'你的直觉，投给哪一张？':phase==='complete'?'三份指引，都接住了吗？':'月光揭晓，一张一张看';
  $('fortune-intro').textContent=phase==='inviting'?'别急着找标准答案，跟着第一眼的心动选一张。':phase==='voting'?'点选 A / B / C，或发弹幕「我选 A」。每人一票，锁票前可以改选。':'投票已锁定。每种选择都有自己的小线索，Z 会依次为大家解读。';
  $('vote-timer').textContent=phase==='voting'?`${Math.max(0,Math.ceil(fortune.remainingSeconds))} 秒`:phase==='inviting'?'准备入席':phase==='complete'?'全部揭晓':'已锁票';
  $('vote-timer').classList.toggle('counting',phase==='voting');
  $('vote-total').textContent=fortune.totalVotes?`${number(fortune.totalVotes)} 票 · 全场共同选择`:'等待第一份直觉';
  const chosen=fortune.myVote!==null&&fortune.myVote!==undefined;
  $('my-vote').textContent=chosen?`你选择了 ${'ABC'[fortune.myVote]}`:'你还没有投票';
  options.forEach((option,index)=>{
    const button=$('fortune-choices').children[index],selected=fortune.myVote===index,revealed=Boolean(option.revealed);
    if(selected&&!button.classList.contains('chosen'))cardFeedback(button,'just-selected',900);
    if(revealed&&!button.classList.contains('is-revealed'))cardFeedback(button,'just-revealed',1300);
    if(!selected)button.classList.remove('just-selected');if(!revealed)button.classList.remove('just-revealed');
    button.disabled=phase==='inviting';button.classList.toggle('chosen',selected);button.classList.toggle('is-revealed',revealed);button.classList.toggle('is-active',fortune.activeIndex===index);button.classList.toggle('winner',fortune.winnerIndex===index&&phase!=='voting'&&phase!=='inviting');
    const badge=button.querySelector('.card-feedback-badge');badge.hidden=!revealed;badge.textContent=fortune.activeIndex===index&&phase==='revealing'?'✦ 正在解读':'✓ 已翻开';
    button.setAttribute('aria-pressed',String(fortune.myVote===index));button.querySelector('.my-choice-tag').hidden=fortune.myVote!==index;
    button.querySelector('.vote-count').textContent=`${number(option.votes)} 票`;button.querySelector('.vote-percent').textContent=fortune.totalVotes?`${percents[index]}%`:'—';button.querySelector('.vote-track i').style.width=`${percents[index]}%`;
    if(option.card&&option.revealed){if(button.dataset.art!==option.card.name){button.dataset.art=option.card.name;button.querySelector('.vote-illustration').innerHTML=tarotArt(option.card.name);}button.querySelector('.vote-card-symbol').textContent=option.card.symbol;button.querySelector('.vote-card-title').textContent=option.card.title;button.querySelector('.vote-card-english').textContent=option.card.name;button.setAttribute('aria-label',`${'ABC'[index]}牌 ${option.card.title}，${percents[index]}%观众选择，点击查看解读`);}else button.setAttribute('aria-label',`投票选${'ABC'[index]}：${option.label||voteLabels[index]}`);
  });
  const active=options[fortune.activeIndex];$('fortune-insight').hidden=!active?.revealed;
  if(active?.revealed){$('insight-title').textContent=`${'ABC'[fortune.activeIndex]} · ${active.card.title} 正位`;$('insight-text').textContent=active.card.meaning;}
  $('vote-note').textContent=phase==='voting'?'演示观众参与 · 每人一票，可改选':phase==='revealing'?'逐张解读中 · 点已翻开的牌可复看':'演示直播间投票 · 票数为本地模拟';
  $('fortune-next').textContent=fortuneReviewOpen&&state.stage!=='fortune'?'收好指引，继续聊天 →':phase==='inviting'?'开始投票 →':phase==='voting'?'锁票，揭晓第一张 →':phase==='complete'?(state.lottery?'继续聊天 →':'收下月光来信 →'):fortune.activeIndex===null?'揭晓第一张 →':fortune.activeIndex===2?(state.lottery?'听完解读，继续聊天 →':'听完解读，收下小惊喜 →'):'揭晓下一张 →';
}
$('fortune-next').onclick=()=>{if(fortuneReviewOpen&&state.stage!=='fortune'){fortuneReviewOpen=false;render(state);return;}if(state.paused)pause(false);const phase=state.fortune?.phase;requestProgress(()=>{if(phase==='inviting')engine.startVoting();else if(phase==='voting')engine.closeVoting();else if(phase==='revealing')engine.revealNext();else engine.advance();});};

let lotteryWasOpen=false,latestRewardId=null;
for(let index=0;index<3;index++){
  const button=document.createElement('button');button.className='lottery-card';button.type='button';
  button.innerHTML=`<span class="lottery-card-inner"><span class="lottery-card-back"><span class="card-back-art">${cardBackArt('lottery',index)}</span><span class="lottery-seal">${['一','二','三'][index]}</span></span><span class="lottery-card-front"><span class="reward-illustration"></span><b class="reward-name"></b><small>MOON ISLAND</small></span></span><span class="card-feedback-badge reveal-badge" hidden>✓ 已获得</span><span class="lottery-card-caption">第${['一','二','三'][index]}封 · 等你拆开</span>`;
  button.onclick=()=>{if(state.paused)pause(false);const card=state.lottery?.cards[index];if(!card)return;if(card.revealed){latestRewardId=card.id;renderLottery();return;}latestRewardId=card.id;engine.revealLotteryCard(index,'player');};
  $('lottery-choices').append(button);
}
function renderLottery(){
  const lottery=state.lottery,open=Boolean(lottery?.open);$('lottery-panel').hidden=!open;
  document.querySelector('.stage-column').classList.toggle('has-lottery',open);
  const entering=open&&!lotteryWasOpen;lotteryWasOpen=open;
  if(!lottery){latestRewardId=null;$('lottery-trigger').textContent='✉ 幸运抽奖';for(const b of $('lottery-choices').children)b.classList.remove('is-revealed','just-revealed','winner');return;}
  $('lottery-trigger').textContent=lottery.islandInvited?'✉ 我的登岛邀请':lottery.phase==='complete'?'✉ 我的月光来信':'✉ 幸运抽奖';
  if(!open)return;
  if(entering&&matchMedia('(max-width: 800px)').matches)requestAnimationFrame(()=>{if(lotteryWasOpen)$('lottery-panel').scrollIntoView({block:'center',behavior:reduced()?'instant':'smooth'});});
  const count=lottery.cards.filter(card=>card.revealed).length;
  $('lottery-count').textContent=`${count} / 3`; $('lottery-panel').dataset.phase=count===3?'complete':'revealing';
  $('lottery-title').textContent=count===3?'三份心意，都为你而来':'月光，给你捎来一封信';
  lottery.cards.forEach((card,index)=>{
    const button=$('lottery-choices').children[index];
    if(card.revealed&&!button.classList.contains('is-revealed'))cardFeedback(button,'just-revealed',1300);
    button.classList.toggle('is-revealed',card.revealed);button.classList.toggle('winner',card.revealed&&card.id==='island-invitation');
    button.querySelector('.card-feedback-badge').hidden=!card.revealed;
    button.setAttribute('aria-label',card.revealed?`查看${card.reward.title}`:`翻开第${index+1}张幸运卡`);
    button.setAttribute('aria-pressed',String(card.revealed));button.querySelector('.lottery-card-front').setAttribute('aria-hidden',String(!card.revealed));
    button.querySelector('.lottery-card-caption').textContent=card.revealed?'✓ 已收入来信册':`第${['一','二','三'][index]}封 · 等你拆开`;
    if(card.revealed&&button.dataset.art!==card.id){button.dataset.art=card.id;button.querySelector('.reward-illustration').innerHTML=rewardArt(card.id);button.querySelector('.reward-name').textContent=card.reward.title;}
  });
  const latest=lottery.cards.find(card=>card.id===latestRewardId&&card.revealed)||lottery.cards.find(card=>card.id==='island-invitation'&&card.revealed)||lottery.cards.find(card=>card.revealed);
  $('lottery-reward-title').textContent=latest?.reward.title||'海风已经把信送到窗边';
  $('lottery-reward-description').textContent=latest?.reward.description||'三张都属于你，先从最心动的那张开始吧。';
  $('lottery-note').textContent=lottery.islandInvited?'登岛邀请已收好 · Z 会到码头接你':'月亮小岛 · 今晚的秘密来信';
  $('lottery-close').textContent=count===3?'收好来信，回到小屋 →':'稍后再拆 →';
}
$('lottery-trigger').onclick=()=>{if(state.paused)pause(false);if(state.lottery)engine.openLottery();else engine.triggerStory('moonlight-invitation');};
$('lottery-close').onclick=()=>{if(state.paused)pause(false);engine.closeLottery();};

function cardDialog(cardOverride=null){
  const card=cardOverride||state.currentCard;if(!card){toast('牌还没有翻开，先跟着直觉选一张吧');return;}
  showDialog('<p class="dialog-eyebrow" id="card-eyebrow"></p><h2 class="dialog-title" id="card-title"></h2><div class="result-card"><span id="card-symbol"></span><b id="card-name"></b></div><p class="dialog-description" id="card-meaning"></p><div class="result-advice" id="card-advice"></div><button class="dialog-primary" id="card-ask">和 Z 聊聊这张牌</button><p class="demo-note">牌给你一个角度，决定权仍稳稳在你手里。</p>',()=>{
    $('card-eyebrow').textContent=card.name;$('card-title').textContent=`${card.title} · 正位`;$('card-symbol').textContent=card.symbol;$('card-name').textContent=card.title;
    $('card-meaning').textContent=card.meaning;$('card-advice').textContent=`可以试着做的小行动：${card.advice}`;
    $('card-ask').onclick=()=>{closeDialog();send(`我想听你多说说${card.title}这张牌。`);};
  });
}
$('current-card-chip').onclick=()=>cardDialog();

function render(next){
  const wasPaused=state.paused;state=next;
  if(wasPaused!==state.paused){if(state.paused)director.pause();else director.resume();}
  $('viewers').textContent=number(state.viewers);$('audience-count').textContent=number(state.viewers);$('likes').textContent=number(state.likes);$('stage-label').textContent=state.stageLabel;
  $('follow').textContent=state.followed?'✓ 已关注':'＋ 关注';$('follow').classList.toggle('is-followed',state.followed);$('follow').setAttribute('aria-pressed',String(state.followed));
  $('rank-number').textContent=`#${Math.max(1,14-Math.floor((state.likes-2380)/1000))}`;
  $('pause-overlay').hidden=!state.paused;$('live-stage').classList.toggle('paused',state.paused);$('pause').innerHTML=icon(state.paused?'play':'pause');$('pause').setAttribute('aria-label',state.paused?'继续直播演示':'暂停直播演示');
  if(!state.currentSpeech){director.cancel?.({preserveRetained:true});if(!['starting','playing','paused'].includes(state.media?.phase))$('character').classList.remove('speaking');}
  const waiting=['pending','queued'].includes(state.replyStatus);
  $('reply-status').classList.toggle('awaiting-reply',waiting);
  $('reply-status-text').textContent=waiting?(state.currentSpeech?'弹幕已收到，等 Z 说完这段再回应':'Z 正在整理你的问题，稍等一下')
    :state.replyStatus==='replying'?'Z 正在回应这条弹幕，听她说完吧'
    :state.replyStatus==='replied'?'这条已经聊完啦，继续和 Z 说说吧'
    :state.replyStatus==='seen'?'弹幕已送达，Z 会挑选话题和大家聊':'发一条弹幕，让 Z 认识你';
  for(const message of state.messages||[]){
    if(!message.mine)continue;
    const receipt=$('messages').querySelector(`[data-message-id="${message.id}"] .message-receipt`);
    if(receipt){receipt.textContent=receiptText(message.replyStatus);receipt.dataset.status=message.replyStatus||'seen';}
  }
  $('speech-skip').title=state.queuedAction?'已安排，听完当前这段后继续':'听完当前这段后继续';
  $('speech-skip').setAttribute('aria-label',state.queuedAction?'已安排，听完后继续':'听完这段后继续');
  $('speech-skip').setAttribute('aria-busy',String(Boolean(state.queuedAction)));
  if(state.agent){$('text-status').textContent=state.agent.errorCode==='agent_insufficient_balance'?'☾ DeepSeek 余额不足':state.agent.errorCode==='agent_review_version_mismatch'?'☾ 请刷新页面后重试':state.agent.source==='retrying'?'☾ Z 正在重新接话':state.agent.source==='unavailable'?'☾ 主 Agent 连接异常':state.agent.source==='local-fallback'?'☾ 主 Agent 暂用本地回应':state.agent.enabled?'✦ 主 Agent 已连接':'☾ 主 Agent 未连接';}
  updateCards();renderFortune();renderLottery();
}
engine.addEventListener('state',()=>{
  $('speech-box').dataset.turnIntent=engine.speechPlan?.result.continuation||'';
  $('speech-box').dataset.backgroundTrace=JSON.stringify(engine.audience.dialogue.traces.filter(e=>e.stage.startsWith('topic.')||e.stage.startsWith('handoff.')||e.stage==='audience.pool.ready').slice(-12));
});
engine.addEventListener('state',e=>render(e.detail));engine.addEventListener('message',e=>renderMessage(e.detail));engine.addEventListener('speak',e=>speak(e.detail));engine.addEventListener('effect',e=>effect(e.detail));
engine.addEventListener('state',({detail})=>{characterStage.setPaused(detail.paused);characterStage.setWaiting(['pending','queued'].includes(detail.replyStatus));if(!detail.currentSpeech&&!engine._mediaPlaying()&&!director.thinkingCue)characterStage.setSpeech({id:null});});
engine.addEventListener('call-listening',()=>characterStage.acknowledgeListening());

function send(text){
  if(state.paused)pause(false);
  const sent=engine.sendMessage(text);if(!sent){toast(text.trim()?'稍等一小下，再把这句心事交给 Z':'先写一句想对 Z 说的话吧');return false;}
  characterStage.noticeMessage();
  toast(engine.getState().call?.active?'连麦消息已发送':'弹幕已发送');
  return true;
}
function submitChat(){const input=$('chat-input');if(composing)return;if(send(input.value)){input.value='';$('input-counter').textContent='';$('emoji-popover').hidden=true;}}
$('send-message').addEventListener('click',submitChat);
$('chat-form').addEventListener('submit',event=>{event.preventDefault();submitChat();});
$('chat-input').addEventListener('compositionstart',()=>{composing=true;});
$('chat-input').addEventListener('compositionend',()=>{composing=false;});
$('chat-input').addEventListener('keydown',event=>{if(event.key!=='Enter')return;if(event.isComposing||composing||event.keyCode===229)return;event.preventDefault();submitChat();});
$('chat-input').addEventListener('input',()=>{$('input-counter').textContent=$('chat-input').value.length>90?`${$('chat-input').value.length}/120`:'';});
document.querySelectorAll('[data-prompt]').forEach(button=>button.onclick=()=>send(button.dataset.prompt));
$('greet-z').onclick=()=>send('嗨，Z！今天想和你聊聊天。');
$('live2d-greet').onclick=()=>$('greet-z').click();
for(const emoji of ['🌙','✨','💜','🌷','🥰','👏','🔮','☀️','🍀','晚安']){const b=document.createElement('button');b.type='button';b.textContent=emoji;b.onclick=()=>{const input=$('chat-input');input.value=(input.value+emoji).slice(0,120);input.focus();$('emoji-popover').hidden=true;};$('emoji-popover').append(b);}
$('emoji').onclick=()=>{$('emoji-popover').hidden=!$('emoji-popover').hidden;};
document.addEventListener('click',e=>{if(!$('chat-form').contains(e.target))$('emoji-popover').hidden=true;});

function effect(detail){
  const layer=$('effect-layer');while(layer.children.length>30)layer.firstElementChild.remove();
  if(detail.type==='like'){
    for(let i=0;i<(reduced()?1:3);i++){const h=document.createElement('span');h.className='floating-heart';h.textContent=['♥','♡','✧'][i%3];h.style.setProperty('--drift',`${Math.random()*90-45}px`);h.style.right=`${30+Math.random()*35}px`;h.style.animationDelay=`${i*.12}s`;layer.append(h);setTimeout(()=>h.remove(),3000);}
  }else{
    layer.querySelector('.scene-burst')?.remove();const el=document.createElement('div');el.className='scene-burst';const symbol=document.createElement('span'),label=document.createElement('p');symbol.textContent=detail.icon;label.textContent=detail.label;el.append(symbol,label);layer.append(el);setTimeout(()=>el.remove(),3000);
    if(!reduced())for(let i=0;i<9;i++){const s=document.createElement('span');s.className='scene-spark';s.textContent=['✦','✧','·'][i%3];s.style.left='50%';s.style.top='44%';s.style.color=i%2?'#efd89c':'#d8b5f2';s.style.setProperty('--dx',`${Math.cos(i*.7)*160}px`);s.style.setProperty('--dy',`${Math.sin(i*.7)*110}px`);layer.append(s);setTimeout(()=>s.remove(),1900);}
  }
}
$('follow').onclick=()=>{const on=engine.toggleFollow();toast(on?'关注成功，下次月亮升起时再见':'已取消关注，随时欢迎回来');};
$('like').onclick=()=>{engine.like();$('like').classList.add('liked');};

function showToday(){
  if(state.paused)pause(false);
  if(state.lottery?.open)engine.closeLottery();
  if(state.stage==='intro')requestProgress(()=>engine.advance());
  else if(state.stage!=='fortune'&&state.fortune){fortuneReviewOpen=!fortuneReviewOpen;render(state);}
  if(state.stage==='fortune'||fortuneReviewOpen)$('fortune-panel').scrollIntoView({block:'nearest',behavior:reduced()?'instant':'smooth'});
}
function programDialog(){showDialog('<p class="dialog-eyebrow">LIVE CONVERSATION</p><h2 class="dialog-title">和 Z 聊聊天</h2><p class="dialog-description">Z 会自己决定聊什么、接谁的话，以及何时唱一段已准备好的歌。也可以直接在弹幕里点题，没有固定节目。</p><button class="dialog-primary" id="conversation-restart">重新开始对话</button>',()=>{$('conversation-restart').onclick=()=>{closeDialog();replay();};});}
$('program').onclick=programDialog;
$('today-start').onclick=showToday;
function requestProgress(action){
  const previous=state.queuedAction;action();
  if(state.queuedAction&&state.queuedAction!==previous)toast('记下啦，等 Z 说完这段再继续');
}
$('speech-skip').hidden=true;

function syncPlayback(){engine.setPaused(userPaused||pageSuspended||hostPaused||document.hidden);}
function pause(value){userPaused=Boolean(value);syncPlayback();}
$('pause').onclick=()=>pause(!state.paused);$('resume').onclick=()=>pause(false);
$('danmaku').onclick=()=>{danmakuOn=!danmakuOn;$('danmaku').classList.toggle('active',danmakuOn);$('danmaku-layer').hidden=!danmakuOn;$('danmaku').setAttribute('aria-label',danmakuOn?'关闭画面弹幕':'开启画面弹幕');toast(danmakuOn?'画面弹幕已开启':'画面弹幕已关闭，右侧记录会继续更新');};
$('fullscreen').onclick=async()=>{try{if(document.fullscreenElement)await document.exitFullscreen();else if($('live-stage').requestFullscreen)await $('live-stage').requestFullscreen();else toast('当前浏览器不支持全屏，可旋转屏幕获得更大画面');}catch{toast('当前环境不支持全屏，直播会继续播放');}};
$('share').onclick=async()=>{try{await navigator.clipboard.writeText(location.href);toast('直播间链接已复制，把月光分享给朋友吧');}catch{showDialog('<p class="dialog-eyebrow">SHARE THE MOONLIGHT</p><h2 class="dialog-title">邀请朋友来小屋</h2><p class="dialog-description">复制下面的地址，一起听 Z 聊聊。</p><input class="dialog-input" id="share-url" readonly><p class="demo-note">部署版本默认仅创建者可访问。</p>',()=>{$('share-url').value=location.href;$('share-url').select();});}};
function openCallVoiceSettings(){
  showDialog('<h2 class="dialog-title">连麦声音设置</h2><p class="dialog-description">只需设置一次，帮助 Z 在嘈杂环境里听清你。</p><p class="dialog-description">找个安静的位置，自然聊聊今天，约 25 秒。</p><p id="call-voice-setup-status" class="dialog-description" role="status">正在检查…</p><progress id="call-voice-setup-progress" max="25" value="0" hidden aria-label="声音录入进度" style="width:100%;margin-top:16px"></progress><button id="call-voice-setup-start" class="dialog-primary" disabled>开始录入</button><button id="call-voice-setup-close" class="dialog-secondary">返回设置</button>');
  const dialog=$('dialog'),status=$('call-voice-setup-status'),start=$('call-voice-setup-start'),progress=$('call-voice-setup-progress');
  const controller=new AbortController();let recording=null,closed=false,restorePause=null;
  const restore=()=>{if(restorePause!==null){const value=restorePause;restorePause=null;pause(value);}};
  const cleanup=()=>{if(closed)return;closed=true;controller.abort();recording?.abort();restore();dialog.removeEventListener('close',cleanup);document.removeEventListener('visibilitychange',visibility);window.removeEventListener('pagehide',finish);};
  const finish=()=>{cleanup();closeDialog();};
  const visibility=()=>{if(document.hidden)finish();};
  dialog.addEventListener('close',cleanup);document.addEventListener('visibilitychange',visibility);window.addEventListener('pagehide',finish);
  $('call-voice-setup-close').onclick=()=>{cleanup();$('settings').click();};
  void fetch('/api/voiceprint/config',{cache:'no-store',signal:controller.signal}).then(async response=>{
    if(!response.ok)throw Error('unavailable');const config=await response.json();if(closed)return;
    if(!config.requested){status.textContent='当前连麦无需声音设置。';start.hidden=true;return;}
    if(!config.credentialsConfigured)throw Error('unavailable');
    if(!config.needsEnrollment){status.textContent='已完成，可以直接点击麦克风连麦。';start.hidden=true;return;}
    status.textContent='准备好后点击开始。';start.disabled=false;
  }).catch(()=>{if(!closed){status.textContent='暂时无法设置，请稍后再试。';start.hidden=true;}});
  start.onclick=async()=>{
    if(closed||recording)return;start.disabled=true;start.textContent='录入中…';status.textContent='请允许使用麦克风。';
    restorePause=userPaused;pause(true);const rec=recording=new VoiceprintRecognition();
    rec.onsetup=event=>{if(closed)return;progress.hidden=false;if(event.phase==='enrolling'){progress.value=25-event.remaining;status.textContent='正在录音，自然说话就好。';}else if(event.phase==='registering'){progress.value=25;status.textContent='正在保存…';}};
    try{
      await rec.enroll();if(closed)return;status.textContent='已完成，回到直播即可连麦。';progress.hidden=true;start.hidden=true;
    }catch(error){
      if(closed)return;progress.hidden=true;start.disabled=false;start.textContent='重新录入';
      status.textContent=error.name==='NotAllowedError'?'请允许使用麦克风后重试。':error.message==='voiceprint_enrollment_too_quiet'?'没有录到足够的讲话，请再试一次。':'这次未完成，请稍后重试。';
    }finally{rec.abort();if(recording===rec)recording=null;restore();}
  };
}
$('settings').onclick=()=>{
  showDialog('<p class="dialog-eyebrow">MAKE YOURSELF AT HOME</p><h2 class="dialog-title">让小屋更合你心意</h2><div class="settings-row"><span>角色形象<small id="character-connection-note"></small></span><select class="character-mode-select" id="setting-character" aria-label="角色形象"><option value="live2d">Live2D 示例 · Haru</option><option value="manga">Z 的原画</option></select></div><p class="character-example-note">Haru 是 Live2D Inc. 官方示例，暂时代替 Z 展示动态效果；后续可换成 Z 的专属模型。<a href="/live2d/licenses/NOTICE.txt" target="_blank" rel="noopener">素材来源与声明</a></p><button class="dialog-secondary" id="setting-character-retry" hidden>重新加载动态形象</button><div class="settings-row"><span>Vivi 2.0 语音<small id="voice-connection-note"></small></span><button class="switch" id="setting-voice" role="switch" aria-label="Vivi 2.0 语音"></button></div><div class="settings-row"><span>语音音量<small id="voice-volume-value"></small></span><input id="setting-volume" type="range" min="0" max="100" step="5" aria-label="Z 的语音音量" style="width:120px;max-width:40%"></div><div class="settings-row"><span>画面弹幕<small>右侧聊天记录始终保留</small></span><button class="switch" id="setting-chat" role="switch" aria-label="画面弹幕"></button></div><div class="settings-row"><span>轻柔动画<small>减少肢体摆动和漂浮效果</small></span><button class="switch" id="setting-motion" role="switch" aria-label="轻柔动画"></button></div><button class="dialog-primary" id="settings-done">好了，继续看月亮</button><p class="demo-note"><a href="/performance" target="_blank" rel="noopener">查看表情与动作预览 ↗</a></p><button class="dialog-secondary" id="settings-replay">重新开始对话</button><p class="demo-note">模拟观众由模型生成。自由聊天的连接状态可在弹幕区查看。</p>',()=>{
    const sync=()=>{for(const [id,on]of[['setting-chat',danmakuOn],['setting-motion',lowMotion]]){$(id).classList.toggle('on',on);$(id).setAttribute('aria-checked',String(on));}};sync();
    const callVoice=document.createElement('button');callVoice.id='setting-call-voice';callVoice.className='dialog-secondary';callVoice.textContent='连麦声音设置';callVoice.onclick=openCallVoiceSettings;$('settings-done').before(callVoice);
    syncVoiceControls();$('setting-volume').value=String(Math.round(director.volume*100));$('setting-volume').oninput=event=>director.setVolume(Number(event.target.value)/100);$('setting-voice').onclick=()=>voiceButton.click();
    syncCharacterSettings();$('setting-character').onchange=event=>{void characterStage.setMode(event.target.value);};$('setting-character-retry').onclick=()=>{void characterStage.retry();};
    $('setting-chat').onclick=()=>{$('danmaku').click();sync();};$('setting-motion').onclick=()=>{lowMotion=!lowMotion;document.body.classList.toggle('low-motion',lowMotion);characterStage.setReducedMotion(reduced());sync();};$('settings-done').onclick=closeDialog;$('settings-replay').onclick=()=>{closeDialog();replay();};
  });
};
$('back').onclick=()=>showDialog('<p class="dialog-eyebrow">MOON ISLAND · AFTER DARK</p><div class="profile-symbol">☾</div><h2 class="dialog-title">月亮小岛，灯还亮着</h2><p class="dialog-description">海风经过邮局，穿过花园，最后停在 Z 的窗边。今晚的小屋只为你开着，进来坐一会儿吧。</p><button class="dialog-primary" id="return-live">回到 Z 的聊天小屋</button><button class="dialog-secondary" id="island-replay">重新开始今晚的相遇</button>',()=>{$('return-live').onclick=closeDialog;$('island-replay').onclick=()=>{closeDialog();replay();};});
function replay(){
  userPaused=false;chatClient.cancel();director.cancel();$('messages').replaceChildren();$('danmaku-layer').replaceChildren();$('effect-layer').replaceChildren();fortuneReviewOpen=false;fortuneWasVisible=false;elapsed=0;engine.replay();syncPlayback();updateLeaders();toast('月亮灯重新亮起，今晚的相遇从这里开始');
}
const clock=setInterval(()=>{if(!state.paused){elapsed++;const h=String(Math.floor(elapsed/3600)).padStart(2,'0'),m=String(Math.floor(elapsed/60)%60).padStart(2,'0'),s=String(elapsed%60).padStart(2,'0');$('duration').textContent=`${h}:${m}:${s}`;}},1000);
document.addEventListener('visibilitychange',syncPlayback);
// A page can be restored from the browser's back/forward cache. Keep the
// engine intact while suspended; a real unload disposes timers automatically.
window.addEventListener('pagehide',()=>{pageSuspended=true;syncPlayback();director.pause();});
window.addEventListener('pageshow',event=>{pageSuspended=false;syncPlayback();if(event.persisted&&!state.paused)director.resume();});

// Animate source-aligned features while retaining the exact Figma illustration.
function configurePuppet(){setupMangaFace({overlay:$('face-overlay')});}
// BEGIN UNITY LIVE BRIDGE — lifecycle only, never a game-state authority.
function mountUnityLiveBridge({window, engine, director, talent, characterStage, chatClient, setHostPaused, onClose}) {
  const query = new URLSearchParams(window.location.search);
  const sessionId = query.get('session');
  if (query.get('embed') !== 'unity' || query.get('bridge') !== '1' || !/^[a-f0-9]{32}$/.test(sessionId || '')) return null;
  let transport = null, closed = false, started = false;
  const seen = new Map();
  const envelope = (type, extra = {}) => ({channel:'project-z-live', version:1, sessionId, type, ...extra});
  const emit = (type, extra) => { if (!closed && transport) transport.postMessage(JSON.stringify(envelope(type, extra))); };
  const dispose = () => {
    if (closed) return;
    closed = true; setHostPaused(true);
    transport?.removeEventListener('message', receive);
    window.removeEventListener('vuplexready', connect);
    window.removeEventListener('keydown', keydown);
    window.removeEventListener('pagehide', dispose);
    engine.removeEventListener('state', report);
    chatClient.cancel(); engine.destroy(); director.destroy(); characterStage.destroy(); onClose();
  };
  function receive(event) {
    let message;
    try { message = typeof event.data === 'string' ? JSON.parse(event.data) : event.data; } catch { return; }
    if (closed || !message || message.channel !== 'project-z-live' || message.version !== 1 || message.sessionId !== sessionId) return;
    if (message.type === 'close') { emit('closed'); dispose(); return; }
    if (!['start', 'visibility'].includes(message.type) || typeof message.paused !== 'boolean' || typeof message.muted !== 'boolean') return;
    if (message.type === 'visibility' && !started) return;
    started = true;
    if (message.muted) void director.setEnabled(false);
    setHostPaused(message.paused);
    emit('state', {paused:message.paused, muted:message.muted});
  }
  function report({detail}) {
    if (!started || closed) return;
    for (const [kind, task] of [['tarot', detail.play?.tarot], ['drawing', detail.play?.drawing], [detail.media?.kind, detail.media]]) {
      if (!task?.id || !task.phase || !['tarot','drawing','singing','voice-performance'].includes(kind)) continue;
      const key = kind + ':' + task.id;
      if (seen.get(key) === task.phase) continue;
      seen.set(key, task.phase);
      if (seen.size > 32) seen.delete(seen.keys().next().value);
      emit('skillState', {kind, taskId:task.id, phase:task.phase});
    }
  }
  function keydown(event) {
    if (event.key !== 'Escape' || event.isComposing) return;
    event.preventDefault(); emit('closeRequested'); dispose();
  }
  function connect() {
    if (closed || transport || !window.vuplex) return;
    transport = window.vuplex;
    transport.addEventListener('message', receive);
    emit('ready');
  }
  setHostPaused(true);
  void director.setEnabled(false);
  engine.addEventListener('state', report);
  window.addEventListener('vuplexready', connect);
  window.addEventListener('keydown', keydown);
  window.addEventListener('pagehide', dispose);
  connect();
  return {dispose, requestClose:()=>{emit('closeRequested');dispose();}};
}
// END UNITY LIVE BRIDGE

if(embeddedInUnity){
  const style=document.createElement('style');style.textContent='#audience-panel,#rank,#viewers-button,#singing-workbench{display:none!important}';document.head.append(style);
  // External workbench/popups stay outside this first embedded evaluation flow.
  document.addEventListener('click',event=>{const link=event.target.closest?.('a');if(link){event.preventDefault();toast('此链接请在独立网页中打开');}},true);
}
const unityBridge=mountUnityLiveBridge({window,engine,director,talent,characterStage,chatClient,setHostPaused:value=>{hostPaused=value;syncPlayback();},onClose:()=>clearInterval(clock)});
let callUI;
if(unityBridge)$('back').onclick=unityBridge.requestClose;
else mountGameReturn({window,button:$('back'),shutdown:()=>{
  userPaused=true;pageSuspended=true;syncPlayback();
  callUI?.destroy();chatClient.cancel();engine.destroy();director.destroy();characterStage.destroy();clearInterval(clock);
  document.querySelectorAll('audio,video').forEach(media=>{media.pause();media.muted=true;});
},showNotice:retry=>showDialog('<p class="dialog-eyebrow">BACK TO THE ISLAND</p><h2 class="dialog-title">回到月亮小岛</h2><p class="dialog-description">直播已停止。请在浏览器提示中选择打开游戏。</p><p class="demo-note">如果没有唤回，请先启动已安装的 Mac 游戏，或手动切回游戏继续探索。Unity 编辑器预览不会被此链接唤回。</p><button class="dialog-primary" id="retry-game-return">再次打开游戏</button>',()=>{$('retry-game-return').onclick=retry;})});
document.body.classList.toggle('low-motion',lowMotion);configurePuppet();render(state);mountPlayUI({engine,toast,showDialog,closeDialog,autonomous:true});callUI=mountCallUI({engine,director,toast,unpause:()=>pause(false)});engine.start();syncPlayback();connectText();director.configure();void characterStage.setMode(characterStage.preferred,{persist:false});
