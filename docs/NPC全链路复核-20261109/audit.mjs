import fs from 'node:fs';
import {fileURLToPath} from 'node:url';
import {once} from 'node:events';
import {createRequire} from 'node:module';
const root=fileURLToPath(new URL('../../',import.meta.url)).replace(/\/$/,''),basePath=root+'/unity-sideview/Tools/MemoryLab';
const require=createRequire(basePath+'/package.json'),Ajv=require('ajv/dist/2020.js');
const {createServer}=await import(basePath+'/server.mjs');
const {createLab}=await import(basePath+'/lib/lab.mjs');
const {LiveRoom}=await import(basePath+'/lib/live-room.mjs');
const schema=JSON.parse(fs.readFileSync(root+'/docs/Project-Z-NPC全链路.schema.json'));
const ajv=new Ajv({strict:false,allErrors:true});ajv.addSchema(schema);
const results=[],failures=[];let checked=0;
const validate=(def,value,label)=>{const v=ajv.compile({$ref:schema.$id+'#/$defs/'+def});checked++;if(!v(value))failures.push({label,def,errors:v.errors.slice(0,4)});};
function decide(c){let a=c.available_actions.find(a=>a.type==='wait');if(c.interaction.input==='offer')a=c.available_actions.find(a=>a.type==='offer_task')||a;
const info={emotion:'serious',changes:{},...(c.actor_id==='z'?{}:{support_willingness:c.actor_id==='zhou_bobo'?'unwilling':'considering'})};
if(c.character_state.cooperation_requests.length){const q=c.character_state.cooperation_requests[0];info.task_participation={task_id:q.task_id,choice:'declined',step_ids:[],contribution:'先做自己的事',reason:'当前有自己的安排',source_event_ids:[c.interaction.source_event_id]};}
const declined=(c.tasks||[]).find(t=>t.decline_event_id===c.interaction.source_event_id);if(declined)info.task_followup={task_id:declined.id,choice:'stop',reason:'尊重这次拒绝',source_event_ids:[c.interaction.source_event_id]};
return {dialogue:'我先自己想一想。',action:{option_id:a.id},info};}
const app=createServer({dbPath:':memory:',backgroundEnabled:false,providerFactory:()=>({publicConfig:()=>({configured:true}),mode:'isolated-audit',config:{}}),decide});
app.server.listen(0,'127.0.0.1');await once(app.server,'listening');const base='http://127.0.0.1:'+app.server.address().port;
const dto={sourceEventId:null,...Object.fromEntries('requestId checkpoint actorId recipientId conversationId topic scene locationId channel proposalId taskId stepId receiptId performerId responderId text weather roomId roomToken'.split(' ').map(k=>[k,null])),syncMode:'reference-v1',portable:false,day:0,period:0,turn:0,accepted:false,succeeded:false,candidates:[],observers:[],reachableLocations:[],walkableLocations:null,details:null,eventData:null};
const eventDto={...Object.fromEntries('id kind conversationId speakerId recipientId text proposalId location locationId title gist fulfilledId channel actionId taskId actorId responderId stepId performerId receiptId messageEventId weather'.split(' ').map(k=>[k,null])),seq:0,day:0,period:0,verified:false,succeeded:false,accepted:false,observers:[],discloseTo:[],participants:[],voluntaryConsents:[]};
async function post(path,p,check=true){if(!path.includes('/live/')||path.endsWith('/live/open')||path.endsWith('/live/close')){const {actors,...body}=p;p={...dto,...body};if(p.eventData)p.eventData={...eventDto,...p.eventData};}const match=schema['x-endpoints'].find(e=>e.path===path);if(check&&match)validate(match.request.split('/').at(-1),p,path+' request');const r=await fetch(base+path,{method:'POST',headers:{'Content-Type':'application/json','X-Z-Memory':'1'},body:JSON.stringify(p)});const value=await r.json();if(check&&r.ok&&match)validate(match.success.split('/').at(-1),value,path+' response');if(r.ok&&value.checkpoint){const cp=JSON.parse(value.checkpoint);validate(cp.version===3?'ReferenceCheckpoint':'PortableCheckpoint',cp,path+' checkpoint');}return {http:r.status,...value};}
const w={worldId:'audit_native',runId:'run1',day:1,period:0,syncMode:'reference-v1'};await post('/api/game/open',w);await post('/api/game/focus',{...w,actorId:'linxu'});
let seq=0;async function speech(text,actor='linxu',conversation='c1'){const id='input_'+(++seq);const r=await post('/api/game/sync',{...w,eventData:{id,seq,kind:'utterance',day:1,period:0,speakerId:'player',recipientId:actor,conversationId:conversation,text,locationId:'dock',channel:'private_player_chat',observers:['player',actor]}});if(!r.ok)throw Error(r.error);return {sourceEventId:id,conversationId:conversation};}
const source=await speech('offer');const req={...w,...source,actorId:'linxu',recipientId:'player',requestId:'request1',locationId:'dock'};let p=await post('/api/game/utterance',req);if(!p.ok)throw Error(p.error);
const retry=await post('/api/game/utterance',req);results.push({probe:'retry_same_utterance_request',first:p.http,retry:retry.http,error:retry.error});
const ev={id:'ack_'+(++seq),seq,day:1,period:0,kind:'utterance',speakerId:'linxu',recipientId:'player',conversationId:'c1',text:p.result.dialogue,proposalId:p.proposalId,locationId:'dock',channel:'private_player_chat',observers:['player','linxu']};await post('/api/game/sync',{...w,eventData:ev});
const offered=app.engine.tasks.list(w.worldId,'linxu').find(t=>t.status==='offered');if(!offered)throw Error('missing offered task');
const responseId='accept_'+(++seq);await post('/api/game/sync',{...w,eventData:{id:responseId,seq,day:1,period:0,kind:'task_response',taskId:offered.id,accepted:true,responderId:'player',conversationId:'c1'}});
p=await post('/api/game/utterance',{...req,requestId:'accepted-reply',sourceEventId:responseId});results.push({probe:'Unity_task_accept_reply',http:p.http,error:p.error,task:offered.id});
if(p.ok){seq++;await post('/api/game/sync',{...w,eventData:{...ev,id:'accepted-ack',seq,text:p.result.dialogue,proposalId:p.proposalId}});}
await post('/api/game/state',w);await post('/api/game/checkpoint',{...w,portable:true});
// A second receipt for the same published proposal but a fresh outbox event ID.
const duplicate=await post('/api/game/sync',{...w,eventData:{...ev,id:'duplicate-receipt',seq:seq+1}});results.push({probe:'new_event_id_same_proposal',http:duplicate.http,expectedAck:seq+1,returnedAck:duplicate.seq,error:duplicate.error});
await post('/api/game/focus',{...w,actorId:'z'});seq=app.engine.world(w.worldId).seq;const zsource=await speech('你好','z','zc');p=await post('/api/game/utterance',{...w,...zsource,actorId:'z',recipientId:'player',requestId:'zq',locationId:'z_home'});
const direct=await post('/api/game/commit',{...w,proposalId:p.proposalId,actorId:'z',observers:['z','player'],locationId:'z_home'});results.push({probe:'silent_endpoint_commits_nonempty_dialogue',http:direct.http,storedText:app.engine.memory.history(app.engine.world(w.worldId),'z','zc').at(-1)?.text});
// Acknowledgement is relabelled with another conversation despite matching proposal text.
await post('/api/game/focus',{...w,actorId:'z'});seq=app.engine.world(w.worldId).seq;
const lastSource=await speech('再聊一句','z','source-conv');const lastP=await post('/api/game/utterance',{...w,...lastSource,actorId:'z',recipientId:'player',requestId:'wrong-conv',locationId:'z_home'});
const renamed=await post('/api/game/sync',{...w,eventData:{...ev,id:'wrong-conversation-ack',seq:++seq,speakerId:'z',conversationId:'different-conv',text:lastP.result.dialogue,proposalId:lastP.proposalId}});
results.push({probe:'receipt_relabels_conversation',http:renamed.http,storedConversation:app.engine.event(w.worldId,'wrong-conversation-ack')?.conversationId});
await post('/api/game/focus',{...w,actorId:null});const opened=await post('/api/game/live/open',w),binding=opened.binding;
for(const actorId of ['z','linxu']){const prep=await post('/api/game/live/prepare',{...binding,actorId});validate('ModelContext',prep.context,'live context '+actorId);}
await post('/api/game/live/observe',{...binding,event:{id:'play-progress',actorId:'z',text:'已经播放的完整前缀',stage:'progress',publicationId:'publication1'}});
const short=await post('/api/game/live/observe',{...binding,event:{id:'play-completed',actorId:'z',text:'另一个结尾',stage:'completed',publicationId:'publication1'}});results.push({probe:'completed_text_diverges_from_played_prefix',http:short.http,error:short.error,record:app.engine.event(w.worldId,short.eventId)?.text});
await post('/api/game/live/observe',{...binding,event:{id:'play-progress-retry',actorId:'z',text:'已经播放的完整前缀',stage:'progress',publicationId:'publication1'}});
const interrupted=await post('/api/game/live/observe',{...binding,event:{id:'play-interrupted',actorId:'z',text:'已经播放的完整前缀',stage:'interrupted',publicationId:'publication1'}});results.push({probe:'progress_after_complete_reopens_publication',http:interrupted.http,newEventId:interrupted.eventId});
await post('/api/game/live/close',binding);
const lab=createLab(app.engine),created=await lab('new',{actorId:'linxu'});validate('LabResponse',created,'lab created');
const scope={worldId:created.worldId,runId:created.runId,actorId:'linxu'};const lturn=await lab('turn',{...scope,text:'offer'});let lp=lturn.results[0];await lab('commit',{...scope,proposalId:lp.proposalId,dialogue:lp.output.dialogue});
const lt=app.engine.tasks.list(scope.worldId,'linxu').find(t=>t.status==='offered');const lr=await lab('task/respond',{...scope,taskId:lt.id,accepted:false});validate('LabResponse',lr,'lab refusal proposed');lp=lr.results[0];const committed=await lab('commit',{...scope,proposalId:lp.proposalId,dialogue:lp.output.dialogue});validate('LabResponse',committed,'lab refusal committed');
results.push({probe:'lab_full_data_task_tables',exportedKeys:Object.keys(committed),hasRawTaskCheckpoint:!!committed.checkpoint||!!committed.taskCheckpoint,actualFollowups:app.engine.tasks.snapshot(scope.worldId).followups.length});
const saved=app.engine.checkpoint(scope.worldId);saved.world.actors.linxu.info={};let corrupted;try{app.engine.open(scope.worldId,'bad',saved);corrupted='accepted';}catch(e){corrupted=e.message;}results.push({probe:'invalid_actor_info_checkpoint',runtime:corrupted});results.at(-1).schemaAccepts=ajv.compile({$ref:schema.$id+'#/$defs/PortableCheckpoint'})(saved);
// Additional independent worlds: lost generation response, movement receipt, failed restore.
const ow={worldId:'audit_orphan',runId:'orphan_run',day:1,period:0,syncMode:'reference-v1'};
await post('/api/game/open',ow);await post('/api/game/focus',{...ow,actorId:'z'});
for(const [n,text] of [[1,'第一句'],[2,'新的第二句']]){
 await post('/api/game/sync',{...ow,eventData:{id:'orphan-input-'+n,seq:n,day:1,period:0,kind:'utterance',speakerId:'player',recipientId:'z',conversationId:'orphan-conv',text,locationId:'z_home',channel:'private_player_chat'}});
 const attempt=await post('/api/game/utterance',{...ow,requestId:'orphan-request-'+n,actorId:'z',recipientId:'player',conversationId:'orphan-conv',sourceEventId:'orphan-input-'+n,locationId:'z_home'});
 if(n===2)results.push({probe:'new_input_with_unacknowledged_prior_reply',http:attempt.http,error:attempt.error});
}
const owcp=app.engine.checkpoint(ow.worldId),pending=app.engine.records(app.engine.world(ow.worldId),'proposal').find(p=>p.status==='pending');
owcp.tasks.state=[{unexpected_column:'invalid'}];let restoreError;try{app.engine.open(ow.worldId,'bad_restore',owcp);}catch(e){restoreError=e.message;}
results.push({probe:'failed_restore_changes_old_proposal',error:restoreError,before:'pending',after:app.store.record(pending.id).status,runId:app.engine.world(ow.worldId).runId});
const mw={worldId:'audit_move',runId:'move_run',day:1,period:0,syncMode:'reference-v1'};
await post('/api/game/open',mw);await post('/api/game/focus',{...mw,actorId:'z'});
await post('/api/game/sync',{...mw,eventData:{id:'move-input',seq:1,day:1,period:0,kind:'utterance',speakerId:'player',recipientId:'z',conversationId:'move-conv',text:'去小小晴的花店',locationId:'z_home',channel:'private_player_chat'}});
app.engine.decide=c=>({...decide(c),dialogue:'好，一起去花店。',action:{option_id:c.available_actions.find(a=>a.type==='move'&&a.target==='flower_shop').id}});
const mp=await post('/api/game/utterance',{...mw,requestId:'move-request',actorId:'z',recipientId:'player',conversationId:'move-conv',sourceEventId:'move-input',locationId:'z_home',walkableLocations:['z_home','residential_middle','public_middle','flower_shop']});
if(!mp.ok)throw Error(mp.error);
await post('/api/game/sync',{...mw,eventData:{id:'move-ack',seq:2,day:1,period:0,kind:'utterance',speakerId:'z',recipientId:'player',conversationId:'move-conv',text:mp.result.dialogue,proposalId:mp.proposalId,locationId:'z_home',channel:'private_player_chat'}});
const actionId=app.engine.world(mw.worldId).actors.z.pending_action,atProposal=app.engine.world(mw.worldId).actors.z.scene;
const moveReceipt={id:'move-done',seq:3,day:1,period:0,kind:'action_result',actionId,succeeded:true,locationId:'flower_shop',text:'已实际到达花店。'};
const moveDone=await post('/api/game/sync',{...mw,eventData:moveReceipt});
const moveAgain=await post('/api/game/sync',{...mw,eventData:{...moveReceipt,id:'move-done-new-id',seq:4}});
const next=app.engine.context(app.engine.player(mw.worldId,'z','我们现在在哪？','unity_'+mw.runId));validate('ModelContext',next,'context after action receipt');
results.push({probe:'move_action_receipt_roundtrip',http:moveDone.http,atProposal,afterReceipt:app.engine.world(mw.worldId).actors.z.scene,actionStatus:app.store.record(actionId).status,nextTurnPreviousResult:next.previous_action_result,nextTurnEvidence:next.character_state.execution_evidence.map(x=>x.action_id)});
results.push({probe:'new_event_id_same_action_receipt',http:moveAgain.http,expectedAck:4,returnedAck:moveAgain.seq});
await new Promise(r=>app.server.close(r));app.store.close();
const report={checked,failures,results};fs.writeFileSync(new URL('./results.json',import.meta.url),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
