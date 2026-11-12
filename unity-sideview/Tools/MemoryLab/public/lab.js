const $=id=>document.getElementById(id), esc=x=>String(x??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
let config,snap,scope={},selected='z',tab='关系',busy=false,conversation='',draft=null;
const name=id=>({player:'玩家',engine:'引擎'}[id]||config?.actors.find(a=>a.id===id)?.name || id || '引擎');
const readable=value=>({relaxed:'放松',curious:'好奇',serious:'认真',happy:'开心',excited:'兴奋',sad:'难过',angry:'生气',anxious:'焦虑',afraid:'害怕',confused:'困惑',unassessed:'尚未判断',considering:'考虑中',willing:'愿意',unwilling:'不愿意',available:'尚未提出',offered:'等待玩家答复',active:'进行中',declined:'已拒绝',completed:'已完成',claim:'原话（内容待核实）',fact:'已确认事实',encounter:'实际同场',observation:'观察信息'}[value]||value);
const modes=['关系','记忆','本轮输入','模型输出','事件','任务','完整数据','人设'];
const labels={affinity:'对玩家好感',trust:'对玩家信任',world_suspicion:'世界怀疑度',persuasion:'援助倾向'};
async function rpc(operation,body={}){
 const response=await fetch('/api/lab/'+operation,{method:'POST',headers:{'Content-Type':'application/json','X-Test-Token':window.LAB_TOKEN},body:JSON.stringify({...scope,actorId:selected,...body})});
 const value=await response.json();if(!response.ok||value.ok===false)throw Error(value.error||'请求失败');return value;
}
function adopt(value){snap=value;scope={worldId:value.worldId,runId:value.runId};conversation=snap.records.job.find(j=>j.mode==='npc_message'&&['pending','proposed'].includes(j.status))?.conversation||conversation;localStorage.setItem('moon-island-lab',JSON.stringify({...scope,actorId:selected,conversation}));render();}
async function work(fn){if(busy)return;busy=true;$('error').hidden=true;$('busy').textContent='处理中…';document.querySelectorAll('button,select').forEach(b=>b.disabled=true);try{await fn()}catch(e){$('error').textContent=e.message;$('error').hidden=false;}finally{busy=false;$('busy').textContent='';document.querySelectorAll('button,select').forEach(b=>b.disabled=false);}}
async function receive(value){
 const result=value.results?.[0];
 if(result?.status==='proposed'){
  selected=result.actor;$('actor').value=selected;draft=result;adopt(value);
  // Render before acknowledging. Failed requests leave the proposal visibly pending.
  await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));
  const next=await rpc('commit',{proposalId:result.proposalId,dialogue:result.output.dialogue});
  const proposal=next.records.proposal.find(p=>p.id===result.proposalId);if(proposal?.context.interaction.mode!=='player')conversation=proposal?.conversation || conversation;
  draft=null;adopt(next);
 }else{adopt(value);if(result?.status==='failed')throw Error(result.validation_error||'本轮生成失败，未提交状态');}
}
function metric(label,value){return `<div class="metric"><span>${esc(label)}</span><span class="bar"><i style="width:${Math.max(0,Math.min(100,Number(value)||0))}%"></i></span><b>${esc(value??'—')}</b></div>`;}
function render(){if(!snap)return;const actor=snap.state.actors.find(a=>a.id===selected);if(!actor)return;
 if($('target').value===selected)$('target').value=config.actors.find(a=>a.id!==selected)?.id||'';
 $('identity').textContent=`${actor.identity?.occupation||''} · ${config.places[actor.scene]||actor.scene}`;
 $('metrics').innerHTML=Object.entries(labels).filter(([k])=>k in actor.info).map(([k,l])=>metric(l,actor.info[k])).join('')+`<p class="muted">情绪：${esc(readable(actor.info.emotion))}<br>援助意愿：${esc(readable(actor.info.support_willingness)||'由Z自主选择')}</p>`;
 $('save').textContent=`${scope.worldId} · 内容 ${snap.contentVersion}`;$('chat-title').textContent=`与${name(selected)}对话`;
 const history=snap.records.event.filter(e=>e.visible?.includes(selected)).slice(-50);
 $('transcript').innerHTML=history.map(e=>`<div class="line ${e.speaker==='player'?'player':e.speaker==='engine'?'event':''}"><b>${esc(name(e.speaker))}</b> <small>${esc(readable(e.kind))} · ${esc(e.id)}</small><p>${esc(e.text)}</p></div>`).join('')+(draft?.output.dialogue?`<div class="line pending"><b>${esc(name(draft.actor))} · 等待呈现提交</b><p>${esc(draft.output.dialogue)}</p></div>`:'');
 $('transcript').scrollTop=$('transcript').scrollHeight;
 const pending=snap.records.proposal.filter(p=>p.actor===selected&&p.status==='pending');
 $('pending').innerHTML=pending.map(p=>`<p>待提交提案：${esc(p.id)} <button data-resume="${esc(p.id)}">显示并提交这一轮</button></p>`).join('');
 $('a2a').textContent=snap.state.player_focus_actor?`玩家正在与${name(snap.state.player_focus_actor)}交谈，该角色不会参加NPC交流。`:conversation?'可以继续已开始的NPC交流；角色可以选择结束。':'当前没有玩家对话占用。';
 $('tasks').innerHTML=snap.state.tasks.filter(t=>t.status!=='locked'&&(t.enabled||t.status!=='available')&&(t.owner===selected||t.status==='active')).map(t=>`<div class="task"><b>${esc(t.title)}</b> <span class="tag">${esc(readable(t.status))}</span><p>${esc(name(t.owner))}：${esc(t.description)}</p>${t.status==='offered'?`<button data-task="${esc(t.id)}" data-choice="true">接受</button><button data-task="${esc(t.id)}" data-choice="false">拒绝</button>`:''}${t.status==='active'?`<button data-invite="${esc(t.id)}">邀请${esc(name(selected))}协作</button>`:''}<p class="muted">${esc(t.collaborators?.length?JSON.stringify(t.collaborators):'暂无协作成员')}</p></div>`).join('')||'<p class="hint">还没有当前可交互的任务，可以先问问NPC有什么需要帮忙的。</p>';
 renderData();}
