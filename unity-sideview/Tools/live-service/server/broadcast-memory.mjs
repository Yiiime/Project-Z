const phases={ready:'已准备好',playing:'开始播放',completed:'已播完',interrupted:'播放中断',failed:'准备或播放失败'};
const roles={'player.message':'player','speech.completed':'z'};
const compact=s=>s.replace(/[\p{P}\p{Z}\s]/gu,'');
function expressed(text,phrase){
  return text.split(/[。！？!?]/u).some(clause=>{
    const value=compact(clause),at=value.indexOf(compact(phrase));
    return at>=0 && !/(?:不|并非|没有说|不能说|难道)$/.test(value.slice(0,at));
  });
}
const followUp=/后来|然后|接着|继续|再讲|还有|为什么|怎么|能说|上次讲/;
const songTopic=/歌|唱|播完/;
const grams=text=>new Set([...text].slice(1).map((c,i)=>text[i]+c));

function publicBinding(b) {
  const entries=Array.isArray(b.entries)?b.entries:[];
  const stories=Array.isArray(b.stories)?b.stories:[];
  if(entries.length>32||stories.length>12)throw Error('invalid_memory_binding');
  const text=(s,n)=>typeof s==='string'&&s.length>0&&s.length<=n;
  for(const e of entries)if(!e || !text(e.id,180)||!text(e.text,240)||!text(e.speaker,64)||
    !['daily','shared'].includes(e.layer)||!Number.isInteger(e.day)||e.day<1||e.day>b.day)throw Error('invalid_memory_entry');
  for(const s of stories)if(!s||!text(s.id,80)||!['anchor','crack'].includes(s.layer)||
    !Array.isArray(s.steps)||(s.steps.length<1||s.steps.length>3)||!s.steps.every(v=>text(v,100))||!Array.isArray(s.topics)||
    s.topics.length>16||!s.topics.every(v=>text(v,32))||!Number.isInteger(s.count)||s.count<0||s.count>s.steps.length)throw Error('invalid_memory_story');
  return {worldId:b.worldId,runId:b.runId,day:b.day,stage:b.stage==="verified_clue"?"verified_clue":"island_daily",entries:entries.map(({id,text,day,speaker,layer})=>({id,text,day,speaker,layer})),
    stories:stories.map(({id,layer,topics,steps,aliases,count})=>({id,layer,topics:[...topics],steps:[...steps],
      aliases:steps.map((_,i)=>Array.isArray(aliases?.[i])?aliases[i].filter(v=>text(v,60)).slice(0,6):[]),count}))};
}

export class BroadcastMemory {
  constructor(binding, saved) {
    if(!binding || !/^[a-f\d]{32}$/.test(binding.runId) || !/^[a-f\d]{32}$/.test(binding.worldId) ||
      !Number.isSafeInteger(binding.day) || binding.day<1)throw Error('invalid_memory_binding');
    this.binding=publicBinding(binding);
    this.pending=saved?.pending||[];
    this.last=saved?.last||0;
    this.lastTopic=saved?.lastTopic||null;
    this.offer=saved?.offer||null;
    this.spoken=saved?.spoken||{};
  }
  refresh(binding){
    if(binding.runId!==this.binding.runId || binding.worldId!==this.binding.worldId || binding.day!==this.binding.day)
      throw Error('invalid_memory_binding');
    this.binding=publicBinding(binding);
  }
  save(){return {binding:this.binding,pending:this.pending,last:this.last,lastTopic:this.lastTopic,offer:this.offer,spoken:this.spoken};}
  acknowledge(n){if(Number.isSafeInteger(n)&&n>=0&&n<=this.last)this.pending=this.pending.filter(row=>row.seq>n);}
  record(type,text,meta={},media=null){
    let speaker=roles[type], change=null;
    if(type==='player.message') {
      this.lastTopic={text,id:meta.id};this.offer=null;
    } else if(type==='speech.completed' || type==='speech.progress') {
      if(type==='speech.progress'){
        text=(text.match(/^[\s\S]*[。！？!?]/)||[])[0]||'';
        speaker='z';
      }
      const s=this.offer;
      const variants=s?[s.line,...(this.binding.stories.find(c=>c.id===s.id)?.aliases[s.step-1]||[])]:[];
      if(s && variants.some(v=>expressed(s.quote && text.includes(s.quote)?s.quote:text,v)) && this.spoken[s.id]?.source!==s.source) {
        change={id:s.id,step:s.step};
        this.spoken[s.id]={...change,source:s.source};this.offer=null;
      }
      if(type==='speech.progress'&&!change)return;
    } else if(type.startsWith('talent.') && media?.kind==='singing' && phases[media.phase] && type==='talent.'+media.phase) {
      speaker='engine';text='《'+media.title+'》'+phases[media.phase];
    } else if(type==='tool.result'){
      let result;try{result=JSON.parse(text);}catch{return;}
      if(result.status!=='persisted'||!['prepare_song','clarify_song'].includes(result.tool))return;
      speaker='engine';text='已收到点歌请求：'+result.request;
    } else return;
    const shared=speaker==='z' && !!this.lastTopic && meta.replyTo?.includes(this.lastTopic.id);
    this.pending.push({seq:++this.last,speaker,shared:!!shared,text:text.slice(0,360),...(change?{change}:{})});
  }
  recall(){
    const topic=this.lastTopic?.text||'', words=grams(topic), day=this.binding.day;
    const entries=(this.binding.entries||[]).map((e,i)=>{
      const overlap=[...grams(e.text)].filter(k=>words.has(k)).length;
      const songs=songTopic.test(topic)&&songTopic.test(e.text);
      return {e,score:overlap*5+(songs?4:0)+(/昨天|昨晚/.test(topic)&&e.day===day-1?4:0),i};
    }).filter(x=>!topic||x.score>0||/之前|上次|还记得|昨天|昨晚/.test(topic))
      .sort((a,b)=>b.score-a.score||b.i-a.i).slice(0,3).map(x=>x.e);
    const stories=this.binding.stories||[];
    const story=stories.find(s=>s.topics.some(t=>topic.includes(t))) ||
      (followUp.test(topic)&&[...stories].reverse().find(s=>(this.spoken[s.id]||s.count>0) && this.spoken[s.id]?.source!==this.lastTopic?.id));
    let next=null;
    if(story){
      const count=Math.max(story.count,this.spoken[story.id]?.step||0);
      if (count<story.steps.length && (!count||followUp.test(topic)||/[？?]/.test(topic)) && this.spoken[story.id]?.source!==this.lastTopic?.id){
        next={id:story.id,layer:story.layer,step:count+1,line:story.steps[count],source:this.lastTopic.id};
        if(this.offer?.source===next.source && this.offer.step===next.step && this.offer.id===next.id)next.quote=this.offer.quote;
        this.offer=next;
      }
      if(count)entries.unshift({id:story.id,layer:story.layer,status:['hinted','unfolding','unfolded'][count-1],text:story.steps.slice(0,count).join('。')});
    }
    return {day,stage:this.binding.stage,entries:entries.slice(0,3),next:next?{id:next.id,layer:next.layer,step:next.step,line:next.line}:null};
  }
}

