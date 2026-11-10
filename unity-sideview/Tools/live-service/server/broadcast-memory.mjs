import {createHash} from 'node:crypto';
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

// Only MemoryLab's verified map milestones have an authored public story.
const visit='和岛上的朋友一起去看了地图上还没有标出来的地方';
const explain='和大家说了说，为什么我想把那个地方留在地图上';
const topicKey=(kind,name)=>kind+'.'+createHash('sha256').update(name).digest('hex').slice(0,24);
const songKey=s=>topicKey('song',s.title);
const musicCue=/唱|点歌|学歌|听歌|首歌|副歌|音乐/;
const topicMatches=(summary,cue)=>{
  if(!cue)return false;
  const subject=summary.replace(/^聊到了|海边的|岛上的/gu,'');
  return [...grams(subject)].some(word=>cue.includes(word));
};

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
  const today=b.today??[];
  if(!Array.isArray(today)||today.length>128||!today.every(v=>text(v,90)))throw Error('invalid_daily_recap');
  const songs=b.songs??[];
  if(!Array.isArray(songs)||songs.length>32||songs.some(s=>!s || !text(s.title,48) ||
    (s.sourceId!=null&&!text(s.sourceId,100))))throw Error('invalid_day_songs');
  const toldItems=b.toldItems??[];
  if(!Array.isArray(toldItems)||toldItems.length>128||!toldItems.every(k=>/^(hello|map|(?:song|topic)\.[a-f0-9]{24})$/.test(k)))throw Error('invalid_told_items');
  const highlights=b.highlights??[];
  if(!Array.isArray(highlights)||highlights.length>32||!highlights.every(v=>text(v,32)))throw Error('invalid_day_highlights');
  return {highlights:highlights.filter(v=>today.includes('聊到了'+v)),toldItems:[...new Set(toldItems)],songs:songs.map(({title,sourceId})=>({title,sourceId})),today:[...new Set(today)],recapSpoken:b.recapSpoken===true,worldId:b.worldId,runId:b.runId,day:b.day,stage:b.stage==="verified_clue"?"verified_clue":"island_daily",entries:entries.map(({id,text,day,speaker,layer})=>({id,text,day,speaker,layer})),
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
    this.recapSpoken=!!saved?.recapSpoken;
    this.told=new Set([...(saved?.told||[]),...this.binding.toldItems]);
    this.legacyRecap=(this.binding.recapSpoken||this.recapSpoken)&&!this.told.size;
    this.cue='';
    this.clock=()=>0;
    this.nextTopicAt=saved?.nextTopicAt??(this.told.size?14000:0);
    this.offer=saved?.offer||null;
    this.spoken=saved?.spoken||{};
  }
  refresh(binding){
    if(binding.runId!==this.binding.runId || binding.worldId!==this.binding.worldId || binding.day!==this.binding.day)
      throw Error('invalid_memory_binding');
    this.binding=publicBinding(binding);
    for(const key of this.binding.toldItems)this.told.add(key);
    if(this.binding.recapSpoken&&!this.told.size)this.legacyRecap=true;
  }
  get song(){return this.binding.songs.findLast(s=>s.sourceId)||this.binding.songs.at(-1);}
  get hasStory(){return this.binding.today.includes(visit)||this.binding.today.includes(explain);}
  nextItem(){
    if(this.legacyRecap || (!this.binding.today.length&&!this.song))return null;
    if(this.clock()<this.nextTopicAt)return null;
    if(this.hasStory&&!this.told.has('map'))return 'map';
    const highlight=this.binding.highlights[0];
    if(highlight&&!this.hasStory&&!this.told.has(topicKey('topic',highlight)))return topicKey('topic',highlight);
    if(!this.told.size)return 'hello';
    if(this.song&&!this.told.has(songKey(this.song)) && (this.song.sourceId||this.musicRequested(this.song)))return songKey(this.song);
    return null;
  }
  musicRequested(song,cue=this.cue){return musicCue.test(cue)||cue.includes(song.title);}
  visibleSongs(cue){return this.binding.songs.filter(s=>this.told.has(songKey(s))||this.musicRequested(s,cue));}
  opening(item=this.nextItem()){
    if(!item)return null;
    if(item==='map'){
      const seen=this.binding.today.includes(visit);
      const start=this.told.size?'说起来，':'晚上好呀。';
      return start+(seen?'今天我跟朋友去看了个地方。翻开岛上的地图，那儿居然还是一片空白。':'今天跟大家聊起岛上的地图，有个地方我特别想把它标上去。')+
        (seen&&this.binding.today.includes(explain)?'回来还跟大家聊了聊，为什么我想把它标上去。':'');
    }
    if(item.startsWith('topic.')){
      const subject=this.binding.highlights.find(v=>topicKey('topic',v)===item);
      return (this.told.size?'说起来，':'晚上好呀。')+'我今天聊到'+subject+'，当时就想，晚上一定要来跟你们聊聊。你们也喜欢吗？';
    }
    const song=this.song;
    if(item==='hello')return song?.sourceId?'晚上好呀。白天有人问我，能不能学首歌，晚上唱给大家听。我答应了。':'晚上好呀，你们来啦。今晚想从哪儿聊起？';
    const task=song.sourceId?this.songTasks?.().find(t=>t.sourceId===song.sourceId):null;
    const lead=(this.told.has('map')||[...this.told].some(k=>k.startsWith('topic.')))?'对了，白天我还答应学一首歌，叫':'白天说的那首歌，叫';
    const title='《'+song.title+'》。';
    if(!song.sourceId)return '说到歌，今天刚好聊起过'+title;
    switch(task?.status){
      case 'ready':return lead+title+'我把'+(task.plan?.segment?.description||'其中一段')+'准备好了，待会儿想唱给你们听。';
      case 'completed':return lead+title+'我准备的是其中一段。';
      case 'scheduled':return lead+title+'那一段已经排上了。';
      case 'needs_input':return lead+title+'我还想确认一下要唱哪个版本。';
      case 'failed':case 'rejected':case 'cancelled':return lead+title+'这次还没准备好。';
      default:return lead+title+(task?'我还在准备，得再给我一点时间。':'我先记下来了。');
    }
  }
  save(){return {told:[...this.told],nextTopicAt:this.nextTopicAt,recapSpoken:this.recapSpoken,binding:this.binding,pending:this.pending,last:this.last,lastTopic:this.lastTopic,offer:this.offer,spoken:this.spoken};}
  acknowledge(n){if(Number.isSafeInteger(n)&&n>=0&&n<=this.last)this.pending=this.pending.filter(row=>row.seq>n);}
  record(type,text,meta={},media=null){
    let speaker=roles[type], change=null;
    if(type==='player.message'||type==='npc.message'){
      this.cue=text;
      this.cueId=meta.id;
    }
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
    const recap=type==='speech.completed' && !!this.recapSpeech && meta.speechId===this.recapSpeech;
    let item=recap?this.recapItem:null;
    if(type==='speech.completed'){
      // A direct answer about the song counts too, so it won't be introduced again.
      if(!item && this.song && text.includes(this.song.title))item=songKey(this.song);
      if(item){this.told.add(item);this.recapSpoken=true;}
      if(item || (this.hasStory && this.cue && /地图|空白|那个地方/.test(this.cue)))this.nextTopicAt=this.clock()+(item==='hello'?7000:14000);
      else this.nextTopicAt=Math.max(this.nextTopicAt,this.clock()+3500);
      if(this.cueId===this.cueAtStart || meta.replyTo?.includes(this.cueId)){
        this.cue='';
        this.cueId=null;
      }
      this.recapSpeech=null;
    }
    const shared=speaker==='z' && !!this.lastTopic && meta.replyTo?.includes(this.lastTopic.id);
    this.pending.push({seq:++this.last,speaker,...(item?{recapDay:this.binding.day,item}:{}),shared:!!shared,text:text.slice(0,360),...(change?{change}:{})});
  }
  recall(cue=this.cue){
    const topic=this.lastTopic?.text||'', words=grams(topic), day=this.binding.day;
    const yesterday=/昨天|昨晚/.test(topic), ownWords=/我.{0,12}(?:说|讲|提|发|点过)/.test(topic);
    const entries=(this.binding.entries||[]).filter(e=>!yesterday||e.day===day-1).map((e,i)=>{
      const overlap=[...grams(e.text)].filter(k=>words.has(k)).length;
      const songs=songTopic.test(topic)&&songTopic.test(e.text);
      return {e,score:overlap*5+(songs?4:0)+(ownWords&&e.speaker==="player"?24:0),i};
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
    const songs=this.visibleSongs(cue);
    const tasks=songs.length?this.songTasks?.() || []:[];
    const today=this.binding.today.filter(v=>(this.told.has('map')&&(v===visit||v===explain))||
      (v.startsWith('聊到了') && (topicMatches(v,cue)||this.told.has(topicKey('topic',v.slice(3))))));

    return {day,daySongs:songs.map(song=>({title:song.title,status:tasks.find(t=>t.sourceId===song.sourceId)?.status || (song.sourceId?"promised":"discussed")})),today,stage:this.binding.stage,entries:entries.slice(0,3),next:next?{id:next.id,layer:next.layer,step:next.step,line:next.line}:null};
  }
}

