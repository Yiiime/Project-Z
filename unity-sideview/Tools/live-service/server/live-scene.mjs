const tools = new Set(['draw_guest','offer_arrival','depart_island']);
export const isSceneTool = name => tools.has(name);
export function validateLiveScene(input) {
  if(input==null)return null;
  if(!['arrival','first_evening','daily'].includes(input.mode)||typeof input.playerName!=='string'||
     !input.playerName.trim()||input.playerName.length>48||!Number.isSafeInteger(input.day)||input.day<1||
     !/^[a-f0-9]{32}$/.test(input.worldId))throw Error('invalid_live_scene');
  return {mode:input.mode,playerName:input.playerName,worldId:input.worldId,day:input.day};
}
export function scenePrompt(scene) {
  if(!scene)return '';
  const common='\n当前游戏场景由宿主提供，优先于历史开场示例。人物台词全部由你结合人设自然表达，没有固定台词或节目单。arrival与first_evening不使用日常直播的7/8/10分钟节拍，忽略通用时长提示；按照现场进度自主推进。玩家名字是scene.playerName。只引用实际公开事实，不把计划或待执行工具说成已经完成。';
  if(scene.mode==='arrival')return common+'\n这是登岛前的一场普通晚间闲聊，岛民不在场，只有虚拟观众。先聊今天的小事、心情或直播间刚出现的弹幕，自然接话；开头不要提1000场、抽奖、名额、邀请或活动预告。'+
    'scene.warmup是已实际播放与显示的进度；ready=false时只完成一轮弹幕交流，不拖长暖场，每轮speech只说1–2个短句，22–38字、最多42字，不写长段落；第一轮讲一件小事，第二轮回应刚出现的弹幕，每轮必须提供3–4条virtualAudience=[{name,text}]，让不同观众聊刚听见的内容，每条不超过36字。用户名不能是岛民，不能提前知道还没公布的活动。'+
    '当scene.warmup.ready=true且scene.phase=opening时，才像突然想起来一样自然提起：原来今天已经是第1000场直播，想送一位朋友来月亮岛一起生活旅游7天。说完惊喜缘由后自主调用draw_guest打开礼盒，由玩家亲手点击拆开；不要反复预告，也不要追加举手、扣字等门槛。'+
    'scene.tools仅含当前可用工具。draw_guest args={}只展示待点击礼盒，不代表已经开奖；收到实际结果后先用一轮短话恭喜scene.playerName，并问愿不愿意来岛上；此时scene.phase=drawn且scene.reunion.greeted=false，先action=speak，不调用工具。恭喜这句话播完、玩家回应后，接住玩家的话并调用offer_arrival args={title,description}，标题简短，描述包含玩家名字且不超过100字。玩家确认后才用depart_island args={}告别。没有工具可用时继续自然闲聊，不虚构开奖或接受事实。'+
    '开场只使用scene.tools中的剧情工具，不唱歌、不点歌、不开始其他活动；唱歌留到登岛后的首夜直播。调用工具时必须action=tool并携带tool={name,args}，其余必填字段照常提供；action=speak或wait时不得携带tool。speech是执行前本轮自然台词。arrival不使用收播决定，省略sessionDecision。'+(scene.tools?.includes('draw_guest')?'\n当前暖场已完成，本轮必须action=tool，tool={name:"draw_guest",args:{}}。speech由你自然表达刚想起第1000场、邀请一位朋友来岛上住七天的惊喜缘由；不再另开闲聊话题。':'');
  if(scene.mode==='first_evening')return common+'\n首夜纯聊天约60秒，之后唱《晚安》并简短告别。本场以主动回忆今天为主，覆盖通用直播“不用盘点一天、只在话题相关时接一句”的默认节奏。前60秒每轮先从broadcast.today和character_context选一段实际公开经历作为主体，再简短接1条弹幕；不要被观众的风凉、催衣服等闲话连续带走。开场从broadcast.today选一条原始事实，用第一人称自然转述；后续换另一条记录。回忆的动作、地点与参与者严格以原记录为限，不补写玩家表现、牵手、沿途介绍等细节。只有记录明确说一起走到广场时才讲一起走；只有抵达码头和介绍记录时就只说在码头见到你、向大家介绍你。可以讲自己当下觉得开心，不替玩家编造当时的感受，也不说逛遍全岛。不要反复问观众想聊什么、想听什么。每轮speech约22–38字，最多42字，优先回应玩家；从host.inbox中选1–2条最相关弹幕自然接话，不逐条点名清空队列，wakeAfterMs=2000，普通聊天continuation=continue，边播放边准备下一轮，不等待弹幕队列全部清空。每轮提供2–3条virtualAudience=[{name,text}]，非岛民网名、每条不超过36字，围绕已发生的当天经历回应；聊天未到60秒时不主动催歌。scene.elapsedMs从首句实际播放起计。60秒后若玩家无新消息，主动说给大家唱一首《晚安》祝大家晚安，再调用start_singing，songId=wanan-yanrenzhong-vivi2-chorus。若玩家正在说话，先回应玩家，再自然接歌。歌曲实际完成后立即简短告别，不再开启闲聊，不反复唱歌。首夜六位岛民可以回应，梨园不参加。'+(scene.songDue?'\n聊天已满一分钟且没有待回应的玩家消息，本轮必须action=tool，tool={name:"start_singing",args:{songId:"wanan-yanrenzhong-vivi2-chorus"}}。用一句温柔的自然台词提出给大家唱《晚安》祝大家晚安；不再等NPC弹幕队列清空。':'');
  return common+'\n首夜或日常直播由你自然展开。先接住新朋友和当日可公开经历，不照着清单汇报；想唱歌时自行选合适歌曲并调用已有唱歌工具，歌曲没有固定安排。首夜六位岛民可以回应，梨园不参加。';
}
export class LiveScene {
  constructor(session,input,saved) {
    this.session=session;this.context=validateLiveScene(saved?.context||input);
    this.phase=saved?.phase||'opening';this.pending=null;this.invitation=saved?.invitation||null;
    this.warmup={speeches:0,messages:0,startedAt:session.engine._clock||0,...saved?.warmup};
    this.completedSpeeches=new Set(saved?.completedSpeeches||[]);
    this.reunion={greeted:false,playerReplied:false,...saved?.reunion};
  }
  warmupStatus(){const elapsed=Math.max(0,(this.session.engine._clock||0)-this.warmup.startedAt);return {...this.warmup,elapsedMs:elapsed,ready:this.warmup.speeches>=2&&this.warmup.messages>=2};}
  hostCompleted(id){if(this.context?.mode==='arrival'&&this.phase==='drawn')this.reunion.greeted=true;if(this.context?.mode!=='arrival'||this.phase!=='opening'||!id||this.completedSpeeches.has(String(id)))return;const wasReady=this.warmupStatus().ready;this.completedSpeeches.add(String(id));this.warmup.speeches++;this.warmupChanged(wasReady);}
  audienceDisplayed(){if(this.context?.mode==='arrival'&&this.phase==='opening'){const wasReady=this.warmupStatus().ready;this.warmup.messages++;this.warmupChanged(wasReady);}}
  warmupChanged(wasReady){
    if(wasReady||!this.warmupStatus().ready)return;
    // The next turn must see the newly available lottery, not a prefetched warmup.
    const e=this.session.engine;e.epoch=(e.epoch||0)+1;e.pending?.abort();e.pending=null;
    e._cancelPreparation?.('arrival_warmup_complete');e._cancelTopic?.('arrival_warmup_complete');
    this.session.speech?.dropWarmSpeech();e.speechPlan=null;e.nextWake=e._clock;
  }
  allowed() { if(this.context?.mode!=='arrival'||this.pending)return [];
    return this.phase==='opening'?(this.warmupStatus().ready?['draw_guest']:[]):this.phase==='drawn'&&this.reunion.greeted&&this.reunion.playerReplied?['offer_arrival']:this.phase==='accepted'?['depart_island']:[];
  }
  binding() {const e=this.session.engine;const facts=(this.session.broadcast?.binding?.today||[]).filter(text=>!text.includes('附近和岛上的朋友说了会儿话'));const recent=[...(e.session?.events?.filter(x=>x.type==='speech.completed').map(x=>x.text)||[]),e._active?.text].filter(Boolean);const turn=recent.length;return this.context?{...this.context,...(this.context.mode==='first_evening'&&facts.length?{recapFocus:turn<facts.length?facts[turn]:null,recentSpeech:recent.slice(-8)}:{}),elapsedMs:this.session.sessionControl?.eveningElapsed?.()||0,songDue:this.session.sessionControl?.eveningSongDue?.()||false,phase:this.phase,reunion:{...this.reunion},tools:this.allowed(),invitation:this.invitation,warmup:this.warmupStatus()}:null;}
  snapshot(){return {context:this.context,phase:this.phase,invitation:this.invitation,reunion:this.reunion,warmup:this.warmup,completedSpeeches:[...this.completedSpeeches]};}
  execute(tool) {
    if(!this.allowed().includes(tool.name))return false;
    if(tool.name==='offer_arrival') {
      if(typeof tool.args?.title!=='string'||tool.args.title.length>60||!tool.args.title.trim()||
        typeof tool.args.description!=='string'||tool.args.description.length>240||!tool.args.description.trim())return false;
      this.invitation={title:tool.args.title,description:tool.args.description};
    }
    if(tool.name==='draw_guest')this.session.engine._cancelPreparation?.('await_player_draw');
    const command=this.session.command('story.present',{tool:tool.name,playerName:this.context.playerName,...this.invitation});
    this.pending=command.commandId;return true;
  }
  playerReplied(){if(this.phase==='drawn'&&this.reunion.greeted){this.reunion.playerReplied=true;this.session.engine._cancelPreparation?.('arrival_player_reply');}}
  accept() {
    if(this.phase==='accepted')return true;
    if(this.pending || this.phase!=='offered')return false;
    this.phase='accepted';this.session.engine._public('story.fact','玩家已明确确认接受七天登岛邀请。',{speakerRole:'system'});
    this.session.engine.nextWake=this.session.engine._clock;return true;
  }
  receipt(command,payload) {
    if(command.commandId!==this.pending)throw Error('stale_story_command');
    if(command.payload.tool==='draw_guest'&&payload.status==='displayed'&&(payload.playerActivated!==true||payload.playerName!==this.context.playerName))throw Error('invalid_draw_evidence');
    this.pending=null;
    if(payload.status!=='displayed')return;
    const tool=command.payload.tool;
    if(tool==='draw_guest') {this.phase='drawn';this.session.engine._public('story.fact','实际抽奖结果：'+this.context.playerName+'获得七天登岛体验邀请。',{speakerRole:'system'});}
    if(tool==='offer_arrival')this.phase='offered';
    if(tool==='depart_island')this.phase='departed';
    this.session.engine.nextWake=this.session.engine._clock;
  }
}

// Reject a repeated prepared line before it reaches review/audio publication.
export function repeatedNightSpeech(text,previous) {
  const clean=value=>String(value||'').replace(/[^\p{L}\p{N}]/gu,'');
  const next=clean(text),pairs=value=>new Set(Array.from({length:Math.max(0,value.length-1)},(_,i)=>value.slice(i,i+2)));
  return previous.some(value=>{const old=clean(value);if(next.length<5||old.length<5)return false;if(next===old)return true;for(let i=0;i<=next.length-10;i++)if(old.includes(next.slice(i,i+10)))return true;if(Math.min(next.length,old.length)<12)return false;const a=pairs(next),b=pairs(old),shared=[...a].filter(x=>b.has(x)).length;return 2*shared/(a.size+b.size)>.65;});
}
