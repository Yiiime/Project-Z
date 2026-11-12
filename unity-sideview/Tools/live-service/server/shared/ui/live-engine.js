import {utteranceMetadata} from '../utterance.js';
/**
 * Interactive local storyline with an optional asynchronous text generator.
 *
 * Usage:
 *   import { LiveEngine } from './live-engine.js';
 *   const live = new LiveEngine();
 *   live.addEventListener('state', ({detail}) => render(detail));
 *   live.addEventListener('speak', ({detail}) => animateZ(detail));
 *   live.addEventListener('effect', ({detail}) => celebrate(detail));
 *   live.start(); // Call destroy() when the view unmounts.
 *
 * Render message and speech text with textContent. All event detail is plain data.
 */


const STORAGE_KEY = 'z-moon-island-live-v1';
const SELF = '小岛来客';
const LABELS = {
  intro: '初次见面 · 欢迎来到月亮小岛',
  fortune: '今日运势 · 星光正在抵达',
  draw: '弹幕抽选 · 今晚的三位幸运观众',
  reading: '专属占卜 · 认真聆听你的问题',
  free: '月光闲聊 · 留下来聊聊天吧',
};

export const CARDS = [
  { name: 'THE STAR', title: '星星', symbol: '✦', color: '#9b8ac8',
    meaning: '星星看的是恢复和希望，别用眼前这点挫折给自己下结论。',
    advice: '今天给自己一个能完成的小目标，做完就认真夸奖自己。' },
  { name: 'THE SUN', title: '太阳', symbol: '☀', color: '#dfb764',
    meaning: '太阳看的是坦诚，有话说清楚，比让彼此猜来猜去轻松。',
    advice: '主动发起一次轻松的沟通，用具体的问题代替猜测。' },
  { name: 'THE MAGICIAN', title: '魔术师', symbol: '∞', color: '#8e9b84',
    meaning: '魔术师提醒你先用手里已有的本事，不必等万事齐备。',
    advice: '列出手边的三项资源，选一件小事，在今天迈出第一步。' },
  { name: 'TEMPERANCE', title: '节制', symbol: '◈', color: '#83a9a5',
    meaning: '节制看的是节奏，慢一点不等于停下，先把最要紧的事留出来。',
    advice: '给重要的事留出空间，也给自己一个按时休息的约定。' },
  { name: 'THE MOON', title: '月亮', symbol: '☾', color: '#a597ba',
    meaning: '月亮提醒你先别急着信猜测，看看还有哪个细节没弄清。',
    advice: '把已知的事实和担心分别写下来，再找一个能够核实的小问题。' },
  { name: 'THE WORLD', title: '世界', symbol: '◎', color: '#a7ac83',
    meaning: '世界看的是一个阶段的收尾，先看清自己带走了什么，再往前走。',
    advice: '整理最近完成的一件事，记录收获，再决定下一步想去哪里。' },
];

const VIEWERS = [
  { name: '晚风来信', question: '最近工作有点迷茫，下一步该怎么走？', color: '#8c7bb4' },
  { name: '一颗小橘子', question: '想知道怎么更自然地向喜欢的人表达心意。', color: '#c19b6e' },
  { name: '奶油云朵', question: '学业压力有点大，怎样找回自己的学习节奏？', color: '#9b85ae' },
  { name: '月亮邮差', question: '准备开始一个新计划，想听听行动上的建议。', color: '#799b90' },
  { name: '小熊软糖', question: '最近总是想太多，如何更好地照顾自己的心情？', color: '#bb8e9b' },
  { name: '不熬夜的猫', question: '工作和生活之间，怎样才能找到适合自己的平衡？', color: '#8f94b0' },
  { name: '海盐汽水', question: '和朋友有一点误会，想知道该怎样开口沟通。', color: '#7ca6a8' },
  { name: '星星收藏家', question: '在学一个新技能，但总怕做不好，怎样坚持下去？', color: '#b1a172' },
];

const CHAT = {
  intro: ['今晚第一排，我来啦！', 'Z 的月亮发夹好可爱 🌙', '晚安！今天也来收集一点好运', '这里的灯光好温柔', '带着我的小问题来听直播了'],
  fortune: ['今天也需要一点星星的勇气 ✨', '把小目标写下来了！', '先行动，再慢慢变好', '喜欢 Z 认真讲牌的样子', '今天的幸运色是什么呀？'],
  draw: ['我想问问工作方向，报名！', '月亮听见我的问题了吗 🌙', '希望抽到我，最近想聊聊感情', '蹲一份学习建议', '大家的问题也很有共鸣'],
  reading: ['认真听讲中，已经开始做笔记了', '这个建议好具体，我也试试看', '给这位朋友一点勇气 ✨', '原来牌也可以帮我们整理想法', '喜欢这种温柔又认真的解读'],
  free: ['还想听岛上的事', '谢谢陪伴，今晚很开心', 'Z，星星这张牌是什么意思呀？', '今天也要好好休息 🌙', '下次还要来小屋坐坐！'],
};

function randomBetween(min, max) {
  return Math.floor(Math.random() * (max - min + 1)) + min;
}
function hash(text) {
  return Array.from(text).reduce((value, char) => ((value * 31) + char.codePointAt(0)) >>> 0, 7);
}
function cleanText(value, limit = 160) {
  return typeof value === 'string' ? Array.from(value.trim().replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, '').replace(/\s+/g, ' ')).slice(0, limit).join('') : '';
}
function wireText(value, maxUnits) {
  const text = cleanText(value, maxUnits);
  // The server validates UTF-16 lengths; keep emoji intact at that boundary.
  return text.slice(0, maxUnits).replace(/[\uD800-\uDBFF]$/, '');
}

function completeReplyText(value, limit = 360) {
  const text = cleanText(value, 1200);
  // Production generation already enforces complete sentences. Reject malformed
  // custom-provider overflows instead of guessing a boundary inside a quotation.
  return text.length <= limit ? text : '';
}

