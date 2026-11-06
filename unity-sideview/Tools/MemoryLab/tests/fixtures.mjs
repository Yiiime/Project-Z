export class FixtureProvider {
  constructor(){this.mode='simulated';this.config={topK:6,provider:'fixture',model:'SIMULATED-TEST-ONLY',baseUrl:'http://127.0.0.1',apiType:'chat'};this.calls=[];this.delay=null;this.failure=null;}
  publicConfig(){return {...this.config,configured:true,verified:true,hasKey:false};}
  request(messages,options={}){return {messages,...options};}
  async call(req,options={}){
    this.calls.push(req);if(this.failure)throw this.failure;
    if(this.delayMs)await new Promise(r=>setTimeout(r,this.delayMs));
    if(this.delay)await this.delay;
    if(options.signal?.aborted&&!this.ignoreAbort)throw new Error('aborted');
    const sys=req.messages[0].content,p=JSON.parse(req.messages.at(-1).content);
    let value;
    if(sys.includes('选择当前 NPC 本轮有用'))value={selected:p.candidates.slice(0,6).map(m=>({id:m.id,relevance:1,reason:'fixture:相关经历'}))};
    else if(sys.includes('主动找一个在场NPC')){
      const remembered=p.memories.find(m=>m.speakerId&&p.candidates.some(c=>c.id===m.speakerId));
      value={actorId:p.self.id,targetId:remembered?.speakerId||p.candidates[0]?.id||'',reason:remembered?'我记得对方之前说过的话，想继续问。':'想了解身边人的打算。',topic:p.publicFacts.length?'小岛通知与各自的打算':'今天的生活与最近的事情'};
    }else if(sys.includes('你已经与对方走到交谈距离'))value={actorId:p.self.id,text:p.publicFacts.length?`${p.self.name}：关于小岛将要毁灭的通知，我想听听你的打算，也说说我能做的。`:`${p.self.name}：今天想和你聊聊最近的事情。有什么需要一起想办法的吗？`,endConversation:p.turn>=5};
    else if(sys.includes('为观察者 NPC 整理')){
      value={memories:p.messages.filter(m=>m.speakerId!==p.observer.id).slice(0,4).map(m=>({kind:'hearsay',content:`${m.speaker}说：${m.text}`,gist:`${m.speaker}谈过最近的事情和打算。`,subject:m.speaker,keywords:[m.speaker],importance:2,emotion:1,evidence:[{id:m.id,quote:m.text}],replaces:null})),used_memories:[]};
    }else if(sys.includes('根据 NPC 当前记得')){
      const s=p.memories.find(m=>m.kind!=='reflection'&&p.new_ids.includes(m.id));
      value={thoughts:s?[{content:'也许应该先问清楚对方愿意承担什么。',gist:'也许先听听对方的打算更合适。',subject:'相处',supports:[s.id],counterexamples:[],replaces:null}]:[]};
    }else throw new Error('Unknown fixture request');
    return {text:JSON.stringify(value),usage:{},finishReason:'stop'};
  }
}
