// Local deterministic fixture for Seabreeze.Editor.LiveSessionVerification.
// Never loads provider secrets or calls a remote model.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createLiveServer} from '../server/http.mjs';
import {AGENT_BUNDLE} from '../server/shared/worker/agent-bundle-data.js';
import {Providers,ArtifactStore} from '../server/providers.mjs';
const directory=fs.mkdtempSync(path.join(os.tmpdir(),'z-live-ui-'));
const configProvider=new Providers({DEEPSEEK_API_KEY:'fixture-only'});
let audienceSequence=0;
const advanced=new Set(),extensionTurns=new Set();
const providers={env:{},config:identity=>configProvider.config(identity),generate:async(_identity,p)=>{
 if(process.env.Z_VERIFY_TIMED==='1'&&p.kind==='session'&&p.context.mode==='inspect'&&!advanced.has(_identity)){advanced.add(_identity);setTimeout(()=>{const s=sessions.get(_identity);if(s){s.sessionControl.now=()=>Date.now()+480000;s.sessionControl.tick();}},2500);}
 if(process.env.Z_VERIFY_DAY==='1'&&p.kind==='session'&&p.context.mode==='inspect'&&p.context.message?.text.startsWith('验收:挽留'))return {intent:'extend',scope:'把晚霞的颜色说完',speech:''};
 if(p.kind==='session')return p.context.mode==='farewell'?{intent:'farewell',scope:'',speech:'刚才的晚霞就聊到这里。谢谢大家陪我，晚安。'}:{intent:'keep',scope:'',speech:''};
 if(p.kind==='review')return {decision:'allow',candidateId:p.context.candidate.id,revision:p.context.revision,promptVersion:AGENT_BUNDLE.review.version};
 if(p.kind==='audience'){
  if(process.env.Z_VERIFY_AUDIENCE!=='1')return {status:'skip',items:[]};
  const npc=p.context.audience.eligibleNpcIds[0];
  return {status:'ready',items:[{npc_id:npc,text:'今晚的小屋打卡 '+(++audienceSequence),intent:'atmosphere',evidence_refs:[p.eventId]}]};
 }
 if(process.env.Z_VERIFY_DAY==='1'&&p.kind==='host'){
  const control=p.context.host.sessionControl,inbox=p.context.host.inbox;
  if(control.extensionActive){
   if(extensionTurns.has(_identity))return {action:'speak',speech:'晚霞的话题说完了，我们就休息吧。',mood:'warm',handledIds:inbox.filter(m=>m.mine).map(m=>m.id),wakeAfterMs:2000,sessionDecision:{intent:'finish_extension',sourceMessageId:null,scope:'',reason:'追加范围完成'}};
   extensionTurns.add(_identity);return {action:'wait',speech:'',mood:'warm',handledIds:[],wakeAfterMs:2000,continuation:'await_event',agendaUpdates:[]};
  }
  if(inbox.some(m=>m.mine&&m.text.startsWith('验收:自主')))return {action:'speak',speech:'刚才的晚霞聊得很开心，今晚我们准备收播啦。',mood:'warm',handledIds:inbox.filter(m=>m.mine).map(m=>m.id),wakeAfterMs:2000,sessionDecision:{intent:'close',sourceMessageId:null,scope:'',reason:'交流已有落点'}};
 }

 return {action:'wait',speech:'',mood:'warm',handledIds:[],wakeAfterMs:120000,continuation:'await_event',agendaUpdates:[]};
}};
const {server,sessions}=await createLiveServer({root:path.join(directory,'sessions'),artifacts:new ArtifactStore(path.join(directory,'artifacts')),songRoot:fileURLToPath(new URL('../server/assets/songs',import.meta.url)),providers,token:'isolated-ui-verification'});
server.listen(4188,'127.0.0.1',()=>console.log('UI verification fixture on 127.0.0.1:4188; no external providers.'));
for(const signal of ['SIGINT','SIGTERM'])process.on(signal,()=>{server.closeAllConnections();server.close(()=>{fs.rmSync(directory,{recursive:true,force:true});process.exit(0);});});
