'use strict';
const copy = {
  zh: {projectName:'来自月亮的 Z',review:'月光直播 · 界面设计预览',reset:'重置',topic:'今晚，听见小岛',gold:'金币',chat:'直播间',giftAuthor:'阿树',giftVerb:'送出',starBottle:'星星瓶',reply:'听潮，这个点子不错。',replySecond:'下一次，想听你亲手录下来的海。',toZ:'送给 Z',giftTitle:'一点心意',smallGift:'小礼物',owned:'已拥有',giftDescription:'把白天挑好的心意，送到她面前。',sendGift:'赠送给 Z',giftNote:'使用 1 份库存 · 金币不变',onAir:'一起聊聊',callTitle:'随机连麦',callQuestion:'小岛上，什么声音让你停下脚步？',callDescription:'报名后随机抽选。抽中时，再由你决定是否接通。',joinCall:'报名连麦',callNote:'自愿参与 · 不会自动开启麦克风',nextSegment:'下一段节目',advanceTitle:'继续听她说',advanceDescription:'结束当前片段，进入主节目。',continue:'继续观看',advanceNote:'发送弹幕、赠礼和报名不推进时间',inputLabel:'发送弹幕',gifts:'礼物',call:'连麦',previewNote:'可体验弹幕、礼物与报名状态。当前为本地界面预览，Z 暂为静态设定图。',placeholder:'说点什么，让她认识你…',you:'你',sent:'已发送',emptyGift:'已赠送',emptyNote:'库存已用完 · 金币仍为 74',giftSent:'小礼物已赠送 · 库存 −1',queued:'已报名',queueDescription:'你已在候选名单中。等待本轮抽选，也可以继续发弹幕。',cancelCall:'取消报名',callJoined:'已加入本轮抽选',callCanceled:'已取消报名',advanceDone:'主节目',advanceToast:'已预览节目推进至 20:40',close:'关闭',hideChat:'收起弹幕',showChat:'展开弹幕',immersive:'沉浸预览',leaveImmersive:'退出沉浸预览',artAlt:'银白长发的 Z 坐在海边窗前，倾听屏幕另一端的观众。',liveLabel:'Z 的手机直播',chatLabel:'观众弹幕',previewViewers:'示例观众数',name1:'小岛来信',text1:'今天的海，好像比昨天更安静。',name2:'听潮',text2:'能把码头的铃声做成开场节拍吗？',name3:'晚风',text3:'想听！把小岛的声音留在歌里。'},
  en: {projectName:'Z, from the Moon',review:'Moonlight stream · UI design preview',reset:'Reset',topic:'Sounds of the island',gold:'Gold',chat:'Live chat',giftAuthor:'Ash',giftVerb:'sent a',starBottle:'Star Bottle',reply:'Tide, I like that idea.',replySecond:'Next time, bring me the sea as you hear it.',toZ:'FOR Z',giftTitle:'A little something',smallGift:'Small gift',owned:'Owned',giftDescription:'A little thought from your day on the island.',sendGift:'Send to Z',giftNote:'Uses 1 owned gift · No gold charged',onAir:'LET’S TALK',callTitle:'Call draw',callQuestion:'What sound on the island made you stop and listen?',callDescription:'Sign up for a random draw. If selected, you choose whether to join the call.',joinCall:'Enter call draw',callNote:'Opt in · Your microphone stays off',nextSegment:'UP NEXT',advanceTitle:'Stay a little longer',advanceDescription:'Finish this segment and start the main show.',continue:'Keep watching',advanceNote:'Chat, gifts and call entries do not advance time',inputLabel:'Send a message',gifts:'Gifts',call:'Call draw',previewNote:'Try chat, gifts and call entry. This is a local UI preview; Z is a still illustration.',placeholder:'Say something. Let her get to know you…',you:'You',sent:'Sent',emptyGift:'Gift sent',emptyNote:'No gifts left · Gold balance: 74',giftSent:'Small gift sent · Inventory −1',queued:'Entered',queueDescription:'You’re in the draw. Keep chatting while you wait for the selection.',cancelCall:'Leave the draw',callJoined:'You’re in the call draw',callCanceled:'You’ve left the draw',advanceDone:'Main show',advanceToast:'Preview advanced to 20:40',close:'Close',hideChat:'Hide chat',showChat:'Show chat',immersive:'Immersive preview',leaveImmersive:'Exit immersive preview',artAlt:'Silver-haired Z sits by a moonlit sea window, listening to viewers on the other side of the screen.',liveLabel:'Z’s phone livestream',chatLabel:'Live chat messages',previewViewers:'Illustrative viewer count',name1:'Island Letter',text1:'The sea feels quieter tonight.',name2:'Tide',text2:'Could the dock bell become your opening beat?',name3:'Evening Breeze',text3:'Yes! Keep a little of the island in your song.'}
};
const $ = s => document.querySelector(s);
let locale = new URLSearchParams(location.search).get('lang') === 'en' ? 'en' : 'zh';
let inventory = 1, queued = false, advanced = false, panel = null, previousTrigger = null, toastTimer;
const customMessages = [];
const t = key => copy[locale][key];

