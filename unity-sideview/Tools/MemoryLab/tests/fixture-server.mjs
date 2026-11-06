import { createMemoryServer } from '../server.mjs';
import { FixtureProvider } from './fixtures.mjs';
const path=process.env.Z_MEMORY_DB;
if(!path||!path.startsWith('/tmp/'))throw new Error('Fixture server requires an explicitly isolated /tmp database.');
const app=createMemoryServer({dbPath:path,configPath:'/tmp/z-seven-memory/unused-test-model.json',providerFactory:()=>{
  const p=new FixtureProvider();p.delayMs=Number(process.env.Z_MEMORY_FIXTURE_DELAY_MS||100);
  if(process.env.Z_MEMORY_NO_MODEL==='1'){const status=p.publicConfig.bind(p);p.publicConfig=()=>({...status(),configured:false,verified:false});}
  return p;
},timeoutMs:10000});
app.server.listen(Number(process.env.Z_MEMORY_PORT||4329),'127.0.0.1',()=>console.log('SIMULATED MODEL — isolated Unity verification service ready.'));
for(const s of ['SIGINT','SIGTERM'])process.on(s,()=>{app.engine.cancelAll();app.server.close(()=>{app.store.close();process.exit(0);});});
