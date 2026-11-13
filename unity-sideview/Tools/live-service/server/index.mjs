import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomBytes} from 'node:crypto';
import {Providers,ArtifactStore,loadEnvironment} from './providers.mjs';
import {NeteaseSource} from './song-library/netease.mjs';
import {CatalogSource} from './song-library/sources.mjs';
import {LiveSongLibrary} from './song-library/index.mjs';
import {SingingService} from './singing/service.mjs';
import {NativeVoiceService} from './voiceprint/native-service.mjs';
import {createLiveServer} from './http.mjs';
import {loadTeamClient} from '../../team-model/client.mjs';
const root=fileURLToPath(new URL('../',import.meta.url)),runtime=process.env.Z_LIVE_RUNTIME_DIR||path.join(root,'.runtime',process.env.Z_LIVE_OFFICE==='1'?'office':'');fs.mkdirSync(runtime,{recursive:true,mode:0o700});
const lock=path.join(runtime,'server.lock');try{const fd=fs.openSync(lock,'wx',0o600);fs.writeFileSync(fd,String(process.pid));fs.closeSync(fd);}catch{let live=true;try{process.kill(Number(fs.readFileSync(lock,'utf8')),0);}catch{live=false;}if(live)throw Error('Another server owns this store.');fs.unlinkSync(lock);fs.writeFileSync(lock,String(process.pid),{flag:'wx',mode:0o600});}
const cleanup=()=>{try{fs.unlinkSync(lock);}catch{}};process.on('exit',cleanup);
const tokenFile=path.join(runtime,'local-token');if(!fs.existsSync(tokenFile))fs.writeFileSync(tokenFile,randomBytes(32).toString('hex'),{mode:0o600});const token=fs.readFileSync(tokenFile,'utf8').trim();
const office=process.env.Z_LIVE_OFFICE==='1'?JSON.parse(fs.readFileSync(path.join(runtime,'host.json'),'utf8')):null;
const port=Number(office?.port||process.env.Z_UNITY_PORT||4180);if(!Number.isInteger(port)||port<1024||port>65535)throw Error('Invalid Z_UNITY_PORT');
const configRoot=process.env.Z_LIVE_CONFIG_DIR||root;
const env=loadEnvironment(configRoot);
const team=office?null:loadTeamClient();
if(team)env.DEEPSEEK_API_KEY='team-managed';
// One local character API configuration can serve both surfaces; never expose it to Unity.
if(!env.DEEPSEEK_API_KEY){const shared=process.env.Z_MEMORY_CONFIG||path.resolve(root,'../MemoryLab/.local/model.json');try{const c=JSON.parse(fs.readFileSync(shared,'utf8'));if(c.apiKey)env.DEEPSEEK_API_KEY=c.apiKey;}catch{}}
const voice=new NativeVoiceService({configRoot:process.env.Z_LIVE_VOICE_CONFIG_DIR||configRoot,profileRoot:path.join(runtime,'voiceprint')});
const singing=await SingingService.open({root,directory:path.join(runtime,'singing'),libraryDirectory:path.join(runtime,'library'),environment:()=>env,sources:[new NeteaseSource(),...(env.Z_SONG_CATALOG?[new CatalogSource({id:'configured',file:env.Z_SONG_CATALOG})]:[])],autoRenew:env.Z_NETEASE_AUTO_RENEW!=='0',onSessionStatus:status=>console.log('NetEase session:',status.status,status.renewal||'')});
const library=new LiveSongLibrary({root,directory:path.join(runtime,'library'),singing});
const providers=new Providers(env,{singing,singingContext:()=>singing.context('local-owner',{publicRoom:true}),...(team?{fetch:team.modelFetch('live',fetch,'https://api.deepseek.com/chat/completions'),modelSource:'team-ark',modelName:team.config.profiles.live.model}: {})});
const {server}=await createLiveServer({root:path.join(runtime,'sessions'),providers,artifacts:new ArtifactStore(path.join(runtime,'artifacts')),songRoot:path.join(root,'server/assets/songs'),token,library,voice,...(office?{tokens:office.tokens,tls:{key:fs.readFileSync(path.join(runtime,'key.pem')),cert:fs.readFileSync(path.join(runtime,'cert.pem'))}}:{})});
// Editor-only loopback credential: outside Assets, never included in a Player build.
server.listen(port,office?.host||'127.0.0.1',()=>{
 if(!office){const file=process.env.Z_LIVE_CONNECTION_FILE||path.resolve(root,'../../UserSettings/ZLive/local-connection.json');fs.mkdirSync(path.dirname(file),{recursive:true});fs.writeFileSync(file,JSON.stringify({baseUrl:`http://127.0.0.1:${port}`,token}),{mode:0o600});}
 console.log(`Unity live service ${office?'https':'http'}://${office?.host||'127.0.0.1'}:${port}; agent=${!!env.DEEPSEEK_API_KEY}, Vivi=${!!env.VOLC_TTS_API_KEY}. Local connection file ready.`);
});
server.on('error',e=>{console.error(`Server failed: ${e.code||'unknown'}`);void singing.close().finally(()=>{library.close();process.exit(1);});});
for(const sig of ['SIGINT','SIGTERM'])process.on(sig,()=>{server.closeAllConnections();void singing.close().finally(()=>{library.close();server.close(()=>process.exit(0));});setTimeout(()=>process.exit(0),3000).unref();});
