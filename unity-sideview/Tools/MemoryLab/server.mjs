import http from 'node:http';
import { readFileSync,writeFileSync,mkdirSync,chmodSync,existsSync } from 'node:fs';
import { dirname,resolve,join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Store } from './lib/store.mjs';
import { ModelProvider,AppError } from './lib/provider.mjs';
import { GameEngine } from './lib/game-engine.mjs';
const ROOT=dirname(fileURLToPath(import.meta.url));

export function createMemoryServer({dbPath=join(ROOT,'.data/game.sqlite'),configPath=join(ROOT,'.local/model.json'),providerFactory,allowTestEndpoint=false,timeoutMs=30000}={}) {
  const store=new Store(dbPath);
  const provider=providerFactory?providerFactory(store):new ModelProvider(store,{allowTestEndpoint});
  let configurationRevision=1,configError='',testController=null;
  if(!providerFactory&&existsSync(configPath)) {
    try {const c=JSON.parse(readFileSync(configPath,'utf8'));if(c.apiKey)provider.set(c);}
    catch {configError='本机模型配置未能读取，请在配置页重新保存。';}
  }
  const engine=new GameEngine(store,provider,{timeoutMs});
  const status=()=>({ok:true,app:'project-z-seven-memory',protocol:1,mode:provider.mode||'real',configurationRevision,...provider.publicConfig(),configError});
  function json(res,value,code=200){res.writeHead(code,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store'});res.end(JSON.stringify(value));}
  async function body(req) {
    let raw='';for await(const chunk of req){raw+=chunk;if(Buffer.byteLength(raw)>8000000)throw new AppError('请求过大',413);}
    try{return JSON.parse(raw||'{}');}catch{throw new AppError('请求JSON无效');}
  }
  const server=http.createServer(async(req,res)=>{
    res.setHeader('X-Content-Type-Options','nosniff');res.setHeader('Referrer-Policy','no-referrer');
    res.setHeader('Content-Security-Policy',"default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'");
    try {
      const host=req.headers.host||'';
      if(!/^(localhost|127\.0\.0\.1)(:\d+)?$/.test(host))throw new AppError('仅允许本机访问',403);
      const url=new URL(req.url,'http://'+host);
      if(req.method==='GET') {
        if(url.pathname==='/api/health'||url.pathname==='/api/config')return json(res,status());
        const files={'/':'settings.html','/settings':'settings.html','/settings.js':'settings.js','/settings.css':'settings.css'};
        const file=files[url.pathname];if(!file)throw new AppError('接口不存在',404);
        res.writeHead(200,{'Content-Type':file.endsWith('.html')?'text/html; charset=utf-8':file.endsWith('.js')?'text/javascript; charset=utf-8':'text/css; charset=utf-8','Cache-Control':'no-store'});
        return res.end(readFileSync(join(ROOT,'public',file)));
      }
      if(req.method!=='POST')throw new AppError('不支持的请求方式',405);
      if(req.headers['x-z-memory']!=='1'||(req.headers.origin&&req.headers.origin!=='http://'+host))throw new AppError('请求来源无效',403);
      const input=await body(req);
      if(url.pathname==='/api/config') {
        testController?.abort();engine.cancelAll();
        provider.set(input);
        const cfg={...provider.config,apiKey:provider.key};
        mkdirSync(dirname(configPath),{recursive:true,mode:0o700});
        writeFileSync(configPath,JSON.stringify(cfg,null,2)+'\n',{mode:0o600});chmodSync(configPath,0o600);
        configError='';configurationRevision++;return json(res,status());
      }
      if(url.pathname==='/api/config/test') {
        testController?.abort();testController=new AbortController();const c=testController,t=setTimeout(()=>c.abort(),30000);
        try{await provider.call(provider.request([{role:'user',content:'请只回复 OK。'}],{maxTokens:100}),{signal:c.signal});return json(res,{...status(),message:'模型连接成功。'});}
        finally{clearTimeout(t);if(testController===c)testController=null;}
      }
      let result;
      switch(url.pathname) {
        case '/api/game/open':result=engine.open(input);break;
        case '/api/game/sync':result=await engine.sync(input);break;
        case '/api/game/intent':result=await engine.intent(input);break;
        case '/api/game/utterance':result=await engine.utterance(input);break;
        case '/api/game/checkpoint':result=engine.response(engine.assert(input));break;
        case '/api/game/cancel':engine.cancel(input);result={ok:true};break;
        case '/api/game/close':result=engine.close(input);break;
        default:throw new AppError('接口不存在',404);
      }
      if(!res.destroyed)json(res,result);
    } catch(error) {
      if(!res.destroyed)json(res,{ok:false,error:error instanceof AppError?error.message:'记忆服务处理失败，请稍后重试。'},error instanceof AppError?error.status:500);
    }
  });
  server.on('close',()=>{engine.cancelAll();testController?.abort();});
  return {server,store,provider,engine,status};
}

if(process.argv[1]&&resolve(process.argv[1])===fileURLToPath(import.meta.url)) {
  const port=Number(process.env.Z_MEMORY_PORT||4330);
  const app=createMemoryServer({dbPath:process.env.Z_MEMORY_DB||join(ROOT,'.data/game.sqlite'),configPath:process.env.Z_MEMORY_CONFIG||join(ROOT,'.local/model.json')});
  app.server.listen(port,'127.0.0.1',()=>console.log(`七人记忆服务：http://127.0.0.1:${port}/settings`));
  app.server.on('error',()=>{console.error('记忆服务启动失败，请检查端口是否已占用。');app.store.close();process.exitCode=1;});
  for(const signal of ['SIGINT','SIGTERM'])process.on(signal,()=>{app.engine.cancelAll();app.server.close(()=>{app.store.close();process.exit(0);});});
}
