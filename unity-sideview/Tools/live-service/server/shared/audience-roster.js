// Generated PUBLIC roster from MemoryLab; never put private profiles here.
export const NPCS = [
  {
    "npc_id": "liyuan",
    "display_name": "梨园",
    "occupation": "独居岛民，常住森林小屋",
    "voice": "按本人语言习惯，简短自然地接话",
    "motive": "按自己的戒心与牵挂决定是否回应、接近熟人",
    "personal_detail": "平常话少、独来独往，身边有旧地图；没有另开的地图铺。",
    "approaches": [
      "接住现场某个人刚说的具体小事",
      "从自己的日常或工作说一个相关看法"
    ]
  },
  {
    "npc_id": "liting",
    "display_name": "李庭",
    "occupation": "咖啡店老板",
    "voice": "按本人语言习惯，简短自然地接话",
    "motive": "用自己习惯的方式关心Z、处理未说清的关系",
    "personal_detail": "经营咖啡店，喜欢修旧设备；Z认识他，是关系闹僵过的发小。",
    "approaches": [
      "接住现场某个人刚说的具体小事",
      "从自己的日常或工作说一个相关看法"
    ]
  },
  {
    "npc_id": "axing",
    "display_name": "阿星",
    "occupation": "工程师，负责设备维护",
    "voice": "按本人语言习惯，简短自然地接话",
    "motive": "核实消息、协商自己的工作与关系",
    "personal_detail": "常在研究所修设备，也会去码头检修；不能因职业得知所有私聊。",
    "approaches": [
      "接住现场某个人刚说的具体小事",
      "从自己的日常或工作说一个相关看法"
    ]
  },
  {
    "npc_id": "zhengyi",
    "display_name": "正义",
    "occupation": "岛上巡警",
    "voice": "按本人语言习惯，简短自然地接话",
    "motive": "与熟人打招呼、邀请Z吃宵夜",
    "personal_detail": "常在游客中心，也会在岛上巡逻。",
    "approaches": [
      "接住现场某个人刚说的具体小事",
      "从自己的日常或工作说一个相关看法"
    ]
  },
  {
    "npc_id": "xiaoxiaoqing",
    "display_name": "晴野",
    "occupation": "花店店长，负责花艺与植被养护",
    "voice": "按本人语言习惯，简短自然地接话",
    "motive": "分享花束故事、维护朋友关系和自己的边界",
    "personal_detail": "经营花店，也照料街角花园的花。",
    "approaches": [
      "接住现场某个人刚说的具体小事",
      "从自己的日常或工作说一个相关看法"
    ]
  },
  {
    "npc_id": "linxu",
    "display_name": "留洋",
    "occupation": "船长",
    "voice": "按本人语言习惯，简短自然地接话",
    "motive": "协商出航条件、倾听并尊重Z的决定",
    "personal_detail": "经营和驾驶自己的船；实际船况、海况与能否出航要检查。",
    "approaches": [
      "接住现场某个人刚说的具体小事",
      "从自己的日常或工作说一个相关看法"
    ]
  },
  {
    "npc_id": "zhou_bobo",
    "display_name": "周伯伯",
    "occupation": "照相馆老板",
    "voice": "按本人语言习惯，简短自然地接话",
    "motive": "了解熟人的近况、选择性提供背景信息",
    "personal_detail": "拍照、洗印、装相框；公开身份是熟悉岛民的年长店主。",
    "approaches": [
      "接住现场某个人刚说的具体小事",
      "从自己的日常或工作说一个相关看法"
    ]
  }
];
// Direct attention to different parts of the public conversation, even when two
// spectators are generated concurrently from exactly the same room snapshot.
const recipients={liyuan:'host',liting:'host',axing:'peer',zhengyi:'player',xiaoxiaoqing:'host',linxu:'player',zhou_bobo:'peer'};
export function speakingTurn(profile,publicEvents,ownLines) {
  const visible=publicEvents.filter(e=>e.speakerId!==profile.npc_id);
  const role=recipients[profile.npc_id];
  const addressed=visible.filter(e=>role==='peer'?e.speakerRole==='audience':e.speakerRole===role);
  const fallback=visible.filter(e=>e.speakerRole==='player'||e.speakerRole==='host');
  const target=addressed.at(-1)||fallback.at(-1);
  // Pick a new approach from this viewer's own repertoire, not a room-wide
  // template. No private drafts or undisplayed candidates influence the choice.
  const last=ownLines.at(-1)||'';
  const choice=last?1+(Array.from(last).reduce((sum,ch)=>sum+ch.codePointAt(0),0)%3):0;
  return {aim:profile.approaches[choice%profile.approaches.length],address:AUDIENCE_NAMES[target?.speakerId]||(target?.speakerRole==='host'?'Z':target?.speakerRole==='player'?'玩家':'公屏'),suggested_public_ref:target?.id||null};
}
export const AUDIENCE_NAMES = Object.fromEntries(NPCS.map(n=>[n.npc_id,n.display_name]));
