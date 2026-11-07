import {SingingCore,reasons} from './singing-core.js';
import {parseSongRequest} from './song-parser.js';
import {Z_VOICE_PROFILE} from '../ui/talent-config.js';
const digest=async bytes=>[...new Uint8Array(await crypto.subtle.digest('SHA-256',bytes))].map(x=>x.toString(16).padStart(2,'0')).join('');
const safeCode=e=>/^[a-z_]{2,80}$/.test(e?.message||'')?e.message:'tool_unavailable';
const json=(data,status=200)=>new Response(JSON.stringify(data),{status,headers:{'Content-Type':'application/json; charset=utf-8','Cache-Control':'private, no-store','X-Content-Type-Options':'nosniff'}});
const expired=t=>['parsing','generating','checking'].includes(t.status)&&t.leaseUntil<Date.now();
export class CloudSinging {
  constructor(env,owner,seed){this.env=env;this.owner=owner;this.seed=seed;}
  initial(){return {version:1,voice:Z_VOICE_PROFILE,tasks:[],songs:[{id:'wanan-yanrenzhong',title:'晚安',artist:'颜人中',version:'',coverage:['第一段完整副歌'],source:'existing_user_review',independentScoreAvailable:false}],assets:[structuredClone(this.seed)],events:[],playbacks:[]};}
  async load(){let row=await this.env.DB.prepare('SELECT value,revision FROM singing_state WHERE owner=?').bind(this.owner).first();if(!row){await this.env.DB.prepare('INSERT OR IGNORE INTO singing_state(owner,value,updated_at) VALUES(?,?,?)').bind(this.owner,JSON.stringify(this.initial()),Date.now()).run();row=await this.env.DB.prepare('SELECT value,revision FROM singing_state WHERE owner=?').bind(this.owner).first();}return {state:JSON.parse(row.value),revision:row.revision};}
  // Optimistic transactions are retried only for synchronous state transitions.
  // Provider calls and file uploads never occur inside this retry loop.
  async mutate(fn){for(let retry=0;retry<8;retry++){const {state,revision}=await this.load(),before=JSON.stringify(state),core=new SingingCore(state),result=fn(core,state);if(result?.then)throw Error('async_transaction_not_allowed');const value=JSON.stringify(state);if(value===before)return result;const r=await this.env.DB.prepare('UPDATE singing_state SET value=?,revision=revision+1,queued=?,updated_at=? WHERE owner=? AND revision=?').bind(value,Number(state.tasks.some(t=>t.status==='queued'&&t.plan)),Date.now(),this.owner,revision).run();if(r.meta.changes===1)return result;}throw Error('state_busy');}
  async recover(){await this.mutate((c,s)=>{c.expirePlaybacks();for(const t of s.tasks)if(expired(t))c.record(s,t,'failed','generation_outcome_unknown');});}
  async context(){await this.recover();const {state}=await this.load();return new SingingCore(state).context(this.owner);}
  async overview(){await this.recover();const {state}=await this.load(),r=await this.env.DB.prepare('SELECT seen FROM singing_runner WHERE id=1').first();return {...new SingingCore(state).overview(this.owner),runnerOnline:!!r&&Date.now()-r.seen<45000};}
  async parsePending(){
    const t=await this.mutate((c,s)=>{const t=s.tasks.find(t=>t.status==='queued'&&!t.plan);if(!t)return null;t.leaseUntil=Date.now()+45000;c.record(s,t,'parsing');return structuredClone(t);});if(!t)return;
    try{const plan=await parseSongRequest(t.requestText,{secret:this.env.DEEPSEEK_API_KEY,signal:AbortSignal.timeout(20000)});await this.mutate((c,s)=>{const v=c.owned(s,t.id,this.owner);if(v.revision!==t.revision||v.status!=='parsing')return;v.plan=plan;
      if(plan.intent!=='request'){v.question=plan.question;c.record(s,v,plan.intent==='clarify'?'needs_input':'rejected',plan.intent==='clarify'?'needs_material':'not_a_song_request');return;}
      if(!plan.artist&&new Set(s.songs.filter(x=>x.title===plan.title).map(x=>x.artist)).size>1){v.question='你想听哪个歌手或版本？';c.record(s,v,'needs_input','needs_material');return;}
      const a=c.findAsset(s,plan);if(a){v.assetId=a.id;c.record(s,v,'ready','',{cacheHit:true,assetId:a.id});return;}
      if(['full','other'].includes(plan.segment.kind)){v.question='请指定不超过90秒的第一句、主歌、副歌、桥段或明确时间范围。';c.record(s,v,'needs_input','range_needs_clarification');return;}
      c.record(s,v,'queued','waiting_generator');v.note='等待电脑端 Vivi 生成后台；电脑离线时保留排队';
    });}catch(e){await this.mutate((c,s)=>{const v=c.owned(s,t.id,this.owner);if(v.revision===t.revision&&v.status==='parsing')c.record(s,v,'failed',safeCode(e));});}
  }
  async taskAction(id,action,body){return this.mutate((c,s)=>{let result;if(action==='submit'){
      const existing=s.tasks.some(t=>t.sourceId===body.sourceId&&t.originSessionId===body.sessionId);
      if(!existing&&(s.tasks.length>=100||s.tasks.filter(t=>t.createdAt.slice(0,10)===new Date().toISOString().slice(0,10)).length>=10))throw Error('daily_task_limit');
      result=c.submit({...body,owner:this.owner});
    }else if(action==='clarify')result=c.clarify(id,this.owner,body.text);
    else if(action==='play')result=c.reserve(id,this.owner,body);
    else if(action==='receipt')result=c.receipt(id,this.owner,body);
    else if(['review','retry','cancel'].includes(action))result=c[action](id,this.owner,body);
    else throw Error('unknown_action');
    if(['retry','clarify'].includes(action)){result.plan=null;delete result.leaseToken;delete result.leaseUntil;}
    return result;
  });}
  async audio(id,allowUnreviewed){const {state}=await this.load(),a=state.assets.find(a=>a.id===id);if(!a||!a.approved&&!allowUnreviewed)throw Error('asset_unavailable');if(id===this.seed.id)return null;const data=await this.env.BUCKET.get(`singing/${this.owner}/${a.id}.wav`);if(!data)throw Error('asset_unavailable');const bytes=await data.arrayBuffer();if(await digest(bytes)!==a.sha256)throw Error('asset_integrity_failed');return new Response(bytes,{headers:{'Content-Type':'audio/wav','Cache-Control':'private, no-store','Cross-Origin-Resource-Policy':'same-origin'}});}
}
async function readBody(request,max=16384){const reader=request.body?.getReader();if(!reader)throw Error('invalid_request');let size=0;const chunks=[];for(;;){const p=await reader.read();if(p.done)break;size+=p.value.length;if(size>max){await reader.cancel();throw Error('body_too_large');}chunks.push(p.value);}const bytes=new Uint8Array(size);let at=0;for(const chunk of chunks){bytes.set(chunk,at);at+=chunk.length;}return bytes;}
async function authenticatedRunner(request,env){const supplied=request.headers.get('Authorization')||'',expected=env.Z_SINGING_RUNNER_TOKEN;if(!expected||supplied.length>256)return false;return await digest(new TextEncoder().encode(supplied))===await digest(new TextEncoder().encode('Bearer '+expected));}
async function renew(env,token){const r=await env.DB.prepare('UPDATE singing_runner SET expires=?,seen=? WHERE id=1 AND token=? AND expires>?').bind(Date.now()+300000,Date.now(),token,Date.now()).run();if(!r.meta.changes)throw Error('runner_lease_lost');}
export async function singingRunner(request,env,seed){
  if(!await authenticatedRunner(request,env))return json({error:'runner_unauthorized'},403);
  const action=new URL(request.url).pathname.split('/').at(-1);
  if(request.method!=='POST')return json({error:'method_not_allowed'},405);
  const raw=await readBody(request,action==='complete'?5000000:16384);
  const b=action==='complete'?{owner:request.headers.get('X-Singing-Owner'),taskId:request.headers.get('X-Singing-Task'),token:request.headers.get('X-Singing-Lease')}:JSON.parse(new TextDecoder().decode(raw));
  if(action==='claim'){
    const token=crypto.randomUUID(),time=Date.now();
    const previous=await env.DB.prepare('SELECT owner,expires FROM singing_runner WHERE id=1').first();
    if(previous?.owner&&previous.expires<time)await new CloudSinging(env,previous.owner,seed).recover();
    await env.DB.prepare('INSERT OR IGNORE INTO singing_runner(id,token,owner,task_id,expires,seen) VALUES(1,?,?,?,0,?)').bind('','','',time).run();
    const r=await env.DB.prepare('UPDATE singing_runner SET token=?,owner=?,task_id=?,expires=?,seen=? WHERE id=1 AND expires<?').bind(token,'','',time+300000,time,time).run();
    if(!r.meta.changes)return json({job:null,busy:true});
    const candidates=await env.DB.prepare('SELECT owner FROM singing_state WHERE queued=1 ORDER BY updated_at LIMIT 10').all();
    for(const row of candidates.results){const service=new CloudSinging(env,row.owner,seed);await service.recover();
      const job=await service.mutate((c,s)=>{const t=s.tasks.find(t=>t.status==='queued'&&t.plan);if(!t)return null;const cached=c.findAsset(s,t.plan);if(cached){t.assetId=cached.id;c.record(s,t,'ready','',{cacheHit:true,assetId:cached.id});return null;}
        t.leaseToken=token;t.leaseUntil=time+300000;t.attempt++;t.attemptId=crypto.randomUUID();c.record(s,t,'generating','',{attempt:t.attempt,attemptId:t.attemptId});return structuredClone(t);});
      if(!job)continue;
      // Global daily bound for an openly shared demo, charged at generation dispatch.
      const day=new Date().toISOString().slice(0,10);await env.DB.prepare('INSERT OR IGNORE INTO singing_budget(day,used) VALUES(?,0)').bind(day).run();
      const budget=await env.DB.prepare('UPDATE singing_budget SET used=used+1 WHERE day=? AND used<?').bind(day,Math.max(1,Math.min(100,Number(env.Z_SINGING_DAILY_LIMIT)||20))).run();
      if(!budget.meta.changes){await service.mutate((c,s)=>{const t=c.owned(s,job.id,row.owner);c.record(s,t,'failed','daily_generation_limit');});continue;}
      await env.DB.prepare('UPDATE singing_runner SET owner=?,task_id=? WHERE id=1 AND token=?').bind(row.owner,job.id,token).run();
      return json({job:{id:job.id,owner:row.owner,token,attemptId:job.attemptId,plan:job.plan,voice:job.voice}});
    }
    await env.DB.prepare('UPDATE singing_runner SET expires=0,seen=? WHERE id=1 AND token=?').bind(Date.now(),token).run();return json({job:null});
  }
  if(!/^[a-f0-9]{64}$/.test(b.owner||'')||typeof b.taskId!=='string'||typeof b.token!=='string')throw Error('invalid_runner_request');
  const service=new CloudSinging(env,b.owner,seed),{state}=await service.load(),task=state.tasks.find(t=>t.id===b.taskId);
  if(!task||task.leaseToken!==b.token)throw Error('runner_lease_lost');
  if((action==='complete'&&task.status==='awaiting_review')||(action==='fail'&&task.status==='cancelled')){await env.DB.prepare('UPDATE singing_runner SET expires=0,seen=? WHERE id=1 AND token=?').bind(Date.now(),b.token).run();return json({status:task.status,duplicate:true});}
  if(task.status!=='generating'||task.leaseUntil<Date.now())throw Error('runner_lease_lost');
  await renew(env,b.token);
  if(action==='heartbeat'){await service.mutate((c,s)=>{const t=c.owned(s,b.taskId,b.owner);if(t.leaseToken!==b.token||t.status!=='generating')throw Error('runner_lease_lost');t.leaseUntil=Date.now()+300000;});return json({ok:true});}
  if(action==='fail'){await service.mutate((c,s)=>{const t=c.owned(s,b.taskId,b.owner);if(t.leaseToken!==b.token||t.status!=='generating')throw Error('runner_lease_lost');c.record(s,t,'failed',safeCode({message:b.code}));});}
  else if(action==='complete'){
    const technical=await validateCloudWav(raw),id=task.attemptId;
    await env.BUCKET.put(`singing/${b.owner}/${id}.wav`,raw,{httpMetadata:{contentType:'audio/wav'}});
    await service.mutate((c,s)=>{const t=c.owned(s,b.taskId,b.owner);if(t.leaseToken!==b.token||t.status!=='generating')throw Error('runner_lease_lost');s.assets.push({id,plan:t.plan,voice:t.voice,...technical,checks:{technical:'pass',song:'unverified',segment:'unverified',voice:'unverified'},approved:false,createdAt:new Date().toISOString(),attemptId:t.attemptId});t.assetId=id;c.record(s,t,'awaiting_review','',{assetId:id});});
  }else throw Error('unknown_runner_action');
  await env.DB.prepare('UPDATE singing_runner SET expires=0,seen=? WHERE id=1 AND token=?').bind(Date.now(),b.token).run();return json({ok:true});
}
export async function validateCloudWav(bytes){const v=new DataView(bytes.buffer,bytes.byteOffset,bytes.byteLength),ascii=(at,n)=>new TextDecoder().decode(bytes.subarray(at,at+n));if(bytes.length<44||ascii(0,4)!=='RIFF'||ascii(8,4)!=='WAVE')throw Error('audio_decode_failed');let format,data;for(let at=12;at+8<=bytes.length;){const n=v.getUint32(at+4,true),end=at+8+n;if(end>bytes.length)throw Error('audio_truncated');if(ascii(at,4)==='fmt ')format={at:at+8,n};if(ascii(at,4)==='data')data={at:at+8,n};at=end+(n%2);}if(!format||format.n<16||!data?.n||data.n%2||v.getUint16(format.at,true)!==1||v.getUint16(format.at+14,true)!==16)throw Error('audio_format_unsupported');const channels=v.getUint16(format.at+2,true),rate=v.getUint32(format.at+4,true);if(channels!==1||rate!==24000)throw Error('audio_format_unsupported');const durationMs=Math.round(data.n/48000*1000);if(durationMs<100||durationMs>90000)throw Error('audio_range_exceeded');let energy=0;for(let i=data.at;i<data.at+data.n;i+=2)energy+=(v.getInt16(i,true)/32768)**2;if(Math.sqrt(energy/(data.n/2))<.0001)throw Error('audio_silent');return {durationMs,sha256:await digest(bytes)};}
export async function cloudSinging(request,env,ctx,seed,page){
  const url=new URL(request.url),p=url.pathname;
  if(!env.DB||!env.BUCKET)return {response:p==='/singing'||p.startsWith('/api/singing/')?json({error:'singing_storage_not_configured'},503):null};
  try{
    if(p.startsWith('/api/singing/runner/'))return {response:await singingRunner(request,env,seed)};
    if(!['/api/agent','/api/agent/config','/singing'].includes(p)&&!p.startsWith('/api/singing/'))return {};
    const identity=request.headers.get('oai-authenticated-user-id');if(!identity)return {response:p.startsWith('/api/singing/')?json({error:'visitor_required'},401):undefined};
    const owner=await digest(new TextEncoder().encode(identity)),service=new CloudSinging(env,owner,seed);
    const schedule=()=>ctx.waitUntil(service.parsePending().catch(()=>{}));
    if(p==='/api/agent'||p==='/api/agent/config')return {context:await service.context()};
    if(request.method==='POST'&&(request.headers.get('Origin')!==url.origin||!request.headers.get('Content-Type')?.startsWith('application/json')))return {response:json({error:'invalid_origin'},403)};
    if(p==='/singing'&&request.method==='GET')return {response:new Response(page,{headers:{'Content-Type':'text/html; charset=utf-8','Cache-Control':'private, no-store','Content-Security-Policy':"default-src 'self'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; media-src 'self'; connect-src 'self'; frame-ancestors 'none'"}})};
    if(p==='/api/singing/state'&&request.method==='GET'){schedule();return {response:json(await service.overview())};}
    const audio=p.match(/^\/api\/singing\/assets\/([a-zA-Z0-9-]+)$/);if(audio&&request.method==='GET')return {response:await service.audio(audio[1],url.searchParams.get('review')==='1'),seedAudio:audio[1]===seed.id};
    if(request.method!=='POST')return {response:json({error:'not_found'},404)};
    const body=JSON.parse(new TextDecoder().decode(await readBody(request)));
    if(p==='/api/singing/tasks'){const t=await service.taskAction(null,'submit',body);schedule();return {response:json(t,202)};}
    const route=p.match(/^\/api\/singing\/tasks\/([a-zA-Z0-9-]+)\/(review|retry|clarify|cancel|play)$/),receipt=p.match(/^\/api\/singing\/playbacks\/([a-zA-Z0-9-]+)$/);
    if(route||receipt){const result=await service.taskAction(route?.[1]||receipt[1],route?.[2]||'receipt',body);if(['retry','clarify'].includes(route?.[2]))schedule();return {response:json(result)};}
    return {response:json({error:'not_found'},404)};
  }catch(e){return {response:json({error:safeCode(e)},409)};}
}
