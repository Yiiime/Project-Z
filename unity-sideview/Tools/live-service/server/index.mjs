import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomBytes} from 'node:crypto';
import {Providers,ArtifactStore,loadEnvironment} from './providers.mjs';
import {LiveSongLibrary} from './song-library/index.mjs';
import {SingingService} from './singing/service.mjs';
import {createLiveServer} from './http.mjs';
const root=fileURLToPath(new URL('../',import.meta.url)),runtime=path.join(root,'.runtime');fs.mkdirSync(runtime,{recursive:true,mode:0o700});
const lock=path.join(runtime,'server.lock');try{const fd=fs.openSync(lock,'wx',0o600);fs.writeFileSync(fd,String(process.pid));fs.closeSync(fd);}catch{let live=true;try{process.kill(Number(fs.readFileSync(lock,'utf8')),0);}catch{live=false;}if(live)throw Error('Another server owns this store.');fs.unlinkSync(lock);fs.writeFileSync(lock,String(process.pid),{flag:'wx',mode:0o600});}
const cleanup=()=>{try{fs.unlinkSync(lock);}catch{}};process.on('exit',cleanup);
const tokenFile=path.join(runtime,'local-token');if(!fs.existsSync(tokenFile))fs.writeFileSync(tokenFile,randomBytes(32).toString('hex'),{mode:0o600});const token=fs.readFileSync(tokenFile,'utf8').trim();
const port=Number(process.env.Z_UNITY_PORT||4180);if(!Number.isInteger(port)||port<1024||port>65535)throw Error('Invalid Z_UNITY_PORT');
const env=loadEnvironment(root);
const singing=await SingingService.open({root,directory:path.join(runtime,'singing'),environment:()=>env});
const library=new LiveSongLibrary({root,directory:path.join(runtime,'library'),singing});
const providers=new Providers(env,{singing,singingContext:()=>singing.context('local-owner')});
const {server}=await createLiveServer({root:path.join(runtime,'sessions'),providers,artifacts:new ArtifactStore(path.join(runtime,'artifacts')),songRoot:path.join(root,'server/assets/songs'),token,library});
// Editor-only loopback credential: outside Assets, never included in a Player build.
const configDir=path.resolve(root,'../../UserSettings/ZLive');fs.mkdirSync(configDir,{recursive:true});fs.writeFileSync(path.join(configDir,'local-connection.json'),JSON.stringify({baseUrl:`http://127.0.0.1:${port}`,token}),{mode:0o600});
server.listen(port,'127.0.0.1',()=>console.log(`Unity live service http://127.0.0.1:${port}; agent=${!!env.DEEPSEEK_API_KEY}, Vivi=${!!env.VOLC_TTS_API_KEY}. Local connection file ready.`));
server.on('error',e=>{console.error(`Server failed: ${e.code||'unknown'}`);void singing.close().finally(()=>{library.close();process.exit(1);});});
for(const sig of ['SIGINT','SIGTERM'])process.on(sig,()=>{server.closeAllConnections();void singing.close().finally(()=>{library.close();server.close(()=>process.exit(0));});setTimeout(()=>process.exit(0),3000).unref();});