function renderData(){if(!snap)return;const a=snap.state.actors.find(a=>a.id===selected),p=snap.records.proposal.filter(p=>p.actor===selected).at(-1);$('tabs').innerHTML=modes.map(t=>`<button data-tab="${t}" class="${t===tab?'active':''}">${t}</button>`).join('');
 if(tab==='关系'){$('data-view').innerHTML=`<p class="hint">${esc(name(selected))} → 其他人。方向相反的关系独立变化；没有故事基线的关系以50/50作为普通交往起点。</p>`+Object.entries(a.peer_relations).map(([id,r])=>`<div class="relation"><strong>${esc(name(selected))} → ${esc(name(id))}</strong>${metric('好感',r.affinity)}${metric('信任',r.trust)}<p class="muted">基线来源：${esc(r.source||'本局初印象')}</p>${(r.history||[]).slice(-3).reverse().map(h=>`<p>${esc(h.reason)} <span class="tag">${esc('触发：'+readable(h.source_kind))}</span><br><small>${esc(h.source_event_ids.join('、'))}</small></p>`).join('')}<details><summary>完整关系数据</summary><pre>${esc(JSON.stringify(r,null,2))}</pre></details></div>`).join('');return;}
 const job=snap.records.job.filter(j=>j.actor===selected).at(-1),failed=job?.status==='failed';
 const value=tab==='记忆'?snap.actor.memory:tab==='本轮输入'?job?.context||p?.context:tab==='模型输出'?failed?{status:'failed',error:job.error,rejected_output:job.rejected_output,notice:'校验未通过，没有呈现NPC台词，也没有提交数值变化'}:{proposalId:p?.id,status:p?.status,...p?.output}:tab==='事件'?snap.records.event:tab==='任务'?snap.state.tasks:tab==='人设'?snap.actor.profile:snap;
 $('data-view').innerHTML=`<pre>${esc(typeof value==='string'?value:JSON.stringify(value??'尚无本轮数据',null,2))}</pre>`;
}
$('tabs').onclick=e=>{const t=e.target.dataset.tab;if(t){tab=t;renderData();}};
$('actor').onchange=()=>work(async()=>{selected=$('actor').value;draft=null;await rpc('focus',{focusActorId:selected});adopt(await rpc('inspect'));});
$('new').onclick=()=>work(async()=>{conversation='';draft=null;adopt(await rpc('new'));});
$('refresh').onclick=()=>work(async()=>adopt(await rpc('inspect')));
$('release').onclick=()=>work(async()=>adopt(await rpc('focus',{focusActorId:null})));
$('chat').onsubmit=e=>{e.preventDefault();const text=$('message').value.trim();if(!text)return;work(async()=>{const v=await rpc('turn',{text});$('message').value='';await receive(v);});};
$('message').onkeydown=e=>{if(e.key==='Enter'&&!e.shiftKey&&!e.isComposing){e.preventDefault();$('chat').requestSubmit();}};
$('arrange').onclick=()=>work(async()=>{if(selected===$('target').value)throw Error('请选择另一位NPC');await rpc('focus',{focusActorId:null});await rpc('place',{locationId:$('place').value});await rpc('place',{actorId:$('target').value,locationId:$('place').value});adopt(await rpc('inspect'));});
$('encounter').onclick=()=>work(async()=>receive(await rpc('encounter',{targetId:$('target').value})));
$('continue').onclick=()=>work(async()=>receive(await rpc('continue',{conversationId:conversation})));
$('event-send').onclick=()=>work(async()=>{const visible=[...document.querySelectorAll('#recipients input:checked')].map(x=>x.value);adopt(await rpc('event',{text:$('event-text').value,visible,verified:$('verified').checked}));$('event-text').value='';});
$('tasks').onclick=e=>{if(e.target.dataset.task)work(async()=>receive(await rpc('task/respond',{taskId:e.target.dataset.task,accepted:e.target.dataset.choice==='true'})));if(e.target.dataset.invite)work(async()=>receive(await rpc('task/invite',{taskId:e.target.dataset.invite})));};
$('pending').onclick=e=>{if(e.target.dataset.resume)work(async()=>{const p=snap.records.proposal.find(p=>p.id===e.target.dataset.resume);await receive({...snap,results:[{actor:p.actor,status:'proposed',proposalId:p.id,output:p.output}]});});};
$('download').onclick=()=>{const url=URL.createObjectURL(new Blob([JSON.stringify(snap,null,2)],{type:'application/json'})),a=document.createElement('a');a.href=url;a.download=scope.worldId+'.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);};
async function start(){config=await (await fetch('/api/config')).json();$('connection').textContent=config.configured?`${config.providerName||'模型'} · ${config.model}`:'未配置模型，请打开模型设置';for(const id of ['actor','target'])$(id).innerHTML=config.actors.map(a=>`<option value="${a.id}">${esc(a.name)}</option>`).join('');$('target').value='linxu';$('place').innerHTML=Object.entries(config.places).map(([id,n])=>`<option value="${id}">${esc(n)}</option>`).join('');$('place').value='dock';$('recipients').innerHTML=config.actors.map(a=>`<label><input type="checkbox" value="${a.id}">${esc(a.name)}</label>`).join('');
 let saved;try{saved=JSON.parse(localStorage.getItem('moon-island-lab'));}catch{}if(saved){scope={worldId:saved.worldId,runId:saved.runId};selected=config.actors.some(a=>a.id===saved.actorId)?saved.actorId:'z';conversation=saved.conversation||'';$('actor').value=selected;try{adopt(await rpc('inspect'));return;}catch{}}
 adopt(await rpc('new'));}
work(start);
