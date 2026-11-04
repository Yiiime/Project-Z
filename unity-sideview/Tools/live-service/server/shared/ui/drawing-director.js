import {validateDrawingScene,renderDrawingScene,drawingText} from './drawing-scene.js';
export class DrawingDirector {
  constructor({generate,changed,event,now=()=>0}){Object.assign(this,{generate,changed,event,now});this.current=null;this.controller=null;this.paused=false;}
  publish(){this.changed(this.current);}
  valid(d,revision){return this.current===d&&d.revision===revision&&!['cancelled','failed'].includes(d.phase);}
  start({request,owner='Z',requesterId=null}){
    try{drawingText(request,600);}catch{return false;}
    this.controller?.abort();
    const d=this.current={id:crypto.randomUUID(),mode:'generated_vector',revision:0,request,owner,requesterId,title:'正在构思',phase:'preparing',stage:-1,stageCount:0,stages:[],scene:null,svg:'',open:false,manuallyHidden:false,displayed:null,displayedRevision:null,artifactId:null,history:[],checks:null,reason:''};
    this.publish();void this.prepare(d);return true;
  }
  edit({taskId,baseRevision,instruction,targetIds}){
    const d=this.current;
    try{drawingText(instruction,600);}catch{return false;}
    if(!d||d.id!==taskId||d.revision!==baseRevision||!['ready','drawing','completed','failed'].includes(d.phase)||!d.scene||!Array.isArray(targetIds)||!targetIds.length||targetIds.length>16||new Set(targetIds).size!==targetIds.length||targetIds.some(id=>!d.scene.elements.some(e=>e.id===id)))return false;
    const base=structuredClone(d.scene);
    d.history=[...d.history,{revision:d.revision,scene:base,artifactId:d.artifactId}].slice(-5);
    d.revision++;d.phase='editing';d.reason='';d.due=null;this.controller?.abort();
    this.publish();void this.prepare(d,{base,edit:{instruction,targetIds:[...targetIds]}});return true;
  }
  async prepare(d,extra={}){
    const revision=d.revision,controller=new AbortController();this.controller=controller;
    try{
      const response=await this.generate({kind:'artwork',requestId:crypto.randomUUID(),eventId:d.id,message:'生成当前绘画产物',context:{drawingJob:{taskId:d.id,revision,request:d.request,...extra}}},{signal:controller.signal});
      if(!this.valid(d,revision)||controller.signal.aborted)return;
      if(response.taskId!==d.id||response.revision!==revision)throw Error('stale_artwork');
      if(response.status==='unavailable'){d.reason=response.reason;throw Error('drawing_unavailable');}
      const scene=validateDrawingScene(response.scene);
      // Defence in depth: even a forged API reply cannot mutate untargeted objects.
      if(extra.base){const targets=new Set(extra.edit.targetIds);for(const e of extra.base.elements)if(!targets.has(e.id)&&JSON.stringify(scene.elements.find(n=>n.id===e.id))!==JSON.stringify(e))throw Error('unrelated_edit');}
      d.scene=scene;d.title=scene.title;d.stages=scene.stages;d.stageCount=scene.stages.length;
      d.stage=extra.base?d.stageCount-1:0;d.svg=renderDrawingScene(scene,d.stage);
      d.artifactId=crypto.randomUUID();d.displayed=null;d.checks={structure:'pass',semantic:'unverified',visual:'unverified'};d.phase='ready';
      this.event('drawing.ready','当前版本的矢量笔画已生成，尚未确认展示；由主 Agent 决定何时打开画板。');this.publish();
    }catch(error){if(this.valid(d,revision)&&!controller.signal.aborted){
      const reasons={agent_timeout:'这次绘画生成超时。',AbortError:'这次绘画生成超时。',agent_invalid_reply:'这次生成的笔画未通过校验。',agent_provider_unavailable:'绘画服务暂时不可用。',agent_not_configured:'绘画服务尚未连接。',agent_busy:'绘画服务暂时繁忙。'};
      d.phase='failed';d.reason=d.reason||(reasons[error?.name==='AbortError'?'AbortError':error?.message]||'这次绘画生成未完成。')+(d.scene?'上一版仍保留，可以继续聊天。':'可以先继续聊天。');
      this.event('drawing.failed',d.reason);this.publish();
    }}
    finally{if(this.controller===controller)this.controller=null;}
  }
  advance(){const d=this.current;if(!d||this.paused||!d.open||d.phase!=='drawing'||d.due==null||this.now()<d.due)return;
    d.stage++;d.svg=renderDrawingScene(d.scene,d.stage);d.artifactId=crypto.randomUUID();d.displayed=null;d.due=null;d.phase='ready';this.publish();}
  displayed(taskId,revision,stage){const d=this.current;
    if(!d||!d.open||this.paused||d.id!==taskId||d.revision!==revision||d.stage!==stage||!['ready','drawing','completed'].includes(d.phase)||d.displayed===`${revision}:${stage}`)return false;
    d.displayed=`${revision}:${stage}`;d.displayedRevision=revision;
    const final=stage===d.stageCount-1;d.phase=final?'completed':'drawing';d.due=final?null:this.now()+1600;
    const labels=d.scene.elements.filter(e=>e.stage<=stage).map(e=>e.label);
    this.event('drawing.displayed',JSON.stringify({taskId:d.id,revision,stage,final,title:d.title,objects:labels.slice(0,12),evidence:'安全矢量已在画板渲染；对象名称来自生成数据，语义效果待观众确认'}));this.publish();return true;
  }
  cancel(){const d=this.current;if(!d)return;this.controller?.abort();this.controller=null;d.phase='cancelled';d.due=null;this.publish();}
  reset(){this.controller?.abort();this.controller=null;this.current=null;this.publish();}
  setPaused(value){this.paused=value;}
}