function renderChat() {
  const list = $('#chat-list');
  list.replaceChildren();
  for (let i = 1; i <= 3; i++) appendMessage(list, t('name'+i), t('text'+i), i===2, false);
  customMessages.forEach(body => appendMessage(list, t('name2'), body, true, true));
  list.scrollTop = list.scrollHeight;
}
function appendMessage(list, name, body, self, sent) {
  const row = document.createElement('p'); row.className='chat-message'+(self?' self':'');
  const nickname = document.createElement('span'); nickname.className='chat-name'; nickname.textContent=name;
  if (self) {const tag=document.createElement('span'); tag.className='self-tag'; tag.textContent=t('you'); nickname.append(tag);}
  const text=document.createElement('span'); text.className='chat-body'; text.textContent=body;
  row.append(nickname,text);
  if(sent){const status=document.createElement('span');status.className='message-status';status.textContent=t('sent');row.append(status);}
  list.append(row);
}
function render() {
  document.documentElement.lang=locale==='zh'?'zh-CN':'en';
  document.querySelectorAll('[data-i18n]').forEach(el=>el.textContent=t(el.dataset.i18n));
  document.querySelectorAll('[data-locale]').forEach(el=>el.setAttribute('aria-pressed',String(el.dataset.locale===locale)));
  $('#message').placeholder=t('placeholder');
  $('.send-button').setAttribute('aria-label',t('inputLabel'));
  document.querySelectorAll('.close-panel').forEach(el=>el.setAttribute('aria-label',t('close')));
  $('#immersive').setAttribute('aria-label',t('immersive')); $('#immersive').title=t('immersive');
  $('#exit-immersive').setAttribute('aria-label',t('leaveImmersive'));
  $('.stream-art').alt=t('artAlt'); $('#stage').setAttribute('aria-label',t('liveLabel'));$('.chat').setAttribute('aria-label',t('chatLabel'));$('.viewers').title=t('previewViewers');
  $('#chat-toggle').setAttribute('aria-label',t($('.chat').classList.contains('collapsed')?'showChat':'hideChat'));
  $('#owned-count').textContent=inventory;
  $('#send-gift').disabled=!inventory;
  if(!inventory){$('#send-gift span').textContent=t('emptyGift');$('#gift-footnote').textContent=t('emptyNote');}
  $('#call-panel').classList.toggle('queued',queued);
  $('#call-button-label').textContent=t(queued?'queued':'call');
  $('#call-description').textContent=t(queued?'queueDescription':'callDescription');
  $('#join-call span').textContent=t(queued?'cancelCall':'joinCall');
  $('.queue-dot').hidden=!queued;
  $('#clock').textContent=advanced?'20:40':'20:10';
  $('#advance-button').disabled=advanced;
  if(advanced) $('#advance-button span').textContent=t('advanceDone');
  setGiftNotice(!inventory);
  renderChat();
}
function closePanel(restoreFocus=true) {
  if(panel) $('#'+panel+'-panel').hidden=true;
  document.querySelectorAll('.action-button, .continue-button').forEach(el=>el.setAttribute('aria-expanded','false'));
  $('#stage').classList.remove('panel-open');
  $('.chat').inert=false;
  panel=null;
  if(restoreFocus&&previousTrigger)previousTrigger.focus();
}
function openPanel(name,trigger) {
  if(panel===name){closePanel();return;}
  closePanel(false); panel=name;previousTrigger=trigger;
  $('#'+name+'-panel').hidden=false;trigger.setAttribute('aria-expanded','true');
  $('#stage').classList.add('panel-open');
  $('.chat').inert=true;
  $('#'+name+'-panel .close-panel').focus({preventScroll:true});
}
function toast(message) {
  clearTimeout(toastTimer);$('#toast').textContent=message;$('#toast').hidden=false;
  toastTimer=setTimeout(()=>$('#toast').hidden=true,3200);
}
function setGiftNotice(own) {
  $('#gift-notice .gift-author').textContent=t(own?'name2':'giftAuthor');
  $('#gift-notice strong').textContent=t(own?'smallGift':'starBottle');
  $('#gift-notice .gift-verb').textContent=t('giftVerb');
  $('#gift-notice use').setAttribute('href',own?'#i-gift':'#i-bottle');
  $('#gift-notice svg').classList.toggle('small-gift-icon',own);
}
document.querySelectorAll('[data-locale]').forEach(el=>el.addEventListener('click',()=>{locale=el.dataset.locale;$('#toast').hidden=true;render();}));
['gift','call','advance'].forEach(name=>$('#'+name+'-button').addEventListener('click',event=>openPanel(name,event.currentTarget)));
document.querySelectorAll('.close-panel').forEach(el=>el.addEventListener('click',()=>closePanel()));
$('#message').addEventListener('input',()=>$('.send-button').disabled=!$('#message').value.trim());
$('#message').addEventListener('keydown',event=>{if(event.key==='Enter'&&event.isComposing)event.preventDefault();});
$('#composer').addEventListener('submit',event=>{
  event.preventDefault();const value=$('#message').value.trim();if(!value)return;
  customMessages.push(value);if(customMessages.length>24)customMessages.shift();
  $('#message').value='';$('.send-button').disabled=true;renderChat();
});
$('#send-gift').addEventListener('click',()=>{if(!inventory)return;inventory--;render();closePanel();toast(t('giftSent'));});
$('#join-call').addEventListener('click',()=>{queued=!queued;render();toast(t(queued?'callJoined':'callCanceled'));});
$('#advance-confirm').addEventListener('click',()=>{if(advanced)return;advanced=true;render();closePanel();toast(t('advanceToast'));});
$('#chat-toggle').addEventListener('click',()=>{$('.chat').classList.toggle('collapsed');const expanded=!$('.chat').classList.contains('collapsed');$('#chat-toggle').setAttribute('aria-expanded',String(expanded));$('#chat-toggle').setAttribute('aria-label',t(expanded?'hideChat':'showChat'));});
$('#immersive').addEventListener('click',()=>{document.body.classList.add('immersive');$('#exit-immersive').focus();});
$('#exit-immersive').addEventListener('click',()=>{document.body.classList.remove('immersive');$('#immersive').focus();});
$('#reset').addEventListener('click',()=>{inventory=1;queued=false;advanced=false;customMessages.length=0;closePanel(false);$('#toast').hidden=true;$('#message').value='';$('.send-button').disabled=true;$('.chat').classList.remove('collapsed');$('#chat-toggle').setAttribute('aria-expanded','true');render();});
document.addEventListener('keydown',event=>{if(event.key==='Escape'){if(panel)closePanel();else{document.body.classList.remove('immersive');$('#immersive').focus();}}});
document.addEventListener('pointerdown',event=>{if(panel&&!event.target.closest('.overlay-panel, .action-button, .continue-button, .review-tools'))closePanel(false);});
render();
