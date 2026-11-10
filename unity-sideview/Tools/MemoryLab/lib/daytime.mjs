// Private dialogue stays here. Only a song name or a fixed public subject can leave this module.
const topics = [
  [/流星雨/u, '流星雨'], [/日落/u, '日落'], [/晚霞/u, '晚霞'], [/灯塔/u, '岛上的灯塔'],
  [/潮汐|涨潮|退潮/u, '海边的潮汐'], [/赶海/u, '赶海'], [/贝壳/u, '海边的贝壳'],
  [/街角花园/u, '街角花园'], [/咖啡豆/u, '咖啡豆'], [/手冲咖啡/u, '手冲咖啡'], [/拉花/u, '咖啡拉花'],
  [/桂花拿铁/u, '桂花拿铁'], [/天文望远镜/u, '天文望远镜'],
  [/沙滩排球/u, '沙滩排球'], [/观鸟/u, '岛上观鸟'], [/星座/u, '星座'], [/钓鱼/u, '钓鱼'],
  [/音乐节/u, '音乐节'], [/露天电影/u, '露天电影'], [/花市/u, '花市'], [/吉他/u, '吉他'], [/古树广场/u, '古树广场'], [/摄影|拍照/u, '拍照'], [/旧书屋/u, '岛上的旧书屋'],
];
const privateOnly = /保密|秘密|私密|私事|别外传|不要外传|只.{0,4}(?:咱|我们|你我|你知)|不方便.{0,4}(?:说|公开)|(?:别|不要|不能|不许|不想|不希望|不可以|不准|不用).{0,12}(?:提|公开|直播|告诉|说|讲|让.*知道)/u;
const songRequest = /(?:我想听|想听|(?:请|能不能|可不可以|可以|帮我|给我|记得|能|想让你).{0,10}(?:学|唱|练)|(?:学|唱|练)(?:一?下|一?首|一?遍|给我)|点(?:一首|首|歌)|(?:直播|晚上).{0,8}(?:唱|学)|(?:学|唱|练)《|^(?:请你|你来|你能|你可以|麻烦你)?(?:学|练|唱)(?!过|完|好|会|得))/u;
const decline = /(?:不|没|别|无法|不好).{0,5}(?:唱|学|练|答应|保证|确定)|不一定|再说吧|考虑一下|要是|如果|看情况|我想听你唱/u;
const accept = /(?:^|[，。！、\s])(?:好[呀啊的嘞哇]?|行[啊呀吧]?|可以[啊呀]?|没问题|收到)[，。！、\s]|(?:我|晚上|直播|回去|这就|先|给你|帮你|抽空|等我).{0,12}(?:唱给你|唱这首|给你唱|练[练习一首下]|学[学一会下]|去学|去练|准备这首)|(?:我来|我会|我去|我想|我先|我打算|我试着)(?:学|练|唱)|(?:唱|练|学)给你|安排上|包在我身上/u;
function titleOf(text) {
  const name=text.match(/《([^《》]{1,48})》/u)?.[1] ??
    text.match(/(?:想听(?:你唱)?|帮我(?:学|唱|练)|(?:学|唱|练)(?:一?下|一首)|^(?:学|唱|练)|点一首)\s*([\p{L}\p{N}· ]{2,32})(?=[，。！？!?；;]|$)/u)?.[1]?.replace(/(?:这首歌|这首|好吗|好不好|吧|可以吗|吗)$/u,'');
  return name && /^[\p{L}\p{N} ·’'＆&-]{1,48}$/u.test(name) && !/这首|那首|什么|你的|我的|我们的|秘密|隐私|暗号|地址|手机号|失眠|\d{6}/u.test(name) ? name.trim() : null;
}
export function dayConversation(events, day) {
  const rows=events.filter(e=>Math.floor(e.hour/24)+1===day && e.channel==='private_player_chat' &&
    e.visible?.includes('player') && e.visible.includes('z') &&
    ((e.speaker==='player' && e.recipient==='z') || (e.speaker==='z' && e.recipient==='player'))).sort((a,b)=>a.seq-b.seq);
  const clauses=text=>text.split(/[，。！？；,;!?]/u);
  const withheld=clause=>privateOnly.test(clause) && !/^(?:原因|理由|我(?:的)?(?:私事|病情|地址)|我失眠的事).{0,5}(?:别|不用|不要)/u.test(clause);
  const blocked=new Set(rows.filter(e=>clauses(e.text).some(withheld)).map(e=>e.conversationId));
  const blockedTitles=new Set(), blockedTopics=new Set(), subjects=new Set(), highlights=new Set(), songs=new Map(), previous=new Map(), lastTitle=new Map();
  // A later privacy request also retracts the topic from an earlier conversation that day.
  let lastNamed;
  for(const e of rows){
    const named=titleOf(e.text);
    if(named)lastNamed=named;
    if(!blocked.has(e.conversationId))continue;
    if(clauses(e.text).some(withheld) && /这首|那首|刚才的歌|那个歌/u.test(e.text) && lastNamed)blockedTitles.add(lastNamed);

    if(named)blockedTitles.add(named);
    for(const [match,subject] of topics)if(match.test(e.text))blockedTopics.add(subject);
  }
  for(const e of rows){
    if(blocked.has(e.conversationId))continue;
    if(e.speaker==='player'){
      previous.set(e.conversationId,e);
      for(const clause of clauses(e.text).filter(c=>!privateOnly.test(c)))
        for(const [match,subject] of topics)if(match.test(clause)&&!blockedTopics.has(subject))subjects.add(subject);
      continue;
    }
    const player=previous.get(e.conversationId);
    if(!player)continue;
    previous.delete(e.conversationId);
    // Publish the subject label only when Z herself wants to share this particular topic.
    for(const sentence of e.text.split(/[。！？!?]/u)){
      if(!/(?:我想|我想要|我也想|我打算|我特别想|我好想).{0,10}(?:在直播.{0,4}(?:聊|讲|说)|(?:讲|聊|说)给(?:直播间|大家)听)/u.test(sentence) ||
         /不想|不能|不该|不要|不打算|要是|如果|你想|你说/u.test(sentence))continue;
      const named=topics.filter(([pattern])=>pattern.test(sentence));
      if(named.length===1 && named[0][0].test(player.text) && subjects.has(named[0][1]) && !blockedTopics.has(named[0][1]))highlights.add(named[0][1]);
    }
    // Without book-title brackets, require the two speakers to agree on the name.
    const named=titleOf(player.text), echoed=titleOf(e.text);
    const title=(named && (player.text.includes('《'+named+'》') || e.text.includes(named)) ? named : null) ||
      (echoed && player.text.includes(echoed) ? echoed : null) ||
      (/这首|那首/u.test(player.text) ? lastTitle.get(e.conversationId) : null);
    if(title)lastTitle.set(e.conversationId,title);
    if(!title || blockedTitles.has(title) || !/歌|唱|听|点一首|学/.test(player.text))continue;
    if(!songs.has(title))songs.set(title,{title});
    const decision=e.daySongSource===player.id ? e.daySongReply : null;
    const agreed=decision ? decision==='accept' && !/不能答应|不打算(?:学|唱)|不接这首/u.test(e.text)
      : !decline.test(e.text) && (accept.test(e.text+'。') || /(?:我先|等我).{0,9}(?:调子|歌词|旋律).{0,7}(?:顺|练|熟悉)/u.test(e.text));
    if(!songRequest.test(player.text)||/(?:别|不要|不想|不必|不用)(?:再)?(?:听|唱|学|练)/u.test(player.text)||!agreed)continue;
    if(!songs.get(title).sourceId || (!songs.get(title).dispatch && e.daySongBridge))songs.set(title,{title,sourceId:player.id,dispatch:e.daySongBridge===true});
  }
  return {subjects:[...subjects],highlights:[...highlights],songs:[...songs.values()]};
}
