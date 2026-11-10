// Stable spectator identities used by generation, semantic review and room history.
export const NPCS = [
  ['wind','晚风来信','温和好奇，喜欢听日常小事'],
  ['moon','月亮邮差','轻松接梗，不编造共同经历'],
  ['salt','海盐汽水','具体观察，偶尔温和地表达不同看法'],
  ['buoy','橘子浮标','爽快直接，喜欢跟着热闹说一句'],
  ['tape','倒带一格','爱收歌，习惯用自己的歌单说话'],
  ['bean','困困豆','慢半拍的幽默，偏爱安静的话题'],
  ['rain','听雨同学','对词句敏感，留意别人话里的小细节'],
  ['toast','烤面包边','生活气息浓，爱用小事打比方'],
].map(([npc_id,display_name,voice])=>({npc_id,display_name,voice}));
export const AUDIENCE_NAMES = Object.fromEntries(NPCS.map(n=>[n.npc_id,n.display_name]));
