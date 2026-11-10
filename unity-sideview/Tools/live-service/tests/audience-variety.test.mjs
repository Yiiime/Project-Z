import test from 'node:test';
import assert from 'node:assert/strict';
import {buildAgentRequest,normalizeAgentInput,parseAgentReply} from '../server/shared/worker/agent-director.js';
const fact={id:'song',type:'speech.completed',text:'今天有一首点歌。',speakerRole:'host',speakerId:'Z'};
const earlier={id:'quiet',type:'npc.message',text:'这个歌名让我想到睡前。',speakerRole:'audience',speakerId:'bean'};
const payload=(kind,context)=>normalizeAgentInput({kind,context,requestId:crypto.randomUUID(),eventId:'song',message:'现场测试'});
test('spectator owns their history; the output slot cannot borrow another biography',()=>{
 const lines=[{npcId:'wind',text:'我和同桌以前听同一副耳机。'}];
 const audience={eligibleNpcIds:['wind'],ownMessages:lines,recentMessages:[{npcId:'bean',text:earlier.text}]};
 const {input}=buildAgentRequest(payload('audience',{phase:'chat',publicEvents:[fact,earlier],audience}));
 assert.deepEqual(input.npcs.map(n=>n.npc_id),['wind']);assert.deepEqual(input.npcs[0].previous_lines,[lines[0].text]);
 assert(!input.shared_context.some(e=>e.text===input.npcs[0].personal_detail));
 assert.throws(()=>payload('audience',{phase:'chat',publicEvents:[fact,earlier],audience:{...audience,eligibleNpcIds:['tape']}}));
});
test('a named semantic echo cannot publish, even if the reviewer also says allow',()=>{
 const v=payload('review',{revision:2,publicEvents:[fact,earlier],candidate:{id:'draft',role:'audience',speakerId:'rain',text:'这歌名听着像能让人安静下来。',intent:'reaction',evidenceIds:['song']}});
 const reply={decision:'allow',reason:'验收',evidence_ids:['song']};
 assert.deepEqual(buildAgentRequest(v).input.recent_displayed.map(e=>e.id),['quiet']);
 assert.equal(parseAgentReply({...reply,echo_of:'quiet'},v).decision,'drop');
 assert.equal(parseAgentReply({...reply,echo_of:null},v).decision,'allow');
 for(const invalid of [undefined,'unknown','song'])assert.throws(()=>parseAgentReply({...reply,echo_of:invalid},v));
});
