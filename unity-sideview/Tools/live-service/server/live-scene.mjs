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
  if(scene.mode==='arrival')return common+'\n这是第1000场直播，有一个来月亮岛一起生活旅游7天的名额。你自主介绍、接弹幕、选择开奖和邀请时机，播报与动效约30秒为软目标，玩家等待不算超时。'+
    '这是登岛前直播，岛民不在场，只有虚拟观众。可以输出可选virtualAudience=[{name,text}]，最多两条，用户名不能是任何岛民，每条不超过36字；只能回应已经公开的事。'+
    '额外工具仅限scene.tools：draw_guest args={}执行抽奖；拿到实际开奖结果后用offer_arrival args={title,description}自然邀请玩家登岛；玩家实际确认后，用depart_island args={}衔接岛上见面的告别。'+
    '调用工具时必须action=tool并携带tool={name,args}，例如{"action":"tool","tool":{"name":"draw_guest","args":{}}}，其余必填字段照常提供；action=speak或wait时不得携带tool。speech是工具执行前本轮自然说的话。继续直播时省略sessionDecision（没有keep意图）；登岛转场使用depart_island，不使用收播决定。不要提前公布未收到的结果；等玩家明确确认再告别转场。';
  return common+'\n首夜或日常直播由你自然展开。先接住新朋友和当日可公开经历，不照着清单汇报；想唱歌时自行选合适歌曲并调用已有唱歌工具，歌曲没有固定安排。首夜六位岛民可以回应，梨园不参加。';
}
export class LiveScene {
  constructor(session,input,saved) {
    this.session=session;this.context=validateLiveScene(saved?.context||input);
    this.phase=saved?.phase||'opening';this.pending=null;this.invitation=saved?.invitation||null;
  }
  allowed() { if(this.context?.mode!=='arrival'||this.pending)return [];
    return this.phase==='opening'?['draw_guest']:this.phase==='drawn'?['offer_arrival']:this.phase==='accepted'?['depart_island']:[];
  }
  binding() {return this.context?{...this.context,phase:this.phase,tools:this.allowed(),invitation:this.invitation}:null;}
  snapshot(){return {context:this.context,phase:this.phase,invitation:this.invitation};}
  execute(tool) {
    if(!this.allowed().includes(tool.name))return false;
    if(tool.name==='offer_arrival') {
      if(typeof tool.args?.title!=='string'||tool.args.title.length>60||!tool.args.title.trim()||
        typeof tool.args.description!=='string'||tool.args.description.length>240||!tool.args.description.trim())return false;
      this.invitation={title:tool.args.title,description:tool.args.description};
    }
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
    this.pending=null;
    if(payload.status!=='displayed')return;
    const tool=command.payload.tool;
    if(tool==='draw_guest') {this.phase='drawn';this.session.engine._public('story.fact','实际抽奖结果：'+this.context.playerName+'获得七天登岛体验邀请。',{speakerRole:'system'});}
    if(tool==='offer_arrival')this.phase='offered';
    if(tool==='depart_island')this.phase='departed';
    this.session.engine.nextWake=this.session.engine._clock;
  }
}