function quote(text) {
  const chars = Array.from(text);
  return `「${chars.slice(0, 72).join('')}${chars.length > 72 ? '…' : ''}」`;
}
function topicOf(text) {
  if (/感情|恋爱|爱情|喜欢的人|表白|复合|对象|伴侣|暗恋|分手|脱单/.test(text)) return 'love';
  if (/工作|事业|职场|面试|跳槽|同事|老板|领导|项目|升职|创业/.test(text)) return 'work';
  if (/学业|学习|考试|考研|高考|论文|学校|成绩|作业|技能/.test(text)) return 'study';
  if (/情绪|心情|焦虑|难过|压力|担心|失眠|睡不着|想太多|内耗|疲惫|孤独|开心/.test(text)) return 'emotion';
  if (/计划|目标|开始|决定|搬家|旅行|存钱|方向|未来|生活|选择|坚持/.test(text)) return 'plan';
  if (/故事|小岛|明日岛|月亮岛|岛上|住在哪|哪里住/.test(text)) return 'story';
  if (/牌义|牌意|塔罗|正位|逆位|星星牌|太阳牌|月亮牌|这张牌|什么牌|牌.*意思/.test(text)) return 'card';
  if (/你好|嗨|哈[喽啰]|早上好|下午好|晚上好|晚安|[Hh]ello|^[Hh]i[!！。， ]*$|初次见面/.test(text)) return 'greeting';
  return 'general';
}
// Every card changes the interpretation of the situation, the obstacle and the
// proposed action. Keyword details then ground that reading in the actual question.
const PERSONAL_READINGS = {
  'THE STAR': {
    work: ['我会先看你已经积累的能力。方向不清时，过去做成的事能提供线索。', '先别只看别人走到哪儿，也看看自己已经会了什么。', '挑一件做成的事，写清你负责什么、解决了什么。'],
    love: ['这里我会看，两个人能不能安心表达。', '一次冷淡，还不足以判断整段关系。', '找个轻松的时候，说一个你希望被理解的需要，再听听对方怎么想。'],
    study: ['我更留意学习中那些小进步，它们容易被一次分数盖过去。', '先别拿一次成绩给全部努力下结论。', '找一道以前不会、现在能讲清的题，把方法记下来。'],
    emotion: ['这次我会先看怎么恢复精力。现在可以先缓一缓，不用逼自己马上开心。', '休息这件事，不用先向谁证明值得。', '今晚留十分钟做件让你放松的事，也可以找信任的人聊聊。'],
    plan: ['它适合看长期想做的事，靠的是能坚持的节奏。', '目标太远时，第一步会显得小，但它依然有用。', '写下你为什么想做，再定一件这周就能完成的事。'],
  },
  'THE SUN': {
    work: ['我会先看，你的成果有没有被说清楚。', '别人未必能自动知道你做了多少。', '带上一个具体成果，约十分钟聊聊你想争取什么。'],
    love: ['这次我会更鼓励你坦诚一点。', '一直试探，双方都很难知道对方真正的意思。', '约一件你们都喜欢的事，表达感受，也留点空间听对方回答。'],
    study: ['我会先看，学到的知识能不能讲明白。', '看懂答案和自己会做，有时还差一步。', '合上资料，花三分钟讲一个知识点，再回去检查漏了什么。'],
    emotion: ['这张牌会提醒你，找回一点和外界的连接。', '如果一直闷着，很多感受就没有机会说出来。', '白天出去走一会儿，或找熟悉的人聊十分钟，不用强求自己振作。'],
    plan: ['这里适合把目标说清楚，做到哪一步算完成。', '同时想做太多件，反而容易顾不过来。', '先挑一项，写下这周结束时你能检查的成果。'],
  },
  'THE MAGICIAN': {
    work: ['我会看你能调动什么：技能、作品，还有能请教的人。', '有时卡住，是一直等条件全部齐了才动。', '列三项手边资源，用其中两项做个小样，或者联系一个了解情况的人。'],
    love: ['放到你的问题里，我会看心意有没有变成实际行动。', '话术想得太多，也容易忽略对方真正说了什么。', '发出一个具体邀请，然后留意对方是不是也愿意主动靠近。'],
    study: ['它更偏向亲手练习。资料可以先少一点，练起来更有用。', '收藏课程这一步，可不能替你把题做完。', '只用一份资料，完成一道题或一个小作品，再看哪里需要补。'],
    emotion: ['这里我会把注意力放回你能掌握的部分。', '反复琢磨控制不了的事，确实会很累。', '把担心分成能行动和需等待两栏，先做能行动栏里最小的一件。'],
    plan: ['我会鼓励你先试一版，现有工具也许已经够用了。', '别让买工具、做准备，占掉了真正开始的时间。', '给自己两天，用手边资源做个最小版本，找个人给反馈。'],
  },
  'TEMPERANCE': {
    work: ['我会先看，任务和精力怎么分。', '每件事都接下来，就很难留住自己的时间。', '列出这周三项任务的顺序，和相关的人确认哪件可以晚一点。'],
    love: ['这张牌让我留意两个人的节奏，可能有些地方还没对上。', '节奏不一样，先别急着理解成不在乎。', '各说一个需要和一个可以商量的地方，再约好什么时候聊。'],
    study: ['重点是学和休息怎么安排。', '熬夜补进度，很容易让第二天更难集中。', '试两段二十五分钟练习，中间离开屏幕五分钟，到时间就停一下。'],
    emotion: ['这次我会建议你，给自己留点余地。', '不用一直硬撑，也不用一下把所有事都放下。', '先吃好一顿饭、休息一下，再看看今天还有多少精力。'],
    plan: ['这个计划要留点余量，热情也得放进实际日程里看。', '每天排满，一点变化就会把整张表打乱。', '先把日程排到七成，留点时间应付变化，两天后再调整。'],
  },
  'THE MOON': {
    work: ['我会先看信息够不够，尤其是实际的工作条件。', '传闻和担心，可以先和已经确认的事分开。', '找出最影响判断的一个未知条件，问清楚，再比较。'],
    love: ['我会留意，你不确定的那些地方。', '聊天里的一个细节，可能还不足以说明对方的心意。', '把实际发生的事和自己的担心分开，找机会直接问一个温和的问题。'],
    study: ['这里我会先找没弄明白的地方。', '越避开不会的题，那个担心就越模糊。', '挑一个最没把握的知识点，不看资料测一次，看看具体卡在哪儿。'],
    emotion: ['暂时说不清心情，也没关系，我们可以先看它从哪儿来。', '最糟的设想，还不是已经发生的事。', '写三句：发生了什么，我有什么感受，现在需要什么。今天先别急着下结论。'],
    plan: ['我会留意，计划里还没核实的条件和成本。', '太兴奋或太害怕的时候，都容易漏看一些信息。', '列三个还没确认的假设，先验证最关键的那个，也给调整留个余地。'],
  },
  'THE WORLD': {
    work: ['我会看，这一阶段已经做成了什么。', '如果一直往前赶，过去的经验可能还没来得及整理。', '做一页项目复盘，写清成果、方法，还有下一步想承担的职责。'],
    love: ['我会留意关系之外，你自己的生活是不是也完整。', '一段关系很重要，也不用让它承担你全部的期待。', '想清一条你珍惜的边界，再和对方聊聊你们想怎样相处。'],
    study: ['它适合把已经学过的东西串起来。', '新章节一直往下看，前面的联系也别落下。', '画一张阶段知识图，找出三个没连上的地方，再安排复习。'],
    emotion: ['我会回头看看，这段经历是不是已经可以慢慢收尾了。', '有些事情未必能等到一个完全满意的结局。', '写下这段经历让你学会的三件事，然后给今天留一个结束的时间。'],
    plan: ['我会先看旧计划有没有收好尾，再谈下一步。', '旧任务还挂着，就再背一个新目标，可能会更累。', '先收掉一件没完成的事，记下经验，再选下一阶段最重要的一件。'],
  },
};

function questionDetail(question, topic) {
  if (topic === 'work') {
    if (/跳槽|辞职|换工作/.test(question)) return '跳槽的话，岗位做什么、团队怎么样、薪酬怎么算，先问清。再和现在的工作比较，不急着拍板。';
    if (/面试/.test(question)) return '面试前，准备一个你亲手解决问题的例子，再想两个你真正在意的岗位问题。你也要看看对方合不合适嘛。';
    if (/领导|老板/.test(question)) return '和领导谈时，把任务、期限和需要的支持写具体，一项项确认，会比猜意思省力。';
    if (/创业/.test(question)) return '创业先找三位可能的用户聊聊，再算一次小试验要花多少时间和钱。';
  }
  if (topic === 'love') {
    if (/复合|前任/.test(question)) return '复合之前，先看看当初的问题有没有实际改变，也问清双方是不是都愿意再试一次。';
    if (/表白|表达心意|暗恋/.test(question)) return '表白可以先说「我很喜欢和你相处，想约你单独见面」。意思清楚，也给对方留回答的空间。';
    if (/分手/.test(question)) return '分手后先照顾好作息，找信任的人聊聊。等情绪缓一些，再谈重要的决定。';
    if (/误会|吵架/.test(question)) return '这次误会，试试从「当时我的感受是……」说起，再听听对方看见的情况。';
  }
  if (topic === 'study') {
    if (/考试|考研|高考/.test(question)) return '准备考试，可以先把错题分成不会、粗心和时间不够三类。最多的是哪类，就先处理哪类，再做一次限时练习。';
    if (/论文/.test(question)) return '论文先写一个研究问题，配三条支撑材料，带着这个版本找导师确认范围。';
    if (/技能/.test(question)) return '学新技能，选一个七天能完成的小作品。先留住第一版，下次只改一个地方。';
  }
  if (topic === 'emotion') {
    if (/失眠|睡不着|熬夜/.test(question)) return '今晚试着早点放下屏幕，把明天的事写下来再收起。如果睡眠一直影响生活，也可以找专业的人帮忙。';
    if (/焦虑|想太多|内耗|担心/.test(question)) return '担心又绕回来时，问问自己有没有新事实。暂时没有，就先回到眼下能做的一件小事。';
  }
  if (topic === 'plan') {
    if (/搬家/.test(question)) return '搬家先看通勤、租金和合同，再亲自走一趟。最不能妥协的条件，提前写下来。';
    if (/旅行/.test(question)) return '旅行先定预算和可用时间，行程留点调整的余地。';
    if (/存钱|预算/.test(question)) return '存钱先记一周实际支出，再定个能坚持的小额度，日常必需的钱也要留好。';
  }
  return '';
}

function personalReading(question, card) {
  const detected = topicOf(question);
  const topic = ['work', 'love', 'study', 'emotion', 'plan'].includes(detected) ? detected : 'plan';
  const reading = PERSONAL_READINGS[card.name] || PERSONAL_READINGS['THE STAR'];
  const [context, obstacle, action] = reading[topic];
  return { context, obstacle, action, detail: questionDetail(question, topic) };
}

function personalAdvice(question, card) {
  const reading = personalReading(question, card);
  return `${reading.context}${reading.obstacle}${reading.action}${reading.detail}`;
}

function fortuneVoteFromText(text) {
  const match = text.trim().match(/^(?:(?:我)?(?:选|选择|投|投票给|选牌)\s*)?([ABCabc123])(?:\s*(?:号|牌|号牌))?[！!。～~]?$/);
  return match ? ('ABC'.includes(match[1].toUpperCase()) ? match[1].toUpperCase().charCodeAt(0) - 65 : Number(match[1]) - 1) : null;
}

function spokenShare(percent) {
  if (percent <= 0) return '不到一成';
  if (percent < 10) return '不到一成';
  if (percent >= 95) return '几乎全部';
  if (percent >= 85) return '九成左右';
  if (percent >= 75) return '八成左右';
  if (percent >= 65) return '七成左右';
  if (percent >= 55) return '六成左右';
  if (percent >= 45) return '一半左右';
  if (percent >= 35) return '四成左右';
  if (percent >= 25) return '三成左右';
  if (percent >= 15) return '两成左右';
  return '一成左右';
}

function shuffledCards(seed) {
  const cards = CARDS.map(card => ({ ...card }));
  let value = hash(seed) || 1;
  const next = () => { value ^= value << 13; value ^= value >>> 17; value ^= value << 5; return value >>> 0; };
  for (let index = cards.length - 1; index > 0; index--) {
    const other = next() % (index + 1);
    [cards[index], cards[other]] = [cards[other], cards[index]];
  }
  return cards.slice(0, 3);
}

export class LiveEngine extends EventTarget {
  constructor({ audienceReadings = false } = {}) {
    super();
    this._audienceReadings = audienceReadings === true;
    this._timer = null;
    this._started = false;
    this._destroyed = false;
    this._clock = 0;
    this._lastTick = 0;
    this._messageId = 0;
    this._order = 0;
    this._phase = 0;
    this._queue = [];
    this._continuations = [];
    this._pendingAction = null;
    this._draining = false;
    this._chatCandidates = [];
    this._selectedPlayer = null;
    this._recentPlayerMessages = new Map();
    this._replyBurst = 0;
    this._active = null;
    this._jobs = [];
    this._nextChatAt = 1400;
    this._nextViewerAt = 2800;
    this._nextDrawAt = Infinity;
    this._nextCardAt = Infinity;
    this._nextReadingAt = Infinity;
    this._lastMessageAt = -Infinity;
    this._lastLikeEffectAt = -Infinity;
    this._lastCardIndex = -1;
    this._seedCount = 0;
    this._speechDriven = false;
    this._speechId = 0;
    this._fortuneRound = 0;
    this._fortuneVotes = new Map();
    this._fortuneVotingEndsAt = Infinity;
    this._fortuneNextVoteAt = Infinity;
    this._textGenerator = null;
    this._textEpoch = 0;
    this._textSequence = 0;
    this._textPending = new Map();
    this._latestChatId = null;
    this._latestReadingId = null;
    this._history = [];
    this._storyEvents = new Set();
    this._lotteryOpenedAt = null;
    const saved = this._readSaved();
    this._state = this._initialState(saved);
  }

