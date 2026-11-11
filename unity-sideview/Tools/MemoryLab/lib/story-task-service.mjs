import {mkdirSync,readFileSync,writeFileSync,existsSync,readdirSync} from 'node:fs';
import {join} from 'node:path';
import {randomUUID} from 'node:crypto';
import {renderDrawing} from './story-raster.mjs';

export function validateStoryTask(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) throw Error('委托格式无效');
  const result={};
  for(const key of ['title','request','item','container']) {
    if(typeof input[key]!=='string'||!input[key].trim()||input[key].length>180)throw Error('委托缺少可执行的物品或说明');
    result[key]=input[key].trim();
  }
  if(result.item===result.container)throw Error('待拾取物与容器必须不同');
  return result;
}

export class StoryTaskService {
  constructor({root,provider,catalog,engine,imageCall}) {
    this.root=root;this.provider=provider;this.catalog=catalog;this.engine=engine;this.imageCall=imageCall;
    this.assetMode=process.env.Z_STORY_ASSET_MODE||'vector';
    this.jobs=new Map();mkdirSync(root,{recursive:true});
    for(const id of readdirSync(root).filter(id=>/^[a-f0-9]{32}$/.test(id)))try{
      const job=JSON.parse(readFileSync(join(root,id,'task.json'),'utf8'));
      if(job.id!==id)continue;if(job.status==='generating'){job.status='failed';job.error='准备时服务已重启，请重新生成委托。';}
      this.jobs.set(id,job);
    }catch{}
  }
  async plan(p) {
    this.catalog.actor(p.actorId);
    const previous=[...this.jobs.values()].find(j=>j.world===p.worldId&&j.requestId===p.requestId);
    if(previous)return this.public(previous);
    if([...this.jobs.values()].some(j=>j.world===p.worldId&&j.status==='generating'))throw Error('这一局已有委托正在准备');
    const w=this.engine.world(p.worldId);
    const events=this.engine.records(w,'event').filter(e=>(e.visible||[]).includes(p.actorId)).slice(-10).map(e=>({text:e.text,speaker:e.speaker}));
    const prompt='为2D横版小岛游戏中的该NPC临时设计一件日常委托。根据本人人设、已知记忆和当前聊天，需要玩家做一件有具体理由的小事。唯一可执行动作是：拾起一个小物体，走几步，放进一个独立容器。物体和容器都会现场生图。只能在当前地点内完成；不需要其他NPC、金钱、战斗或修理，不解锁离岛权限。只输出JSON，四个字符串键title/request/item/container；request用NPC第一人称说明委托，item/container是便于画成独立完整物体的具体外观，分别至多35字。不得说任务已经完成。';
    const choice=p.details?.spontaneous?'先根据刚结束的交流，判断是否自然产生了一个需要玩家帮忙的小事。如果没有，返回 {"offer":false}；不要为了发任务而发任务。如果有，返回上述四个字段。':'玩家主动请你提出一个适合当下的小委托。';
    const request=this.provider.request([{role:'system',content:prompt+'\n不要重复近期已经完成的委托。\n'+choice+'\n'+this.catalog.profile(p.actorId)},{role:'user',content:JSON.stringify({place:p.scene,story_points:p.details?.points||0,recent_events:events})}],{json:true,maxTokens:700});
    const reply=await this.provider.call(request,{signal:AbortSignal.timeout(25000)});
    let text=reply.text.trim().replace(/^```(?:json)?\s*|\s*```$/g,'');
    const parsed=JSON.parse(text);if(p.details?.spontaneous&&parsed.offer===false)return {status:'none'};
    const definition=validateStoryTask(parsed);
    const job={id:randomUUID().replaceAll('-',''),world:p.worldId,requestId:p.requestId,actor:p.actorId,...definition,status:'generating',assetMode:this.assetMode==='vector'?'AI 简绘 · 现场生成':'Seedream 现场生成'};
    this.jobs.set(job.id,job);this.persist(job);
    this.generate(job).catch(error=>{job.status='failed';job.error=String(error.message||error).slice(0,160);this.persist(job);});
    return this.public(job);
  }
  persist(job){mkdirSync(join(this.root,job.id),{recursive:true});writeFileSync(join(this.root,job.id,'task.json'),JSON.stringify(job));}
  get(id,world){
    if(!/^[a-f0-9]{32}$/.test(id||''))throw Error('委托标识无效');
    let job=this.jobs.get(id);
    if(!job&&existsSync(join(this.root,id,'task.json'))){job=JSON.parse(readFileSync(join(this.root,id,'task.json'),'utf8'));if(job.status==='generating'){job.status='failed';job.error='准备时服务已重启，请重新生成委托。';}this.jobs.set(id,job);}
    if(!job||job.world!==world)throw Error('当前存档没有这项委托');return this.public(job);
  }
  public(j){return Object.fromEntries(['id','actor','title','request','item','container','status','error','assetMode','itemUrl','containerUrl'].map(k=>[k,j[k]??'']));}
  async image(body) {
    if(this.imageCall)return this.imageCall(body);
    if(this.provider.config.provider!=='ark'||!this.provider.key)throw Error('当前模型配置未提供方舟生图凭证。');
    const response=await fetch(this.provider.config.baseUrl+'/images/generations',{method:'POST',headers:{'Content-Type':'application/json',Authorization:'Bearer '+this.provider.key},body:JSON.stringify({model:'doubao-seedream-5-0-flash-260915',size:'1K',response_format:'b64_json',output_format:'png',watermark:false,...body}),signal:AbortSignal.timeout(150000)});
    const data=await response.json();if(!response.ok)throw Error('生图服务暂不可用：'+(data.error?.code||response.status));
    if(!Array.isArray(data.data)||!data.data.length)throw Error('生图没有返回图片');return data.data;
  }
  async bytes(image){
    let bytes;
    if(image.b64_json)bytes=Buffer.from(image.b64_json,'base64');
    else if(image.url){const response=await fetch(image.url,{signal:AbortSignal.timeout(30000)});if(!response.ok)throw Error('图片下载失败');bytes=Buffer.from(await response.arrayBuffer());}
    if(!bytes||bytes.length>32*1024*1024||bytes.subarray(0,8).toString('hex')!=='89504e470d0a1a0a')throw Error('图片不是有效PNG');
    return bytes;
  }
  async generate(job) {
    await Promise.all(['item','container'].map(async kind=>{
      if(this.assetMode==='vector') {
        const prompt='你是2D游戏道具画师。请在512×512透明画布上画一个独立完整物体：'+job[kind]+'。温暖手绘风的简化矢量插画，主体居中，留40像素透明边缘，不画背景、地板、文字、人物。使用15至45个有层次的几何形状画清楚轮廓、部件和明暗。只输出JSON {"shapes":[...]}。形状按绘制先后叠加，每项必须有fill为#RRGGBB。支持type=rect或ellipse，字段x,y,w,h为外接矩形左上角和宽高；type=polygon的points是[[x,y],...]。坐标与宽高都在0..512。不要输出SVG或代码。';
        let drawing,png,error;
        const messages=[{role:'user',content:prompt+' 所有坐标使用直接的数字，禁止加减表达式和公式。'}];
        for(let attempt=0;attempt<3;attempt++){
          const request=this.provider.request(messages,{json:true,maxTokens:4500});
          const reply=await this.provider.call(request,{signal:AbortSignal.timeout(60000)});
          try{drawing=JSON.parse(reply.text.trim().replace(/^```(?:json)?\s*|\s*```$/g,''));png=renderDrawing(drawing);break;}
          catch(e){error=e;messages.push({role:'assistant',content:reply.text.slice(0,15000)},{role:'user',content:'请修正JSON：'+e.message+'。可以减少为12至20个形状。所有坐标必须是数字字面量，不要公式。只返回完整JSON。'});}
        }
        if(!png)throw error;
        writeFileSync(join(this.root,job.id,kind+'.png'),png);
        writeFileSync(join(this.root,job.id,kind+'.drawing.json'),JSON.stringify(drawing));
        job[kind+'Url']='/api/story-assets/'+job.id+'/'+kind+'.png';return;
      }
      const prompt='2D横版海滨小镇游戏的独立道具：'+job[kind]+'。单一完整主体，纯白背景，严格侧面或轻微四分之三视角，温暖手绘插画，柔和边缘，清楚轮廓，中等细节，无人物、文字、阴影、地面、场景。主体居中完整展示，周围留空。';
      const original=await this.image({prompt});const source=await this.bytes(original[0]);
      const layers=await this.image({image:'data:image/png;base64,'+source.toString('base64'),layer_decomposition:true,prompt:'只分离图中这一个完整物体，所有部件保留在同一个透明前景图层，另一个为背景底图。不要拆散物体。'});
      const layer=layers.filter(x=>Number(x.z_index)>0).sort((a,b)=>Number(a.z_index)-Number(b.z_index))[0];
      if(!layer)throw Error('没有取得可用的透明物体图层');
      writeFileSync(join(this.root,job.id,kind+'.png'),await this.bytes(layer));
      job[kind+'Url']='/api/story-assets/'+job.id+'/'+kind+'.png';
    }));
    job.status='ready';this.persist(job);
  }
}
