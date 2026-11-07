import {test} from 'node:test';
import assert from 'node:assert/strict';
import {Providers} from '../server/providers.mjs';
const original='799f8935-2f68-4ab6-b6e4-4e28bb455423';
const payload=()=>({requestId:crypto.randomUUID(),kind:'host',eventId:'test',context:{host:{inbox:[{id:original,name:'小岛来客',mine:true,text:'唱青藏高原'}],session:{sessionId:'test',evidence:[{id:original,type:'player.message',speakerRole:'player',speakerId:'player',text:'唱青藏高原'}],unfinishedItems:[],recentInitiatives:[],crossSessionMemory:{available:false,entries:[]}}},mediaRuntime:{audioEnabled:true,ttsAvailable:true}}});
const reply=ref=>({action:'tool',speech:'我想一想这首歌。',mood:'warm',handledIds:[ref],wakeAfterMs:2000,memory:'',tool:{name:'prepare_song',args:{messageId:ref}},agendaUpdates:[{id:null,goal:'唱歌',nextStep:'准备歌曲',status:'open',evidenceIds:[ref]}]});
function provider(make){return new Providers({DEEPSEEK_API_KEY:'test-key'},{singingContext:()=>({enabled:true,tasks:[]}),fetch:async(_url,options)=>{const envelope=JSON.parse(options.body),input=JSON.parse(envelope.messages[1].content),m=input.context.host.inbox[0];assert.equal(m.id,'inbox_1');assert.equal(input.context.host.session.evidence[0].id,m.id);return Response.json({choices:[{finish_reason:'stop',message:{content:JSON.stringify(make(m.id))}}]});}});}
test('host short references restore exact IDs in handled messages, tools and agenda evidence',async()=>{
 const r=await provider(reply).generate('test',payload());assert.deepEqual(r.handledIds,[original]);assert.equal(r.tool.args.messageId,original);assert.deepEqual(r.agendaUpdates[0].evidenceIds,[original]);
});
test('unknown short references still fail validation; no fuzzy ID repair',async()=>{
 await assert.rejects(()=>provider(()=>({...reply('inbox_99'),agendaUpdates:[]})).generate('test',payload()),e=>e.code==='agent_invalid_reply'&&e.validationReason==='unknown_handled_id');
});