  _initialState(saved = {}) {
    return {
      viewers: 24186, likes: 2380, followed: Boolean(saved.followed),
      stage: 'intro', stageLabel: LABELS.intro, round: 0, selectedViewers: [],
      features: { audienceReadings: this._audienceReadings },
      replyStatus: 'idle', replyTo: null, replySource: null, replyError: null,
      queuedAction: null,
      textMode: this._textGenerator ? 'provider' : 'local', textStatus: this._textGenerator ? 'ready' : 'local',
      lottery: null, claimedRewards: [], islandInvited: false,
      currentCard: null, selectedCardIndex: null, currentViewer: null, currentSpeaker: 'Z', currentSpeech: '', currentSpeechId: null, currentSource: 'local',
      mood: 'happy', gesture: 'wave', paused: false, drawJoined: false,
      drawQuestion: '', drawParticipants: 38, waitingForCard: false,
      cardChoices: ['月光', '星海', '晚风'], messages: [],
    };
  }

  _readSaved() {
    try {
      const data = JSON.parse(globalThis.localStorage?.getItem(STORAGE_KEY) || '{}');
      return data && typeof data === 'object' ? data : {};
    } catch { return {}; }
  }

  _persist() {
    try {
      globalThis.localStorage?.setItem(STORAGE_KEY, JSON.stringify({
        followed: this._state.followed,
      }));
    } catch { /* Private browsing and full storage should never interrupt the demo. */ }
  }

  getState() {
    return JSON.parse(JSON.stringify(this._state));
  }

  _event(type, detail) {
    if (!this._destroyed) this.dispatchEvent(new CustomEvent(type, { detail }));
  }

  _emitState() { this._event('state', this.getState()); }

  _message(text, options = {}) {
    const message = {
      id: options.id || `z-msg-${++this._messageId}`, name: options.name || '月亮小屋',
      text, type: options.type || 'system', mine: Boolean(options.mine),
      ...(options.color ? { color: options.color } : {}),
      ...(options.source ? { source: options.source } : {}),
      ...(options.replyStatus ? { replyStatus: options.replyStatus } : {}),
    };
    this._state.messages.push(message);
    if (this._state.messages.length > 80) this._state.messages.shift();
    this._event('message', { ...message });
    this._emitState();
    return message;
  }

  _schedule(delay, callback, kind = 'general') {
    this._jobs.push({ at: this._clock + delay, callback, kind, order: this._order++ });
  }

  _scheduleReply(answer) {
    this._say(answer.text, answer.mood, answer.gesture, {
      priority: 10, scope: 'reply', replyTo: answer.replyTo, source: answer.source || 'local',
      replyMessage: answer.replyMessage,
    });
  }

  _setReceipt(id, status, extra = {}) {
    const message = this._state.messages.find(item => item.id === id);
    if (message) Object.assign(message, { replyStatus: status }, extra);
    if (this._state.replyTo === id) Object.assign(this._state, { replyStatus: status });
  }

  _deferAction(label, run) {
    if (!this._active) return false;
    if (!this._pendingAction) this._pendingAction = { label, run, phase: this._phase, stage: this._state.stage, fortunePhase: this._state.fortune?.phase, cardIndex: this._state.fortune?.activeIndex, round: this._state.round };
    this._state.queuedAction = this._pendingAction.label;
    this._emitState();
    return true;
  }

  _candidateScore(text) {
    if (fortuneVoteFromText(text) !== null) return 0;
    const words = text.replace(/[\p{P}\p{S}\s]/gu, '').toLowerCase();
    if (!words || /^(?:z)?(?:嗯|哦|噢|啊|哈|嘿|呵|好的|好呀|好哒|好|谢谢|感谢|收到|okay|ok|666|wow|哇|耶|赞)+(?:你|z)?$/.test(words)) return 0;
    return 1 + (/[?？]|怎么|如何|为什么|能不能|想问|请问|吗/.test(text) ? 3 : 0)
      + (/(?:@?Z|你)/i.test(text) ? 1 : 0)
      + (['emotion', 'work', 'study', 'love', 'card'].includes(topicOf(text)) ? 1 : 0);
  }

  _selectPlayerReply() {
    if (this._selectedPlayer || !this._chatCandidates.length || this._destroyed) return false;
    this._jobs = this._jobs.filter(job => job.kind !== 'reply-select');
    this._chatCandidates.sort((a, b) => b.score - a.score || b.order - a.order);
    const selected = this._chatCandidates.shift();
    for (const other of this._chatCandidates) this._setReceipt(other.message.id, 'seen');
    this._chatCandidates = [];
    this._selectedPlayer = { ...selected.message };
    Object.assign(this._state, { replyStatus: 'pending', replyTo: selected.message.id, replyError: null });
    this._setReceipt(selected.message.id, 'pending');
    if (this._textGenerator) this._generateText({ kind: 'chat', message: selected.message.text,
      replyTo: selected.message.id, replyMessage: selected.message, history: selected.history });
    else {
      this._state.replySource = 'local';
      this._scheduleReply({ ...this._answer(selected.message.text), replyTo: selected.message.id, replyMessage: selected.message, source: 'local' });
    }
    this._emitState();
    return true;
  }

  _say(text, mood = 'warm', gesture = 'explain', options = {}) {
    const phase = this._phase;
    this._queue.push({
      ...utteranceMetadata('z',options,{allowLegacy:true}),text, mood, gesture, transition: ['smooth','sudden'].includes(options.transition)?options.transition:'auto',
      duration: options.duration || Math.min(14500, Math.max(3600, Array.from(text).length * 155)),
      priority: options.priority || 0, order: this._order++,
      scope: options.scope || 'phase', phase,
      replyTo: options.replyTo || null, source: options.source || 'local',
      invitationGranted: options.invitationGranted,
      replyMessage: options.replyMessage ? { id: options.replyMessage.id, name: options.replyMessage.name, text: options.replyMessage.text } : null,
      generatedReading: Boolean(options.generatedReading),
      seed: Boolean(options.seed),
      onComplete: options.onComplete,
      preparedAudioKey: options.preparedAudioKey || null,
    });
    if (options.scope === 'reaction') {
      const reactions = this._queue.filter(item => item.scope === 'reaction' && !item.resume);
      if (reactions.length > 3) this._queue = this._queue.filter(item => item !== reactions[0]);
    }
    if (options.scope === 'reply' && options.replyTo) this._setReceipt(options.replyTo, 'queued');
    this._queue.sort((a, b) => b.priority - a.priority || a.order - b.order);
    this._pumpSpeech();
  }

  _pumpSpeech() {
    if (this._active || this._state.paused || this._destroyed || this._draining) return;
    this._draining = true;
    try { for (let attempts = 0; attempts < 60 && !this._active; attempts++) {
      if (this._pendingAction) {
        const action = this._pendingAction; this._pendingAction = null; this._state.queuedAction = null;
        if (action.phase === this._phase && action.stage === this._state.stage && action.fortunePhase === this._state.fortune?.phase && action.cardIndex === this._state.fortune?.activeIndex && action.round === this._state.round) action.run();
        continue;
      }
      const allowed = item => !this._state.lottery?.open || item.scope === 'story' || item.scope === 'reply';
      let index = this._queue.findIndex(item => allowed(item) && item.scope === 'story' && item.priority > 10);
      if (index < 0) index = this._queue.findIndex(item => allowed(item) && item.scope === 'reply');
      if (index < 0 && this._selectedPlayer) return;
      const hasShow = !this._state.lottery?.open && (this._continuations.length || this._queue.some(item => item.scope === 'phase'));
      if (index < 0 && this._chatCandidates.length && (!this._replyBurst || !hasShow)) {
        this._selectPlayerReply(); continue;
      }
      if (index < 0 && !this._state.lottery?.open && this._continuations.length) {
        const next = this._continuations.shift();
        if (next.scope !== 'phase' || next.phase === this._phase) next.run();
        continue;
      }
      if (index < 0) index = this._queue.findIndex(allowed);
      if (index < 0) return;
      const item = this._queue.splice(index, 1)[0];
      if (item.scope === 'phase' && item.phase !== this._phase) continue;
      if (item.resume && item.complete) { if (item.onComplete) item.onComplete(); continue; }
      if (item.scope === 'reply' && typeof item.invitationGranted === 'boolean'
        && item.invitationGranted !== this._hasIslandInvitation()) {
        Object.assign(item, this._invitationUpdateAnswer(), { source: 'local-state-update', invitationGranted: this._hasIslandInvitation() });
        item.duration = Math.min(14500, Math.max(3600, Array.from(item.text).length * 155));
        Object.assign(this._state, { textStatus: 'fallback', replyError: '获邀状态已更新，已按当前结果使用本地回应。' });
      }
      if (item.replyMessage) {
        item.text = `${item.replyMessage.name}说：「${item.replyMessage.text}」。${item.text}`;
        item.duration = Math.min(30000, Math.max(3600, Array.from(item.text).length * 155));
      }
      const speechId = ++this._speechId;
      this._active = {
        ...item, id: speechId, complete: false, until: this._clock + item.duration,
      };
      Object.assign(this._state, {
        currentSpeaker: 'Z', currentSpeech: item.text, currentSpeechId: speechId, currentSource: item.source, mood: item.mood, gesture: item.gesture,
      });
      if (item.scope === 'reply') {
        Object.assign(this._state, { replyStatus: 'replying', replyTo: item.replyTo, replySource: item.source });
        this._setReceipt(item.replyTo, 'replying');
      } else this._replyBurst = 0;
      if (!item.resume && (item.scope === 'reply' || item.generatedReading)) this._rememberText('assistant', item.text);
      if (!item.resume) this._message(item.text, { name: 'Z', type: 'z', color: '#8c78a1', source: item.source });
      else this._emitState();
      this._event('speak', {
        ...utteranceMetadata('z',item,{allowLegacy:true}),id: speechId, speechId, text: item.text, mood: item.mood, gesture: item.gesture, duration: item.duration,
        scope: item.scope, replyTo: item.replyTo, source: item.source, transition:item.transition,
        resume: Boolean(item.resume), utteranceKey: String(item.order),
        preparedAudioKey:item.preparedAudioKey,
      });
    } } finally { this._draining = false; }
  }

