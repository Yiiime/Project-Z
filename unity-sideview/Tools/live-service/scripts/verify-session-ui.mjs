// Local deterministic fixture for Seabreeze.Editor.LiveSessionVerification.
// Never loads provider secrets or calls a remote model.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createLiveServer} from '../server/http.mjs';
import {Providers,ArtifactStore} from '../server/providers.mjs';
const directory=fs.mkdtempSync(path.join(os.tmpdir(),'z-live-ui-'));
const configProvider=new Providers({DEEPSEEK_API_KEY:'fixture-only'});
const advanced=new Set();
const providers={env:{},config:identity=>configProvider.config(identity),generate:async(_identity,p)=>{
 if(process.env.Z_VERIFY_TIMED==='1'&&p.kind==='session'&&p.context.mode==='inspect'&&!advanced.has(_identity)){advanced.add(_identity);setTimeout(()=>{const s=sessions.get(_identity);if(s){s.sessionControl.now=()=>Date.now()+480000;s.sessionControl.tick();}},2500);}
 if(p.kind==='session')return p.context.mode==='farewell'?{intent:'farewell',scope:'',speech:'刚才的晚霞就聊到这里。谢谢大家陪我，晚安。'}:{intent:'keep',scope:'',speech:''};
 if(p.kind==='review')return {decision:'allow',candidateId:p.context.candidate.id,revision:p.context.revision,promptVersion:'fixture-only'};
 if(p.kind==='audience')return {status:'skip',items:[]};
 return {action:'wait',speech:'',mood:'warm',handledIds:[],wakeAfterMs:120000,continuation:'await_event',agendaUpdates:[]};
}};
const {server,sessions}=await createLiveServer({root:path.join(directory,'sessions'),artifacts:new ArtifactStore(path.join(directory,'artifacts')),songRoot:fileURLToPath(new URL('../server/assets/songs',import.meta.url)),providers,token:'isolated-ui-verification'});
server.listen(4188,'127.0.0.1',()=>console.log('UI verification fixture on 127.0.0.1:4188; no external providers.'));
for(const signal of ['SIGINT','SIGTERM'])process.on(signal,()=>{server.closeAllConnections();server.close(()=>{fs.rmSync(directory,{recursive:true,force:true});process.exit(0);});});
