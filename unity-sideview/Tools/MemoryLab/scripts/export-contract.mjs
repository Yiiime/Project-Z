// Refresh source-derived definitions; retain hand-documented transport branches.
// --check is read-only and fails when the checked-in contract has drifted.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {Catalog} from '../lib/catalog.mjs';
import {Policy} from '../lib/policy.mjs';
import {storageContract} from '../lib/checkpoint.mjs';
const repo=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../../..');
const file=path.join(repo,'docs/Project-Z-NPC全链路.schema.json');
const before=fs.readFileSync(file,'utf8'),doc=JSON.parse(before),D=doc.$defs,c=new Catalog(),policy=new Policy(c),clone=structuredClone;
const s={type:'string'},i={type:'integer'},b={type:'boolean'},n={type:'number'},ns={type:['string','null']};
const ref=k=>({$ref:'#/$defs/'+k}),arr=x=>({type:'array',items:x}),obj=(p,required=Object.keys(p),extra=false)=>({type:'object',properties:p,required,additionalProperties:extra});
const ids=[...c.actors.keys()];
D.PeerRelationChanges=clone(c.contract.$defs.info.properties.peer_relation_changes);
for(const actor of ids){
 const suffix=actor==='z'?'Z':actor==='zhou_bobo'?'ZhouBobo':actor;
 const raw=policy.schema(actor),backend=clone(raw.$defs.info);D['BackendInfo'+suffix]=backend;
 const compact=clone(backend);delete compact.allOf;
 for(const k of ['affinity','trust','world_suspicion','persuasion','missing_state','memory_updates','knowledge_updates'])delete compact.properties[k];
 compact.required=['emotion','changes',...(actor==='z'?[]:['support_willingness'])];
 if(actor==='zhou_bobo')delete compact.properties.changes.properties.persuasion;
 D['ModelInfo'+suffix]=compact;
 const player=clone(compact);delete player.properties.communication;delete player.properties.social_decision;D['PlayerInfo'+suffix]=player;
 const live=clone(compact);for(const k of ['communication','social_decision','agenda_update','task_participation','task_followup'])delete live.properties[k];D['LiveInfo'+suffix]=live;
 D['BackendTurn'+suffix].allOf=clone(raw.$defs.output.allOf);
}
// The live backend loads this same source, including its nested action variants.
Object.assign(D,JSON.parse(fs.readFileSync(new URL('../lib/live-contract.schema.json',import.meta.url))).$defs);
D.HostBinding=clone(D.LiveBinding);D.HostBinding.properties.memoryUrl={type:'string',description:'Unity adds the local backend origin; not a field returned by live/open.'};D.HostBinding.required.push('memoryUrl');
D.LivePrepareResponse.properties.turnSchema={type:'object',description:'Backend-generated complete live turn contract; the proposal endpoint validates the same shape.'};D.LivePrepareResponse.required=[...new Set([...D.LivePrepareResponse.required,'turnSchema'])];
D.PreparedLiveContext=clone(D.LivePrepareResponse);D.PreparedLiveContext.properties.publicEventIds=arr(s);D.PreparedLiveContext.required.push('publicEventIds');
D.UnityRequest.properties.sourceEventId=ns;
const utterance=D.UtteranceRequest.allOf.find(x=>x.properties);utterance.properties.sourceEventId=ns;
D.UtteranceRequest.description='Bind the synced sourceEventId. Legacy omission can resolve only within this exact conversation, never from another conversation.';
D.UnityEvent.allOf.push({if:{properties:{kind:{const:'utterance'}},required:['kind']},then:{properties:{speakerId:{enum:[...ids,'player']}}}});
// Make repeated exports stable.
D.UnityEvent.allOf=[...new Map(D.UnityEvent.allOf.map(x=>[JSON.stringify(x),x])).values()];
D.RelationHistory=obj({turn_id:s,reason:s,source_event_ids:arr(s),source_kind:s,source_actor_id:ref('SpeakerId'),before:obj({affinity:ref('Metric'),trust:ref('Metric')}),after:obj({affinity:ref('Metric'),trust:ref('Metric')})});
D.PeerRelation.properties.history={...arr(ref('RelationHistory')),maxItems:12};
const recalled={actorId:ref('ActorId'),content:s,detailLevel:{enum:['detail','gist']},sourceLabel:s,speakerId:ns,commitmentStatus:ns,certainty:s,availability:n,clarity:n,cueScore:n,cued:b,reason:s,state:ref('MemoryAvailability'),rank:n};
Object.assign(D.RecalledMemory.properties,recalled);
D.RecentExperience=obj({id:s,speakerId:ref('SpeakerId'),text:s,conversation_id:s,actual_audience:arr(s),disclose_to:arr(s),source_event_ids:arr(s),kind:{enum:['claim','observation']}},['id','speakerId','text','actual_audience','disclose_to','source_event_ids','kind'],true);
D.CooperationRequest=obj({task_id:s,title:s,request:s,source_event_id:s,effect_class:s,allowed_step_ids:arr(s),step_labels:{type:'object',additionalProperties:s}},['task_id','title','request','source_event_id','allowed_step_ids','step_labels'],true);
D.CooperationState=obj({task_id:s,title:s,status:s,source_event_id:s,effect_class:s,allowed_step_ids:arr(s),decision:{anyOf:[ref('TaskParticipation'),{type:'null'}]}},['task_id','title','status','source_event_id','allowed_step_ids','decision'],true);
const cp=D.ModelContext.properties.character_state.properties;
cp.recent_conversation=arr(ref('RecentExperience'));cp.recent_experiences=arr(ref('RecentExperience'));cp.cooperation_requests=arr(ref('CooperationRequest'));cp.cooperation_state=arr(ref('CooperationState'));
D.TaskProgress=obj({succeeded:b,performed_by:ref('SpeakerId'),details:ref('TaskReceiptDetails'),receipt_id:s});
D.TaskState.allOf.find(x=>x.properties).properties.progress={type:'object',additionalProperties:ref('TaskProgress')};
D.TaskReceiptRecord=obj({session:s,receipt:s,payload:s});D.TaskFollowupRecord=obj({session:s,task:s,decline_event:s,decision:ns});
D.TaskCheckpoint.properties.receipts=arr(ref('TaskReceiptRecord'));D.TaskCheckpoint.properties.followups=arr(ref('TaskFollowupRecord'));
D.TaskDefinition.properties.reward=obj({kind:s,summary:s});
D.MapPlace=obj({id:ref('LocationId'),name:s,kind:s,description:s,aliases:arr(s)},['id','name'],true);
const map=D.ModelContext.properties.environment.properties.map_context.properties;
map.current_place=ref('MapPlace');map.exits=arr(ref('MapPlace'));map.boundary=s;
Object.assign(map.asked_routes.items.properties,{destination:ref('LocationId'),destination_name:s,via:arr(s)});
D.ModelContext.properties.environment.properties.available_actions=arr(ref('AvailableAction'));
D.ModelContext.properties.environment.properties.allowed_communication_channels=arr(s);
D.ModelContext.properties.environment.properties.sceneId.description='Legacy host alias; scene_id is the normalized context field. Neither is location_id.';
D.JobRecord=obj({id:s,experimentId:s,epoch:i,hour:n,world:s,actor:ref('ActorId'),event:s,mode:s,conversation:s,channel:s,root:s,hop:i,due:n,status:s,environment:{type:'object'},context:ref('ModelContext'),infoSchema:{type:'object'},validation_error:s},['id','experimentId','epoch','hour','actor','event','mode','status'],true);
D.ConversationRecord=obj({id:s,experimentId:s,epoch:i,hour:n,status:s,actors:arr(ref('ActorId')),lines:i},['id','experimentId','epoch','status','actors'],true);
D.DiagnosticEventRecord=obj({id:s,experimentId:s,epoch:i,hour:n,type:s,text:s,details:{type:'object'},at:s});
D.PortableCheckpoint.properties.records.properties.event=arr({oneOf:[ref('WorldEventRecord'),ref('DiagnosticEventRecord')]});
D.PortableCheckpoint.properties.records.properties.job=arr(ref('JobRecord'));D.PortableCheckpoint.properties.records.properties.conversation=arr(ref('ConversationRecord'));
D.WorldState.properties.lab=b;
Object.assign(D,storageContract(c,policy).$defs);
D.LabScope=obj({worldId:s,runId:s,actorId:ref('ActorId')},['worldId','runId'],true);
D.LabResponse=obj({ok:{const:true},worldId:s,runId:s,contentVersion:s,state:ref('StateSnapshot'),records:D.PortableCheckpoint.properties.records,actor:obj({id:ref('ActorId'),profile:s,memory:{type:'object'}}),initialRelationships:{type:'object'},observerView:{const:true},exportVersion:{const:1},exportNotice:s,backend:obj({world:ref('WorldState'),taskCheckpoint:ref('TaskCheckpoint')}),results:arr(ref('JobResult'))},['ok','worldId','runId','state','records','actor','observerView','exportVersion','backend'],true);
D.LabResponse.properties.records=clone(D.PortableCheckpoint.properties.records);
D.LabResponse.properties.records.properties.trace=arr(ref('TraceRecord'));
D.LabResponse.properties.records.description='Includes complete current-epoch diagnostic records. backend.taskCheckpoint contains raw task tables; credentials are removed.';
doc.$id='urn:project-z:npc-contract:2026-11-09:review-4';
doc['x-versions'].document='review-4（请求恢复、回执、播放终态、原子恢复与完整诊断）';
doc['x-endpoints'].find(e=>e.path==='/api/game/utterance').note='绑定已sync的sourceEventId生成提案；同requestId同内容重放原提案，同ID不同内容拒绝。较新玩家输入取消同会话未确认的旧回复。reference-v1仅回台词。';
doc['x-current-limitations'][0]='角色、直播与存储定义从运行契约导出；存档入口使用同源结构校验及引用预检。部分HTTP封装仍为手工描述，并非所有入口统一使用AJV；用export-contract --check检查漂移。仅本地修改。';
doc['x-repair-status']={C01:'HostBinding declared separately',C02:'deferred: no changes to time rules or constraints',C03:'accepted design: own directed relationships may change in player/A2A/live/world event turns with visible sources',C04:'self-target excluded by source-generated actor output schema',C05:'live stage validated at observe',C06:'live complete turnSchema validated at propose',C07:'utterance speaker restricted to player or roster',R01:'sourceEventId bound; exact-conversation fallback only',R02:'physical executors deferred',R03:'recall/cooperation/progress/checkpoint/adaptor shapes supplied',R06:'runtimeRevision exposed in health'};
doc['x-relationship-policy']={direction:'observer -> target; never mutate the reverse relation',initialization:'story baselines; missing edges neutral 50/50 with neutral_unspecified provenance',updates:'up to 7 unique related targets per turn; non-zero deltas; visible current event; each value remains 0..100; duplicate source scoring rejected',memory:'last 12 change reasons with source kind and before/after in peer_relations[target].history; raw proposals/events remain in backend',help:'relationships, memories, motives and risk inform NPC decisions; no affinity threshold forces assistance'};
doc['x-lab']={page:'/lab',base:'/api/lab/',response:'#/$defs/LabResponse',operations:['new','inspect','focus','turn','place','encounter','continue','event','task/respond','task/invite','commit','cancel'],note:'Diagnostic observer view of isolated lab_ worlds, not a player UI and not an executor. Mutations require the local test token or X-Z-Memory header.'};
doc['x-audit-repairs-20261109']={R01:'durable job request identity, replay and newer-input supersession',R02:'duplicate receipt acknowledgements advance seq without gameplay effects',R03:'silent-only commit and conversation/channel binding',R04:'publication prefix and terminal lifecycle',R05:'shared storage schema, semantic preflight and atomic restore',R06:'versioned diagnostic export with raw task tables and sanitized world',R07:'source-derived storage/job/trace definitions'};
const sha=v=>crypto.createHash('sha256').update(v).digest('hex');
doc['x-content-snapshot'].registry=c.registry;doc['x-content-snapshot'].personas=ids.map(actor=>({actor_id:actor,profile_path:c.actor(actor).profile,markdown:c.profile(actor),sha256:sha(c.profile(actor))}));doc['x-content-snapshot'].shared_documents=c.registry.common_documents.map(p=>({path:p,markdown:fs.readFileSync(c.path(p),'utf8')}));
for(const entry of doc['x-source-files']||[])if(fs.existsSync(path.join(repo,entry.path)))entry.sha256=sha(fs.readFileSync(path.join(repo,entry.path)));
doc['x-validation']={note:'Run scripts/export-contract.mjs --check plus contract-drift.test.mjs and runtime regression. Schema acceptance is not physical execution or dialogue semantic proof.'};
const after=JSON.stringify(doc,null,2)+'\n';
if(process.argv.includes('--check')){if(after!==before){console.error('Contract drift: run node scripts/export-contract.mjs');process.exitCode=1;}else console.log('Source-derived contracts are synchronized.');}
else {fs.writeFileSync(file,after);console.log('Updated '+file+' ('+Object.keys(D).length+' definitions)');}