  _setStage(stage) {
    if (!this._audienceReadings && ['draw', 'reading'].includes(stage)) return false;
    this._invalidateTextRequests();
    this._phase++;
    this._queue = this._queue.filter(item => item.scope !== 'phase');
    this._continuations = this._continuations.filter(item => item.scope !== 'phase');
    Object.assign(this._state, {
      stage, stageLabel: LABELS[stage], currentSpeech: this._active?.text || '',
      waitingForCard: false, currentSpeechId: this._active?.id || null,
    });
    if (stage !== 'fortune' && !(stage === 'free' && this._state.fortune?.phase === 'complete')) delete this._state.fortune;
    this._fortuneVotingEndsAt = Infinity;
    this._fortuneNextVoteAt = Infinity;
    this._nextDrawAt = Infinity;
    this._nextCardAt = Infinity;
    this._nextReadingAt = Infinity;
    this._emitState();
  }

  start() {
    if (this._started || this._destroyed) return this.getState();
    this._started = true;
    this._lastTick = performance.now();
    this._timer = setInterval(() => this._tick(), 160);
    this._message('欢迎来到 Z 的占卜小屋。今晚的月光，也为你留了一个位置。');
    this._message(this._audienceReadings
      ? '这是可交互演示：观众与预埋剧情由本地脚本模拟。连接文字服务后，自由聊天和你的专属解读会实时生成。'
      : '这是可交互演示：观众与预埋剧情由本地脚本模拟，连接文字服务后可以和 Z 自由聊天。');
    this._say('来啦，晚上好！我是 Z，今晚在岛上陪你们玩塔罗。', 'happy', 'wave', { duration: 10500 });
    this._say(this._audienceReadings
      ? '今天先一起看看运势，再抽三个人，聊聊你们正在纠结的事。谁会被抽到呢？来，先把牌摆好。'
      : '今天先看运势，心里放一件在意的事。等牌摆好，你们来选。', 'happy', 'explain', {
      duration: 10000, onComplete: () => this._enterFortune(),
    });
    this._emitState();
    return this.getState();
  }

  _tick() {
    if (this._destroyed) return;
    const now = performance.now();
    const delta = Math.min(1000, Math.max(0, now - this._lastTick));
    this._lastTick = now;
    if (this._state.paused) return;
    this._clock += delta;
    const due = this._jobs.filter(job => job.at <= this._clock);
    this._jobs = this._jobs.filter(job => job.at > this._clock);
    for (const job of due) job.callback();
    const waitingForChat = this._hasPendingPlayerChat();
    const chatHasFloor = waitingForChat || this._active?.scope === 'reply';
    // closeLottery restores the entire overlay interval; do not count the
    // same elapsed time again when a player chat is also pending underneath.
    if (chatHasFloor && !this._state.lottery?.open) for (const key of ['_nextDrawAt', '_nextCardAt', '_nextReadingAt'])
      if (Number.isFinite(this[key])) this[key] += delta;
    if (this._active && (this._speechDriven ? this._active.complete : this._active.until <= this._clock)) this._finishSpeech();
    this._pumpSpeech();
    this._tickFortuneVoting();
    if (this._clock >= this._nextViewerAt) {
      this._state.viewers = Math.min(29999, Math.max(23500, this._state.viewers + randomBetween(-5, 11)));
      this._state.likes += randomBetween(2, 10);
      if (this._state.stage === 'draw') this._state.drawParticipants += randomBetween(0, 2);
      this._nextViewerAt = this._clock + randomBetween(2800, 4600);
      this._emitState();
    }
    if (this._clock >= this._nextChatAt) {
      this._seedChat();
      this._nextChatAt = this._clock + randomBetween(3000, 6000);
    }
    if (!this._state.lottery?.open && !this._hasPendingPlayerChat() && this._active?.scope !== 'reply' && this._clock >= this._nextDrawAt) {
      this._nextDrawAt = Infinity;
      this.drawNext();
    }
    if (!this._state.lottery?.open && !this._hasPendingPlayerChat() && this._active?.scope !== 'reply' && this._clock >= this._nextCardAt) {
      this._nextCardAt = Infinity;
      this.selectCard(randomBetween(0, 2));
    }
    if (!this._state.lottery?.open && !this._hasPendingPlayerChat() && this._active?.scope !== 'reply' && this._clock >= this._nextReadingAt) {
      this._nextReadingAt = Infinity;
      this._advanceReading();
    }
  }

  _seedChat() {
    const person = VIEWERS[randomBetween(0, VIEWERS.length - 1)];
    const choices = CHAT[this._state.stage];
    const text = choices[randomBetween(0, choices.length - 1)];
    const message = this._message(text, { name: person.name, type: 'chat', color: person.color });
    this._seedCount++;
    const canReact = ['free', 'draw', 'fortune'].includes(this._state.stage);
    const seedPending = this._active?.seed || this._queue.some(item => item.seed);
    if (this._seedCount % 5 === 0 && canReact && !seedPending) {
      const answer = this._answer(text);
      const brief = answer.text.split(/(?<=[。！？])/u).slice(0, 2).join('');
      this._say(brief, answer.mood, answer.gesture, {
        scope: 'reaction', priority: 1, duration: 4500, seed: true, replyMessage: message,
      });
    }
  }

  setTextGenerator(generator) {
    if (generator !== null && typeof generator !== 'function') return false;
    this._invalidateTextRequests();
    this._textGenerator = generator;
    Object.assign(this._state, {
      textMode: generator ? 'provider' : 'local', textStatus: generator ? 'ready' : 'local',
      replyError: null,
    });
    this._emitState();
    return true;
  }

  _rememberText(role, content) {
    const value = wireText(content, 500);
    if (!value) return;
    this._history.push({ role, content: value });
    if (this._history.length > 6) this._history.splice(0, this._history.length - 6);
  }

  _invalidateTextRequests() {
    this._textEpoch++;
    for (const request of this._textPending.values()) if (request.kind === 'reading' && request.viewer?.reading) {
      Object.assign(request.viewer.reading, { status: 'cancelled', source: 'local-fallback', error: '已切换到其他内容，保留本地牌面解读。' });
      if (this._state?.currentViewer?.question === request.viewer.question && this._state.currentViewer.mine)
        this._state.currentViewer.reading = { ...request.viewer.reading };
    }
    this._textPending.clear();
    for (const candidate of this._chatCandidates) this._setReceipt(candidate.message.id, 'seen');
    this._chatCandidates = [];
    if (this._selectedPlayer && this._active?.replyTo !== this._selectedPlayer.id) {
      this._setReceipt(this._selectedPlayer.id, 'seen');
      this._selectedPlayer = null;
    }
    this._queue = this._queue.filter(item => item.scope !== 'reply' || item.replyTo === this._active?.replyTo);
    this._latestChatId = null;
    this._latestReadingId = null;
    this._jobs = this._jobs.filter(job => job.kind !== 'text-timeout' && job.kind !== 'reply-select');
    if (this._state) {
      if (['pending', 'queued'].includes(this._state.replyStatus)) this._state.replyStatus = 'seen';
      this._state.textStatus = this._textGenerator ? 'ready' : 'local';
    }
  }

  _hasPendingPlayerChat() {
    return Boolean(this._selectedPlayer);
  }

  _hasIslandInvitation() {
    return this._state.islandInvited === true
      && Boolean(this._state.lottery?.cards.some(card => card.revealed && card.reward?.id === 'island-invitation'));
  }

  _invitationUpdateAnswer() {
    return this._hasIslandInvitation()
      ? { text: '刚刚那封邀请就是你的啦！等你来，我去码头接你。', mood: 'happy', gesture: 'wave' }
      : { text: '登岛邀请还没到你手里，先等等消息。', mood: 'warm', gesture: 'nod' };
  }

  _lotteryOutcome() {
    const revealed = this._state.lottery?.cards.filter(card => card.revealed && card.reward) || [];
    return wireText(`已揭晓奖励：${revealed.length ? revealed.map(card => card.reward.title).join('、') : '无'}；${this._hasIslandInvitation()
      ? '玩家已获得登岛邀请，Z已约定去码头接他'
      : '尚未获得登岛邀请'}。`, 240);
  }

