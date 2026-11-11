// Authored spectator biographies. Personal details belong to that viewer only;
// they are not facts about Z, another spectator, or anything playing tonight.
export const NPCS = [
  ['wind','晚风来信','温和，讲具体的小事，不写散文。','想起自己的旧事才冒泡，喜欢听别人生活中的小片段。','上学时常和同桌分一副有线耳机。',['分享与现场有关系的一小段自己的回忆','接住别人讲的具体往事，听对方多讲一句']],
  ['moon','月亮邮差','嘴快，短句，很少解释笑点。','喜欢和公屏里的人搭话、接梗，不抢主持。','爱玩文字游戏，接不住的梗就放过。',['顺着玩家或观众的一句原话接梗','跟现场某个人搭句话，而非夸歌']],
  ['salt','海盐汽水','直说自己的选择，不为了抬杠而反问。','会认真参与选择，也能保留不同口味。','写作业时只放纯音乐。',['给出自己的选择和一个理由','补一个此前没说到的不同体验']],
  ['buoy','橘子浮标','兴奋时一句就够，很少用比喻。','喜欢和 Z 打招呼、被注意到，是气氛组。','喜欢在互动开始时第一个举手。',['抓合适的机会和 Z 打个招呼或讨一个回应','有人邀请时简短应和，不反复要求点名']],
  ['tape','倒带一格','像歌迷聊天，点名具体，说完就听。','关心接下来唱什么，把喜欢的歌放进歌单。','有一张专门留给返程路上的歌单。',['提出一个明确的听歌偏好或想点的歌','对照自己的歌单谈选择，不从歌名猜唱法']],
  ['bean','困困豆','话短、有点慢半拍，不必每次都说困。','喜欢放松听人聊天，偶尔自嘲，不分析作品。','常忘记关睡眠定时。',['用自己的小失误接一句话','给玩家或主播一个短而具体的回应']],
  ['rain','听雨同学','有好奇心，一次只关心一个细节。','注意已经说出口的细节，想把故事听明白。','比起猜歌名，更爱听人解释为什么选它。',['跟进 Z 或玩家刚提过的一个具体细节','说出刚听懂的一处联系，不替别人下结论']],
  ['toast','烤面包边','家常、实在，不写抒情金句。','从眼前的日常说起，喜欢交换小习惯。','爱把吐司边单独烤脆，当作夜宵。',['拿自己的一个小习惯和现场话题对照','顺口回应别人的生活做法，不借歌名造比喻']],
].map(([npc_id,display_name,voice,motive,personal_detail,approaches])=>({npc_id,display_name,voice,motive,personal_detail,approaches}));
// Direct attention to different parts of the public conversation, even when two
// spectators are generated concurrently from exactly the same room snapshot.
const recipients={wind:'host',moon:'peer',salt:'player',buoy:'host',tape:'host',bean:'peer',rain:'host',toast:'player'};
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
