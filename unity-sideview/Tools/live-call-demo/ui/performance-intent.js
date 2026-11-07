// Local, conservative preview rules; this is not an AI emotion classifier.
const DEFAULT_GESTURES = { neutral:'none', happy:'handWave', warm:'present', serious:'openArms', thinking:'chin', shy:'coverMouth', sad:'nod', surprised:'startle' };
export function resolvePreviewIntent(text, { mood = 'auto', gesture = 'auto' } = {}) {
  const source = String(text || '').trim();
  const clean = source.replace(/(?:并不|并没有|不|没有)(?:那么|太|很|再|觉得|感到)?(?:难过|伤心|开心|高兴|害羞|惊讶)/g, '');
  let inferred='neutral', inferredGesture=null, reason='没有匹配到明确情绪，先使用自然表达';
  for (const [pattern, value, explanation, action] of [
    [/不好意思|害羞|脸红|夸我|别夸|羞/, 'shy', '表达害羞或被夸奖'],
    [/难过|伤心|失落|沮丧|遗憾|想哭/, 'sad', '表达失落'],
    [/惊喜|(?:那边|这里|那里).*(?:还有|居然有|竟然有).*(?:路|通道)|(?:发现|找到).*(?:新路|小路)|(?:这条路).*(?:竟然|居然|以前)/, 'happy', '突然发现新线索', 'discover'],
    [/居然|竟然|没想到|真的假的|不敢相信|诶[？?]|意外/, 'surprised', '表达意外'],
    [/经验|建议|诀窍|我的做法|我试过|分享/, 'warm', '分享自己的经验', 'openArms'],
    [/先别.*结论|认真|具体|核实/, 'serious', '认真解释或核实'],
    [/我在听|歇一会|休息|慢慢来|别急|陪你|累了|没关系|安慰/, 'warm', '安慰或陪伴'],
    [/让我想想|哪一次|为什么|不太明白|想不通|疑惑|怎么回事/, 'thinking', '思考或澄清'],
    [/你来啦|你好|欢迎|太好了|好开心|高兴|好地方|一起去|好消息/, 'happy', '招呼或分享喜悦'],
  ]) if (pattern.test(clean)) { inferred=value;inferredGesture=action;reason=explanation;break; }
  const chosen=Object.hasOwn(DEFAULT_GESTURES,mood)?mood:inferred;
  return { mood:chosen, gesture:gesture==='auto'?((mood==='auto'&&inferredGesture)||DEFAULT_GESTURES[chosen]):gesture, reason:mood==='auto'?reason:'使用你指定的情绪' };
}
const EXAMPLE_LINES = {
  serious:['分享一个我的经验：先把最难的那一步拆小，再慢慢试。','我的做法是先看清问题，再决定往哪儿走。'],
  shy:['你这么夸我……我都有点不好意思了。','别一直看着我笑呀，我会害羞的。'],
  happy:['你来啦！走，我带你看个刚发现的好地方。','太好了！这次我们真的找到那条小路了。'],
  warm:['今天累了就先歇一会儿。想说的话，我在听。','别急，我们慢慢来，我陪你一起想。'],
  thinking:['等一下，你说的是哪一次？让我想想。','这件事我有点想不通，让我再看一眼。'],
  sad:['这件事没做好，我确实有点难过。让我缓一会儿。','没能和你一起看到日出，还是有点遗憾。'],
  surprised:['诶？这条路竟然通到了海边！','真的假的？我一直没发现门后还有一个房间。'],
  neutral:['今晚海边风不大，我们坐这儿聊聊吧。','我把地图放在桌上了，等会儿再一起看看。'],
};
export function generatePreviewLine(mood='auto', random=Math.random) {
  const pick=items=>items[Math.min(items.length-1,Math.max(0,Math.floor(random()*items.length)))];
  const selected=Object.hasOwn(EXAMPLE_LINES,mood)?mood:pick(Object.keys(EXAMPLE_LINES));
  return pick(EXAMPLE_LINES[selected]);
}
