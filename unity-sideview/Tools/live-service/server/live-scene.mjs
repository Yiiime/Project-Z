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
    'scene.warmup是已实际播放与显示的进度；ready=false时保持闲聊，每轮speech约35–70字，每轮必须提供3–4条virtualAudience=[{name,text}]，让不同观众聊刚听见的内容，每条不超过36字。用户名不能是岛民，不能提前知道还没公布的活动。'+
    '当scene.warmup.ready=true且scene.phase=opening时，才像突然想起来一样自然提起：原来今天已经是第1000场直播，想送一位朋友来月亮岛一起生活旅游7天。说完惊喜缘由后自主调用draw_guest打开礼盒，由玩家亲手点击拆开；不要反复预告，也不要追加举手、扣字等门槛。'+
    'scene.tools仅含当前可用工具。draw_guest args={}只展示待点击礼盒，不代表已经开奖；收到实际结果后才调用offer_arrival args={title,description}，标题简短，描述包含玩家名字且不超过100字。玩家确认后才用depart_island args={}告别。没有工具可用时继续自然闲聊，不虚构开奖或接受事实。'+
    '调用工具时必须action=tool并携带tool={name,args}，其余必填字段照常提供；action=speak或wait时不得携带tool。speech是执行前本轮自然台词。arrival不使用收播决定，省略sessionDecision。';
  return common+'\n首夜或日常直播由你自然展开。先接住新朋友和当日可公开经历，不照着清单汇报；想唱歌时自行选合适歌曲并调用已有唱歌工具，歌曲没有固定安排。首夜六位岛民可以回应，梨园不参加。';
}
export class LiveScene {
  constructor(session,input,saved) {
    this.session=session;this.context=validateLiveScene(saved?.context||input);
    this.phase=saved?.phase||'opening';this.pending=null;this.invitation=saved?.invitation||null;
    this.warmup={speeches:0,messages:0,startedAt:session.engine._clock||0,...saved?.warmup};
    this.completedSpeeches=new Set(saved?.completedSpeeches||[]);
  }
  warmupStatus(){const elapsed=Math.max(0,(this.session.engine._clock||0)-this.warmup.startedAt);return {...this.warmup,elapsedMs:elapsed,ready:this.warmup.speeches>=3&&this.warmup.messages>=4&&elapsed>=45000};}
  hostCompleted(id){if(this.context?.mode!=='arrival'||this.phase!=='opening'||!id||this.completedSpeeches.has(String(id)))return;this.completedSpeeches.add(String(id));this.warmup.speeches++;}
  audienceDisplayed(){if(this.context?.mode==='arrival'&&this.phase==='opening')this.warmup.messages++;}
  allowed() { if(this.context?.mode!=='arrival'||this.pending)return [];
    return this.phase==='opening'?(this.warmupStatus().ready?['draw_guest']:[]):this.phase==='drawn'?['offer_arrival']:this.phase==='accepted'?['depart_island']:[];
  }
  binding() {return this.context?{...this.context,phase:this.phase,tools:this.allowed(),invitation:this.invitation,warmup:this.warmupStatus()}:null;}
  snapshot(){return {context:this.context,phase:this.phase,invitation:this.invitation,warmup:this.warmup,completedSpeeches:[...this.completedSpeeches]};}
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
  accept() {
    if(this.phase==='accepted')return true;
    if(this.phase!=='offered')return false;
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