  _textRequestCurrent(request) {
    if (this._destroyed || request.epoch !== this._textEpoch || request.phase !== this._phase
      || this._textPending.get(request.id) !== request) return false;
    if (request.kind === 'chat') return this._latestChatId === request.id;
    return this._audienceReadings && this._latestReadingId === request.id && this._state.stage === 'reading'
      && this._state.round === request.round && this._state.currentViewer?.mine
      && this._state.currentViewer.question === request.question
      && this._state.currentCard?.name === request.cardName;
  }

  _generateText({ kind, message, replyTo = null, replyMessage = null, viewer = null, history }) {
    if (kind === 'reading' && !this._audienceReadings) return false;
    const generator = this._textGenerator;
    if (!generator) return false;
    this._textSequence++;
    let nonce = this._textSequence;
    const id = globalThis.crypto?.randomUUID?.() || 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, char => {
      const value = (Math.floor(Math.random() * 16) + (nonce & 15)) & 15; nonce = Math.floor(nonce / 16);
      return (char === 'x' ? value : (value & 3) | 8).toString(16);
    });
    const card = this._state.currentCard;
    const request = {
      id, kind, message, replyTo, replyMessage: replyMessage ? { ...replyMessage } : null, viewer, epoch: this._textEpoch, phase: this._phase,
      round: this._state.round, question: viewer?.question || '', cardName: card?.name || null,
      invitationGranted: this._hasIslandInvitation(),
    };
    if (kind === 'chat') {
      this._latestChatId = id;
    } else this._latestReadingId = id;
    this._textPending.set(id, request);
    this._state.textStatus = 'pending';
    const payload = {
      requestId: id, message: wireText(message, 500), kind,
      context: {
        stage: wireText(this._state.stage, 32),
        question: wireText(viewer?.question || this._state.currentViewer?.question || this._state.drawQuestion || '', 500),
        cardTitle: wireText(card?.title || '', 80), cardMeaning: wireText(card?.meaning || '', 800), cardAdvice: wireText(card?.advice || '', 800),
        lotteryOutcome: this._lotteryOutcome(),
      },
      history: (history || this._history).slice(-6).map(item => ({ role: item.role, content: wireText(item.content, 500) })),
    };
    this._schedule(45000, () => this._settleText(request, null, true), 'text-timeout');
    Promise.resolve().then(() => this._textRequestCurrent(request) ? generator(payload) : null)
      .then(result => this._settleText(request, result, false), () => this._settleText(request, null, true));
    this._emitState();
    return true;
  }

  _settleText(request, result, failed) {
    if (!this._textRequestCurrent(request)) return;
    const text = result && completeReplyText(result.text);
    if (!text) failed = true;
    request.ready = { result, failed, text };
    if (!this._state.paused) this._applyGeneratedText(request);
  }

  _applyGeneratedText(request) {
    if (!this._textRequestCurrent(request) || !request.ready) return;
    const { result, text } = request.ready;
    // A reply generated before the invitation was awarded cannot deny the
    // now-visible result. Ordinary postcard/bookmark reveals do not invalidate
    // it, and recovering locally never creates another paid request.
    const invitationChanged = request.kind === 'chat' && request.invitationGranted !== this._hasIslandInvitation();
    const failed = request.ready.failed || invitationChanged;
    this._textPending.delete(request.id);
    const source = invitationChanged ? 'local-state-update'
      : failed ? 'local-fallback' : cleanText(result.source, 40) || 'deepseek';
    const fallback = failed || source === 'local-fallback';
    const mood = !failed && ['happy', 'warm', 'serious', 'thinking', 'neutral', 'shy', 'sad', 'surprised'].includes(result.mood) ? result.mood
      : request.kind === 'reading' ? 'serious' : 'warm';
    this._state.textStatus = fallback ? 'fallback'
      : this._textPending.size ? 'pending' : 'ready';
    if (request.kind === 'chat') {
      const answer = invitationChanged
        ? this._invitationUpdateAnswer()
        : failed ? this._answer(request.message) : { text, mood, gesture: ({happy:'handWave',warm:'present',serious:'openArms',thinking:'chin',shy:'coverMouth',sad:'nod',surprised:'startle',neutral:'none'})[mood] || 'explain' };
      Object.assign(this._state, {
        replySource: source,
        replyError: invitationChanged ? '获邀状态已更新，已按当前结果使用本地回应。'
          : fallback ? '文字服务暂时不可用，已使用本地回应。' : null,
      });
      this._say(answer.text, answer.mood, answer.gesture, {
        priority: 10, scope: 'reply', replyTo: request.replyTo, replyMessage: request.replyMessage, source, invitationGranted: this._hasIslandInvitation(),
      });
    } else {
      const viewer = request.viewer;
      viewer.reading = {
        ...viewer.reading,
        interpretation: failed ? viewer.reading.interpretation : text,
        source, status: fallback ? 'fallback' : 'ready',
        error: fallback ? '文字服务暂时不可用，已使用与原题和牌面匹配的本地解读。' : null,
      };
      this._state.currentViewer = { ...viewer, card: { ...viewer.card }, reading: { ...viewer.reading } };
      this._clearPhaseSpeech();
      const response = failed
        ? `你问${quote(viewer.question)}。${viewer.reading.interpretation}${viewer.reading.advice}` : text;
      this._say(response, mood, 'explain', {
        priority: 9, source, generatedReading: true,
        onComplete: () => { this._nextReadingAt = this._clock + 3800; },
      });
    }
    this._emitState();
  }

  triggerStory(eventId = 'moonlight-invitation', actor = 'player') {
    if (this._destroyed || this._state.paused || actor !== 'player'
      || typeof eventId !== 'string' || !/^[a-z0-9-]{1,64}$/.test(eventId)
      || this._storyEvents.has(eventId) || this._state.lottery) return false;
    this._storyEvents.add(eventId);
    this._state.lottery = {
      eventId, phase: 'ready', open: true, nextIndex: 0,
      cards: ['postcard', 'island-invitation', 'star-bookmark'].map((id, index) => ({
        id, label: ['月光信封', '海风来信', '星图密语'][index], revealed: false, reward: null, playerOnly: true,
      })),
      claimedRewards: [...this._state.claimedRewards], islandInvited: this._state.islandInvited,
    };
    this._holdForLottery();
    this._storyLine('等一下，这里还有三封给你的来信。先拆哪封？');
    this._emitState();
    return true;
  }

  _holdForLottery() {
    this._lotteryOpenedAt = this._clock;
  }

  _storyLine(text, priority = 8) {
    // Rewards update immediately; their spoken acknowledgement waits its turn.
    this._say(text, 'happy', 'wave', { scope: 'story', priority, source: 'local' });
  }

  revealLotteryCard(index, actor = 'player') {
    const lottery = this._state.lottery;
    if (this._destroyed || this._state.paused || actor !== 'player' || !lottery?.open
      || !Number.isInteger(index) || index < 0 || index > 2 || lottery.cards[index].revealed) return false;
    const rewards = [
      { id: 'moonlight-postcard', title: '月光明信片', description: '一张由 Z 写下今晚寄语的小岛明信片。', icon: '✉' },
      { id: 'island-invitation', title: '恭喜被抽中登岛机会', description: '一份登岛邀请，Z 已约定在码头接你。', icon: '⛵' },
      { id: 'z-star-bookmark', title: 'Z 星图书签', description: '一枚 Z 的星图书签，留给你标记正在读的那一页。', icon: '✦' },
    ];
    const reward = rewards[index];
    lottery.cards[index].revealed = true;
    lottery.cards[index].reward = { ...reward };
    if (!this._state.claimedRewards.includes(reward.id)) this._state.claimedRewards.push(reward.id);
    if (index === 1) this._state.islandInvited = true;
    lottery.claimedRewards = [...this._state.claimedRewards];
    lottery.islandInvited = this._state.islandInvited;
    lottery.nextIndex = lottery.cards.findIndex(card => !card.revealed);
    if (lottery.nextIndex < 0) lottery.nextIndex = 3;
    lottery.phase = lottery.cards.every(card => card.revealed) ? 'complete' : 'revealing';
    this._message(`获得${reward.title}：${reward.description}`, { name: SELF, mine: true, source: 'local' });
    this._event('effect', { type: 'lottery', icon: reward.icon, label: reward.title });
    this._storyLine(index === 1
      ? '恭喜被抽中登岛机会！这张邀请是你的啦。等你来，我去码头接你！'
      : index === 0 ? '月光明信片，收好啦！今晚这份好运先寄给你。'
        : '是星图书签，送给你。夹在你最近看的那一页吧。', index === 1 ? 12 : 8);
    this._emitState();
    return true;
  }

  closeLottery() {
    const lottery = this._state.lottery;
    if (this._destroyed || this._state.paused || !lottery?.open) return false;
    lottery.open = false;
    lottery.phase = 'closed';
    const held = this._lotteryOpenedAt === null ? 0 : this._clock - this._lotteryOpenedAt;
    for (const key of ['_nextDrawAt', '_nextCardAt', '_nextReadingAt']) if (Number.isFinite(this[key])) this[key] += held;
    this._lotteryOpenedAt = null;
    if (!this._audienceReadings && this._state.stage === 'fortune' && this._state.fortune?.phase === 'complete') {
      if (!this._continuations.some(item => item.kind === 'close-lottery'))
        this._continuations.push({ scope: 'phase', phase: this._phase, kind: 'close-lottery', run: () => this._enterFree() });
    }
    this._pumpSpeech();
    this._emitState();
    return true;
  }

  openLottery() {
    const lottery = this._state.lottery;
    if (this._destroyed || this._state.paused || !lottery || lottery.open) return false;
    lottery.open = true;
    lottery.phase = lottery.cards.every(card => card.revealed) ? 'complete'
      : lottery.cards.some(card => card.revealed) ? 'revealing' : 'ready';
    this._holdForLottery();
    this._emitState();
    return true;
  }

  // The director owns completion: elapsed subtitle or engine time cannot cut audio.
  setSpeechDriven(value) {
    this._speechDriven = Boolean(value);
    return this._speechDriven;
  }

  completeSpeech(id) {
    if (this._destroyed || !this._active || this._active.id !== id) return false;
    this._active.complete = true;
    if (!this._state.paused) {
      this._finishSpeech();
      this._pumpSpeech();
    }
    return true;
  }

  _finishSpeech() {
    const finished = this._active;
    if (!finished) return;
    this._active = null;
    Object.assign(this._state, { currentSpeech: '', currentSpeechId: null, gesture: 'nod' });
    if (finished.scope === 'reply') {
      this._setReceipt(finished.replyTo, 'replied');
      if (this._selectedPlayer?.id === finished.replyTo) this._selectedPlayer = null;
      this._replyBurst++;
    }
    if (finished.onComplete) this._continuations.push({ scope: finished.scope, phase: finished.phase, run: finished.onComplete });
    this._emitState();
  }

  _clearPhaseSpeech() {
    // Called only at a speech boundary. Never discard an utterance in progress.
    this._queue = this._queue.filter(item => item.scope !== 'phase');
    this._continuations = this._continuations.filter(item => item.scope !== 'phase');
  }

  _enterFortune() {
    if (this._deferAction('说完这段后看今日运势', () => this._enterFortune())) return true;
    this._setStage('fortune');
    this._fortuneRound++;
    this._fortuneVotes.clear();
    this._fortuneVoterSerial = 0;
    this._fortuneCards = shuffledCards(`moon-island-fortune-${this._fortuneRound}`);
    Object.assign(this._state, {
      currentCard: null, selectedCardIndex: null,
      fortune: {
        phase: 'inviting',
        options: ['月光', '星海', '晚风'].map((label, index) => ({
          id: 'ABC'[index], label, votes: 0, percent: 0, card: null, revealed: false,
        })),
        totalVotes: 0, myVote: null, remainingSeconds: 18, activeIndex: null, winnerIndex: null,
      },
    });
    this._say('月光、星海、晚风，你第一眼看中了哪张？给你们十八秒，点牌或者发「我选 A」，中途也能改。', 'happy', 'draw', {
      onComplete: () => this._beginFortuneVoting(),
    });
    this._emitState();
  }

  startVoting() {
    if (this._destroyed || this._state.paused || this._state.stage !== 'fortune' || this._state.fortune?.phase !== 'inviting') return false;
    if (this._deferAction('说完这段后开始投票', () => this.startVoting())) return true;
    this._clearPhaseSpeech();
    const started = this._beginFortuneVoting();
    if (started) this._say('开选！让我看看今晚哪张最抢手。', 'happy', 'wave');
    return started;
  }

  _beginFortuneVoting() {
    const fortune = this._state.fortune;
    if (this._state.stage !== 'fortune' || fortune?.phase !== 'inviting') return false;
    fortune.phase = 'voting';
    // These IDs represent distinct simulated viewers. Every ID votes once;
    // the local player has a separate replaceable vote.
    this._fortuneEligible = Math.max(0, this._state.viewers - 1);
    this._fortuneTarget = Math.floor(this._fortuneEligible * 0.58);
    this._fortuneVotingStartedAt = this._clock;
    this._fortuneVotingEndsAt = this._clock + 18000;
    this._fortuneNextVoteAt = this._clock + 300;
    this._message('今日运势投票开放 18 秒：点击 A / B / C，或发「我选 A」。统计包含模拟观众，每位观众一票，改票不重复累计。');
    this._emitState();
    return true;
  }

  _recountFortuneVotes() {
    const fortune = this._state.fortune;
    if (!fortune) return;
    const counts = [0, 0, 0];
    for (const index of this._fortuneVotes.values()) counts[index]++;
    if (fortune.myVote !== null) counts[fortune.myVote]++;
    fortune.totalVotes = counts.reduce((sum, value) => sum + value, 0);
    const raw = counts.map(value => fortune.totalVotes ? value * 100 / fortune.totalVotes : 0);
    const percents = raw.map(Math.floor);
    // Largest remainder makes the displayed whole-number percentages sum to 100.
    let remaining = fortune.totalVotes ? 100 - percents.reduce((sum, value) => sum + value, 0) : 0;
    const order = [0, 1, 2].sort((a, b) => (raw[b] - percents[b]) - (raw[a] - percents[a]) || a - b);
    for (let index = 0; index < remaining; index++) percents[order[index % 3]]++;
    fortune.options.forEach((option, index) => { option.votes = counts[index]; option.percent = percents[index]; });
  }

  _tickFortuneVoting() {
    const fortune = this._state.fortune;
    if (this._state.stage !== 'fortune' || fortune?.phase !== 'voting') return;
    const remaining = Math.max(0, Math.ceil((this._fortuneVotingEndsAt - this._clock) / 1000));
    let changed = remaining !== fortune.remainingSeconds;
    fortune.remainingSeconds = remaining;
    if (this._clock >= this._fortuneNextVoteAt) {
      const progress = Math.min(1, (this._clock - this._fortuneVotingStartedAt) / 18000);
      const available = Math.max(0, Math.min(this._fortuneEligible, this._state.viewers - 1));
      const target = Math.min(available, Math.floor(this._fortuneTarget * progress));
      while (this._fortuneVoterSerial < target) {
        const voter = this._fortuneVoterSerial++;
        // A stable distribution produces a real cumulative simulated tally,
        // rather than independently changing decorative numbers.
        const bucket = hash(`viewer-${this._fortuneRound}-${voter}`) % 100;
        const choice = bucket < 31 ? 0 : bucket < 72 ? 1 : 2;
        this._fortuneVotes.set(`viewer-${voter}`, choice);
      }
      this._fortuneNextVoteAt = this._clock + 480;
      this._recountFortuneVotes();
      changed = true;
    }
    if (this._clock >= this._fortuneVotingEndsAt) this.closeVoting({ automatic: true });
    else if (changed) this._emitState();
  }

  vote(index) {
    const fortune = this._state.fortune;
    if (this._destroyed || this._state.paused || this._state.stage !== 'fortune' || fortune?.phase !== 'voting'
      || !Number.isInteger(index) || index < 0 || index > 2) return false;
    if (this._clock >= this._fortuneVotingEndsAt) { this.closeVoting({ automatic: true }); return false; }
    const previous = fortune.myVote;
    if (previous === index) return true;
    fortune.myVote = index;
    this._recountFortuneVotes();
    this._event('effect', { type: 'vote', icon: '✦', label: `${previous === null ? '已投' : '改选'} ${fortune.options[index].id} · ${fortune.options[index].label}` });
    this._emitState();
    return true;
  }

  closeVoting({ automatic = false } = {}) {
    const fortune = this._state.fortune;
    if (this._destroyed || this._state.paused || this._state.stage !== 'fortune'
      || !fortune || !['inviting', 'voting'].includes(fortune.phase)) return false;
    if (!automatic && this._deferAction('说完这段后收票', () => this.closeVoting())) return true;
    this._clearPhaseSpeech();
    this._fortuneVotingEndsAt = Infinity;
    this._fortuneNextVoteAt = Infinity;
    fortune.phase = 'revealing';
    fortune.remainingSeconds = 0;
    const highest = Math.max(...fortune.options.map(option => option.votes));
    const leaders = fortune.options.map((option, index) => ({ option, index })).filter(item => item.option.votes === highest);
    fortune.winnerIndex = fortune.totalVotes ? leaders[0].index : null;
    const result = !fortune.totalVotes
      ? '还没有选票，那就三张都看看。'
      : leaders.length > 1
        ? `${leaders.map(item => item.option.label).join('和')}一样多，那就按顺序来。`
        : `${leaders[0].option.label}最多，另外两张我们也看。`;
    this._say(`好，收票！${result}从月光开始翻。`, 'happy', 'draw', {
      onComplete: () => this.revealNext(),
    });
    this._emitState();
    return true;
  }

  revealNext() {
    const fortune = this._state.fortune;
    if (this._destroyed || this._state.paused || this._state.stage !== 'fortune' || fortune?.phase !== 'revealing') return false;
    if (this._deferAction('说完这段后翻下一张', () => this.revealNext())) return true;
    this._clearPhaseSpeech();
    const index = fortune.options.findIndex(option => !option.revealed);
    if (index < 0) {
      fortune.phase = 'complete';
      if (!this._storyEvents.has('moonlight-invitation') && !this._jobs.some(job => job.kind === 'story'))
        this._schedule(3000, () => this.triggerStory('moonlight-invitation'), 'story');
      const favorite = fortune.winnerIndex === null ? '' : `刚才最多人选的是${fortune.options[fortune.winnerIndex].card.title}，`;
      this._say(this._audienceReadings
        ? `三张都看完了。${favorite}哪句最有用，今天就试一试。接下来，轮到你们的问题。`
        : `三张看完啦。${favorite}你觉得哪句对得上？`, 'happy', 'wave', {
        onComplete: () => this._afterFortune(),
      });
      this._emitState();
      return true;
    }
    const option = fortune.options[index];
    const card = { ...this._fortuneCards[index] };
    option.card = card;
    option.revealed = true;
    fortune.activeIndex = index;
    this._state.currentCard = { ...card };
    this._state.selectedCardIndex = index;
    const winner = index === fortune.winnerIndex;
    const participation = fortune.totalVotes
      ? option.votes ? `${spokenShare(option.percent)}的选票在这张；` : '这张还没收到票，也看看；'
      : '';
    this._event('effect', { type: 'draw', icon: card.symbol, label: `${option.id} · ${option.label} → ${card.title} 正位` });
    const opening = ['先翻月光：', '再看星海：', '最后是晚风：'][index];
    const collective = {
      'THE STAR': '今天找一件你比从前做得好的事，别又把它当成理所当然。',
      'THE SUN': '挑一句一直没说出口的真心话，找合适的人说清楚，也听听对方的回答。',
      'THE MAGICIAN': '今天用手边的东西做个小样，第一版粗糙一点也算开始。',
      'TEMPERANCE': '今天少排一件事，把最重要的那件做好，留点力气给自己。',
      'THE MOON': '挑一个让你纠结的细节先核实，别让猜测替你做决定。',
      'THE WORLD': '找一件惦记很久的小事收个尾，再想下一步要去哪儿。',
    }[card.name];
    this._say(`${opening}${card.title}，正位。${participation}${card.meaning}${winner ? '这张选的人最多，认真听这句：' : ''}${collective}${winner ? '对得上的，可以告诉我。' : ''}`, 'serious', 'explain', {
      onComplete: () => this.revealNext(),
    });
    this._emitState();
    return true;
  }

  _afterFortune() {
    if (this._audienceReadings) { this._enterDraw(); return true; }
    const lottery = this._state.lottery;
    if (!lottery) return this.triggerStory('moonlight-invitation');
    if (lottery.open) return true;
    this._enterFree();
    return true;
  }

  _enterDraw() {
    if (!this._audienceReadings) return false;
    if (this._deferAction('说完这段后抽选观众', () => this._enterDraw())) return true;
    this._setStage('draw');
    Object.assign(this._state, { round: 0, selectedViewers: [], currentCard: null, selectedCardIndex: null, currentViewer: null });
    this._say('接下来抽三个人！想问的事写具体一点，比如要不要换工作，或者这次面试怎么准备。点「报名占卜」，看看谁先被抽到。', 'happy', 'wave', {
      duration: 10500, onComplete: () => { this._nextDrawAt = this._clock + 6500; },
    });
    this._emitState();
  }

  sendMessage(text) {
    const value = wireText(text, 120);
    if (!value || this._destroyed) return false;
    const wallNow = performance.now();
    if (wallNow - this._lastMessageAt < 480) return false;
    this._lastMessageAt = wallNow;
    const message = this._message(value, { name: SELF, type: 'chat', mine: true, color: '#a58b6c', replyStatus: 'seen' });
    const history = this._history.map(item => ({ ...item }));
    this._rememberText('user', value);
    const voteIndex = fortuneVoteFromText(value);
    if (voteIndex !== null) {
      this.vote(voteIndex);
      this._emitState();
      return true;
    }
    const normalized = value.replace(/[\p{P}\p{S}\s]/gu, '').toLowerCase();
    const duplicate = this._recentPlayerMessages.has(normalized) && this._clock - this._recentPlayerMessages.get(normalized) < 60000;
    this._recentPlayerMessages.set(normalized, this._clock);
    for (const [key, at] of this._recentPlayerMessages) if (this._clock - at >= 60000) this._recentPlayerMessages.delete(key);
    const score = this._candidateScore(value);
    if (score && !duplicate) {
      this._chatCandidates.push({ message: { id: message.id, name: message.name, text: message.text }, history, score, order: this._order++ });
      this._chatCandidates.sort((a, b) => b.score - a.score || b.order - a.order);
      while (this._chatCandidates.length > 4) this._setReceipt(this._chatCandidates.pop().message.id, 'seen');
      if (!this._selectedPlayer && !this._jobs.some(job => job.kind === 'reply-select'))
        this._schedule(700, () => { this._selectPlayerReply(); this._pumpSpeech(); }, 'reply-select');
    }
    if (!this._selectedPlayer && !this._chatCandidates.length && this._state.replyStatus === 'idle')
      Object.assign(this._state, { replyStatus: 'seen', replyTo: message.id });
    this._emitState();
    return true;
  }

  _answer(text) {
    if (/你.*(?:AI|人工智能|NPC|程序)|公司.*(?:毁岛|摧毁|关闭.*岛)|七天.*(?:毁|销)|世界.*(?:假的|虚拟)/i.test(text))
      return { text: '这话从哪儿听来的？我没见过你说的这些。', mood: 'thinking', gesture: 'nod' };
    if (/(?:岛|岛民|朋友).*(?:骗子|骗人|坏人)|(?:骗子|骗人|坏人).*(?:岛|岛民|朋友)/.test(text))
      return { text: '你指哪件事？先说具体点，我也想听清楚。', mood: 'serious', gesture: 'nod' };
    if (/不想.*(?:看牌|选牌|占卜|活动)|不看.*牌|(?:很累|好累|累了).*不想/.test(text))
      return { text: '那就先不看。想聊我陪你，不想说话也可以。', mood: 'warm', gesture: 'nod' };
    if (/登岛|去岛上|来岛上|中奖|抽中|邀请|船.*(?:到|开|几点)|码头/.test(text)) {
      if (!this._hasIslandInvitation()) return { text: '我这边还没看到你的登岛邀请，先别着急。', mood: 'warm', gesture: 'nod' };
      if (/船|几点/.test(text)) return { text: '船期我这会儿还不知道。定下来告诉我，我去码头接你。', mood: 'happy', gesture: 'wave' };
      if (/明天|后天|今天/.test(text)) return { text: '这么快？船什么时候到，记得告诉我，我去码头接你。', mood: 'happy', gesture: 'wave' };
      return { text: '对，邀请已经是你的啦！什么时候来，提前告诉我。', mood: 'happy', gesture: 'wave' };
    }
    if (/住在哪|哪里住|住哪里|哪个岛|岛.*叫什么|故乡/.test(text))
      return { text: '我住在明日岛呀。你想先听哪边的事？', mood: 'happy', gesture: 'wave' };
    if (/发夹.*(?:可爱|好看)|(?:可爱|好看).*发夹/.test(text)) return { text: '嘿嘿，这个月亮发夹我也喜欢。眼光不错嘛！', mood: 'happy', gesture: 'wave' };
    if (/幸运色/.test(text)) return { text: '今天我选雾紫色！挑个你看着喜欢的小物件就行。', mood: 'happy', gesture: 'explain' };
    if (/谢谢|感谢/.test(text)) return { text: '不用客气呀。之后有新进展，也来讲讲。', mood: 'warm', gesture: 'nod' };
    if (/声音.*(?:好听|温柔)|(?:好听|温柔).*声音/.test(text)) return { text: '嘿嘿，收到！这句我先开心一下。', mood: 'happy', gesture: 'wave' };
    if (/期待|好激动/.test(text)) return { text: '嘿嘿，那我可不能掉链子。', mood: 'happy', gesture: 'wave' };
    switch (topicOf(text)) {
      case 'love': return { text: questionDetail(text, 'love') || '先挑你最在意的一点说清楚，也听听对方怎么想。光靠猜，可太累了。', mood: 'warm', gesture: 'nod' };
      case 'work': return { text: questionDetail(text, 'work') || '是这份工作不合适，还是最近这件事让你累了？先弄清这一点，别急着替以后做完所有决定。', mood: 'serious', gesture: 'explain' };
      case 'study': return { text: questionDetail(text, 'study') || '先别跟别人比进度，挑一块二十五分钟能完成的任务试试。做完看看哪里真会了，卡住再问。', mood: 'warm', gesture: 'explain' };
      case 'emotion': return { text: questionDetail(text, 'emotion') || '今天先少要求自己一点，歇一会儿也行。要是想说，我在听。', mood: 'warm', gesture: 'nod' };
      case 'plan': return { text: questionDetail(text, 'plan') || '先试一小版怎么样？做完看看实际感受，再决定要不要继续。', mood: 'happy', gesture: 'explain' };
      case 'story': return { text: '岛上的事，你想先听哪一件？码头，还是我那张改了又改的地图？', mood: 'happy', gesture: 'wave' };
      case 'card': {
        const card = /太阳/.test(text) ? CARDS[1] : /月亮牌/.test(text) ? CARDS[4] : this._state.currentCard || CARDS[0];
        return { text: `${card.meaning}具体到你，还得结合眼下的事看。`, mood: 'serious', gesture: 'draw' };
      }
      case 'greeting': return { text: /晚安/.test(text) ? '晚安，早点休息。下次见啦。' : '来啦，晚上好！前排还有位置，快坐。', mood: 'happy', gesture: 'wave' };
      default: {
        const replies = [
          '你说的这件事，我还想听具体一点。',
          '嗯，我在听，接着说。',
          '这一下把我问住了，让我想想。你最在意的是哪一点？',
        ];
        return { text: replies[hash(text) % replies.length], mood: 'thinking', gesture: 'nod' };
      }
    }
  }


  toggleFollow() {
    if (this._destroyed) return this._state.followed;
    this._state.followed = !this._state.followed;
    this._persist();
    this._emitState();
    if (this._state.followed) {
      this._message(`${SELF}关注了 Z，下次月亮升起时，小屋还会为你亮着灯。`);
      this._say('嘿嘿，下次来就好找我啦。', 'happy', 'wave', { priority: 6, scope: 'reaction', duration: 6400 });
    }
    return this._state.followed;
  }

  like() {
    if (this._destroyed) return false;
    this._state.likes++;
    const now = performance.now();
    if (now - this._lastLikeEffectAt > 90) {
      this._event('effect', { type: 'like', icon: '♡', label: '+1 心动' });
      this._lastLikeEffectAt = now;
    }
    this._emitState();
    return true;
  }


  advance() {
    if (this._destroyed || this._state.paused) return false;
    if (this._state.stage === 'free' && !this._audienceReadings) return false;
    if (this._deferAction('说完这段后继续', () => this.advance())) return true;
    if (this._state.stage === 'intro') {
      this._enterFortune();
      return true;
    }
    if (this._state.stage === 'fortune') {
      const phase = this._state.fortune?.phase;
      if (phase === 'inviting' || phase === 'voting') return this.closeVoting();
      if (phase === 'revealing') return this.revealNext();
      if (phase === 'complete') return this._afterFortune();
      return false;
    }
    return this._audienceReadings ? this.drawNext() : false;
  }

  joinDraw(question = '') {
    if (!this._audienceReadings) return false;
    if (this._destroyed) return false;
    if (this._state.stage === 'reading') {
      this._message('这轮的三位朋友已经抽出啦。先陪他们听完，结束后还可以参加下一轮。');
      return false;
    }
    const value = cleanText(question);
    const firstJoin = !this._state.drawJoined;
    this._state.drawJoined = true;
    this._state.drawQuestion = value || this._state.drawQuestion || '想为接下来的生活找到一点方向，也想更好地照顾自己。';
    if (firstJoin) this._state.drawParticipants++;
    if (this._state.stage === 'free') this._enterDraw();
    this._message(firstJoin ? `${SELF}已加入抽选：${this._state.drawQuestion}` : `抽选问题已更新：${this._state.drawQuestion}`, { mine: true });
    if (firstJoin) this._say('报名收到，问题记下啦！等会儿一起抽。', 'happy', 'nod', { priority: 7, scope: 'reaction', duration: 6800 });
    this._emitState();
    return true;
  }

  drawNext() {
    if (!this._audienceReadings) return false;
    if (this._destroyed || this._state.paused) return false;
    const stage = this._state.stage;
    if (this._deferAction('说完这段后继续抽选', () => this.drawNext())) return true;
    if (stage === 'fortune') return this.advance();
    if (stage === 'intro' || stage === 'free') {
      if (stage === 'free') this._state.drawJoined = false;
      this._enterDraw();
      return true;
    }
    if (stage === 'reading') {
      if (!this._state.currentCard) return this.selectCard(randomBetween(0, 2));
      this._advanceReading();
      return true;
    }
    if (stage !== 'draw') return false;
    const pool = VIEWERS.map(viewer => ({ name: viewer.name, question: viewer.question }));
    for (let index = pool.length - 1; index > 0; index--) {
      const other = randomBetween(0, index);
      [pool[index], pool[other]] = [pool[other], pool[index]];
    }
    const selected = pool.slice(0, this._state.drawJoined ? 2 : 3);
    if (this._state.drawJoined) selected.splice(randomBetween(0, 2), 0, { name: SELF, question: this._state.drawQuestion, mine: true });
    this._state.selectedViewers = selected;
    this._state.round = 1;
    this._event('effect', { type: 'draw', icon: '✦', label: '今晚的三位幸运观众已诞生' });
    this._message(`月光名单：${selected.map(viewer => viewer.name).join('、')}。每个人都会得到一次认真解读。`);
    this._enterReading();
    return true;
  }

  _enterReading() {
    if (!this._audienceReadings) return false;
    if (this._deferAction('说完这段后看下一位', () => this._enterReading())) return true;
    this._setStage('reading');
    const viewer = this._state.selectedViewers[this._state.round - 1];
    if (!viewer) return this._enterFree();
    Object.assign(this._state, {
      currentViewer: { ...viewer }, currentCard: null, selectedCardIndex: null, waitingForCard: true,
      stageLabel: `专属占卜 · ${this._state.round} / 3 · ${viewer.name}`,
    });
    this._say(`第${['一', '二', '三'][this._state.round - 1]}位，${viewer.name}。来，想着你刚才的问题，选一张吧。`, 'serious', 'draw', {
      duration: 10300,
      onComplete: () => { if (!this._state.currentCard) this._nextCardAt = this._clock + 5200; },
    });
    this._emitState();
  }

  selectCard(index) {
    if (!this._audienceReadings) return false;
    if (this._destroyed || this._state.paused || !Number.isInteger(index) || index < 0 || index > 2) return false;
    if (this._state.stage !== 'reading' || this._state.currentCard) return false;
    if (this._deferAction('说完这段后解读选牌', () => this.selectCard(index))) return true;
    const viewer = this._state.selectedViewers[this._state.round - 1];
    let cardIndex = (hash(viewer.question) + index + this._state.round) % CARDS.length;
    if (cardIndex === this._lastCardIndex) cardIndex = (cardIndex + 1) % CARDS.length;
    this._lastCardIndex = cardIndex;
    const card = { ...CARDS[cardIndex] };
    this._nextCardAt = Infinity;
    this._state.currentCard = card;
    this._state.selectedCardIndex = index;
    this._state.waitingForCard = false;
    viewer.card = { ...card };
    const reading = personalReading(viewer.question, card);
    viewer.reading = {
      question: viewer.question,
      interpretation: `「${card.title}」正位：${reading.context}${reading.obstacle}`,
      advice: `${reading.action}${reading.detail}`, source: 'local', status: 'ready',
    };
    this._state.currentViewer = { ...viewer, card: { ...card }, reading: { ...viewer.reading } };
    this._event('effect', { type: 'draw', icon: card.symbol, label: `${viewer.name} · ${card.title} 正位` });
    this._invalidateTextRequests();
    this._clearPhaseSpeech();
    if (viewer.mine && this._textGenerator) {
      viewer.reading.source = 'pending';
      viewer.reading.status = 'pending';
      this._state.currentViewer = { ...viewer, card: { ...card }, reading: { ...viewer.reading } };
      const history = this._history.map(item => ({ ...item }));
      this._rememberText('user', viewer.question);
      this._say(`你问${quote(viewer.question)}。抽到的是${card.title}，正位。我来结合你的情况看看。`, 'serious', 'draw', { priority: 9, source: 'local' });
      this._generateText({ kind: 'reading', message: viewer.question, viewer, history });
      this._emitState();
      return true;
    }
    this._say(`你问${quote(viewer.question)}。抽到的是${card.title}，正位。${reading.context}${reading.obstacle}${viewer.reading.advice}${viewer.mine ? '哪一点最贴近你现在的情况？也可以接着告诉我。' : '先试一步，之后再看实际的变化。'}`, 'serious', 'explain', {
      duration: 10900, priority: 9, onComplete: () => { this._nextReadingAt = this._clock + 3800; },
    });
    this._emitState();
    return true;
  }

  _advanceReading() {
    if (!this._audienceReadings) return false;
    if (this._state.stage !== 'reading') return false;
    if (this._deferAction('说完这段后看下一位', () => this._advanceReading())) return true;
    if (this._state.round >= 3) this._enterFree();
    else {
      this._state.round++;
      this._enterReading();
    }
  }

  _enterFree() {
    if (this._deferAction('说完这段后自由聊天', () => this._enterFree())) return true;
    this._setStage('free');
    this._state.waitingForCard = false;
    this._state.drawJoined = false;
    this._say(this._audienceReadings
      ? '三个人都聊完了。谢谢你们愿意说说自己的事，刚才的建议挑一条试试就好，不用一口气全做到。'
      : '牌先收好，来信也会留着。想看随时翻。', 'warm', 'nod', { duration: 10500 });
    this._say(this._audienceReadings
      ? '接下来随便聊聊。还想问什么，或者想听小岛上的事，都可以。再抽一轮也行，我还不急着收摊。'
      : '接下来聊你的。今天有没有什么想跟我说的？', 'happy', 'wave', { duration: 8500 });
    this._emitState();
  }

  setPaused(value) {
    if (this._destroyed) return;
    this._state.paused = Boolean(value);
    this._lastTick = performance.now();
    this._emitState();
    if (!this._state.paused) {
      for (const request of [...this._textPending.values()]) if (request.ready) this._applyGeneratedText(request);
      if (this._active?.complete) this._finishSpeech();
      this._pumpSpeech();
    }
  }

  replay() {
    if (this._destroyed) return false;
    if (this._timer !== null) clearInterval(this._timer);
    this._invalidateTextRequests();
    this._history = [];
    this._storyEvents.clear();
    this._lotteryOpenedAt = null;
    const saved = { followed: this._state.followed };
    this._state = this._initialState(saved);
    this._started = false;
    this._timer = null;
    this._clock = 0;
    this._phase++;
    this._queue = [];
    this._continuations = [];
    this._pendingAction = null;
    this._chatCandidates = [];
    this._selectedPlayer = null;
    this._recentPlayerMessages.clear();
    this._replyBurst = 0;
    this._active = null;
    this._jobs = [];
    this._nextChatAt = 1400;
    this._nextViewerAt = 2800;
    this._nextDrawAt = Infinity;
    this._nextCardAt = Infinity;
    this._nextReadingAt = Infinity;
    this._lastMessageAt = -Infinity;
    this._lastLikeEffectAt = -Infinity;
    this._lastCardIndex = -1;
    this._seedCount = 0;
    this._fortuneVotes.clear();
    this._fortuneVotingEndsAt = Infinity;
    this._fortuneNextVoteAt = Infinity;
    this.start();
    return true;
  }

  destroy() {
    this._invalidateTextRequests();
    if (this._timer !== null) clearInterval(this._timer);
    this._timer = null;
    this._started = false;
    this._destroyed = true;
    this._queue = [];
    this._continuations = [];
    this._pendingAction = null;
    this._chatCandidates = [];
    this._selectedPlayer = null;
    this._recentPlayerMessages.clear();
    this._replyBurst = 0;
    this._active = null;
    this._jobs = [];
  }
}
