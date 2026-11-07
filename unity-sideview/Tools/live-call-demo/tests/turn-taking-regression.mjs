/** Offline turn-taking invariants using the real LiveEngine. */
import assert from 'node:assert/strict';
import fs from 'node:fs';
const source = fs.readFileSync(new URL('../ui/live-engine.js', import.meta.url), 'utf8');
const originals = { performance: globalThis.performance, setInterval, clearInterval, random: Math.random };
let now = 0;
Object.defineProperty(globalThis, 'performance', { configurable: true, value: { now: () => now } });
globalThis.setInterval = () => 1; globalThis.clearInterval = () => {}; Math.random = () => .42;
if (!globalThis.CustomEvent) globalThis.CustomEvent = class extends Event { constructor(type, { detail } = {}) { super(type); this.detail = detail; } };
const { LiveEngine } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
const engines = [];
function live(options) { const e = new LiveEngine(options); e.setSpeechDriven(true); e.start(); engines.push(e); e._nextChatAt = Infinity; return e; }
function step(e, ms) { const end = now + ms; while (now < end) { now += Math.min(160, end - now); e._tick(); } }
function done(e) { const id = e.getState().currentSpeechId; assert.notEqual(id, null); assert.equal(e.completeSpeech(id), true); return id; }
function send(e, text) { step(e, 500); assert.equal(e.sendMessage(text), true); return e.getState().messages.filter(x => x.mine && x.type === 'chat').at(-1); }
function receipt(e, id) { return e.getState().messages.find(x => x.id === id)?.replyStatus; }
async function flush() { for (let i = 0; i < 8; i++) await Promise.resolve(); }
async function check(name, run) { await run(); console.log(`PASS ${name}`); }
try {
  await check('ready local response waits for actual speech completion, quotes its snapshot, and only then completes its receipt', () => {
    const e = live(), first = e.getState(), spoken = [];
    e.addEventListener('speak', x => spoken.push(x.detail));
    const question = send(e, 'Z，我最近学习压力很大，怎么办？'); step(e, 800);
    assert.equal(e.getState().currentSpeechId, first.currentSpeechId);
    assert.equal(receipt(e, question.id), 'queued');
    step(e, 180000); assert.equal(e.getState().currentSpeechId, first.currentSpeechId, 'elapsed engine time cannot end real audio');
    const old = done(e), reply = e.getState();
    assert.equal(reply.replyStatus, 'replying');
    assert.equal(reply.replyTo, question.id);
    assert.ok(reply.currentSpeech.startsWith(`小岛来客说：「${question.text}」。`));
    assert.equal(e.completeSpeech(old), false);
    done(e); assert.equal(receipt(e, question.id), 'replied');
    assert.equal(e.getState().stage, 'intro');
    assert.equal(spoken.filter(x => x.replyTo === question.id).length, 1);
    assert.equal(spoken.at(-1).scope, 'phase');
  });
  await check('one selected provider request survives newer chat, low-information/repeated/vote messages stay seen', async () => {
    const e = live(), requests = []; let resolve;
    e.setTextGenerator(payload => { requests.push(payload); return new Promise(r => { resolve = r; }); });
    const first = send(e, '我最近面试很紧张，怎么准备？'); step(e, 800); await flush();
    assert.equal(requests.length, 1); assert.equal(receipt(e, first.id), 'pending');
    const low = send(e, '谢谢！'), vote = send(e, '我选A'), duplicate = send(e, first.text);
    assert.equal(receipt(e, low.id), 'seen'); assert.equal(receipt(e, vote.id), 'seen'); assert.equal(receipt(e, duplicate.id), 'seen');
    for (let i = 0; i < 9; i++) send(e, `Z，我的计划第${i}步怎么安排？`);
    assert.ok(e._chatCandidates.length <= 4); assert.equal(requests.length, 1);
    const initial = done(e); assert.equal(e.getState().currentSpeechId, null, 'at a boundary, pending selected request holds continuation');
    resolve({ text: '准备一个亲手解决问题的例子，再问清岗位要求。', source: 'deepseek', mood: 'serious' }); await flush();
    assert.equal(e.getState().replyStatus, 'replying'); assert.equal(e.getState().replyTo, first.id);
    assert.ok(e.getState().currentSpeech.includes(`「${first.text}」`));
    assert.equal(e.completeSpeech(initial), false); done(e);
    assert.equal(e.getState().currentSource, 'local'); assert.equal(e.getState().stage, 'intro');
  });
  await check('manual advance is a single boundary action and cannot skip two cards', () => {
    const e = live(), original = e.getState().currentSpeechId;
    assert.equal(e.advance(), true); assert.equal(e.advance(), true);
    assert.ok(e.getState().queuedAction); assert.equal(e.getState().stage, 'intro');
    assert.equal(e.getState().currentSpeechId, original); done(e);
    assert.equal(e.getState().stage, 'fortune'); assert.equal(e.getState().queuedAction, null);
    e.startVoting(); done(e); assert.equal(e.getState().fortune.phase, 'voting');
    e.closeVoting(); done(e); assert.equal(e.getState().fortune.phase, 'revealing');
    done(e); assert.equal(e.getState().fortune.options.filter(x => x.revealed).length, 1);
    const card = e.getState().currentSpeech;
    assert.match(card, /正位/); assert.ok(card.length > 50, 'meaning and practical advice are a single speech unit');
    e.revealNext(); e.advance(); e.revealNext();
    assert.equal(e.getState().fortune.options.filter(x => x.revealed).length, 1);
    done(e); assert.equal(e.getState().fortune.options.filter(x => x.revealed).length, 2);
  });
  await check('voting deadline locks immediately while a long player response continues uninterrupted', () => {
    const e = live(); e.advance(); done(e); e.startVoting(); done(e); done(e);
    assert.equal(e.getState().fortune.phase, 'voting');
    const msg = send(e, 'Z，今天工作很迷茫，怎么办？'); step(e, 800);
    assert.equal(receipt(e, msg.id), 'replying'); const speaking = e.getState().currentSpeechId;
    step(e, 19000); assert.equal(e.getState().fortune.phase, 'revealing');
    assert.equal(e.getState().currentSpeechId, speaking);
    const tally = e.getState().fortune.totalVotes; assert.equal(e.vote(2), false); assert.equal(e.getState().fortune.totalVotes, tally);
    assert.equal(e.getState().fortune.options.some(x => x.revealed), false);
    done(e); assert.match(e.getState().currentSpeech, /收票/);
  });
  await check('lottery state is immediate while all spoken lines finish, including close/reopen', () => {
    const e = live(), id = e.getState().currentSpeechId;
    assert.equal(e.triggerStory(), true); assert.equal(e.revealLotteryCard(1), true);
    assert.equal(e.getState().islandInvited, true); assert.equal(e.getState().currentSpeechId, id);
    assert.equal(e.closeLottery(), true); assert.equal(e.openLottery(), true);
    assert.equal(e.getState().currentSpeechId, id); done(e);
    assert.match(e.getState().currentSpeech, /恭喜被抽中登岛机会.*码头接你/);
    const awardId = e.getState().currentSpeechId; e.closeLottery(); assert.equal(e.getState().currentSpeechId, awardId);
    done(e); assert.equal(e.completeSpeech(awardId), false);
  });
  await check('award fact refresh keeps the exact original quote without another paid request', async () => {
    const e = live(); let resolve, count = 0;
    e.setTextGenerator(() => { count++; return new Promise(r => { resolve = r; }); });
    const msg = send(e, '我中了登岛邀请吗？'); step(e, 800); await flush();
    e.triggerStory(); e.revealLotteryCard(1);
    resolve({ text: '你还没有获得邀请。', source: 'deepseek' }); await flush();
    done(e); assert.match(e.getState().currentSpeech, /恭喜被抽中/); done(e);
    assert.equal(e.getState().currentSource, 'local-state-update');
    assert.ok(e.getState().currentSpeech.startsWith(`小岛来客说：「${msg.text}」。`));
    assert.doesNotMatch(e.getState().currentSpeech, /还没有获得邀请/); assert.equal(count, 1);
  });
  await check('pause parks actual completion and replay clears all pending requests, actions, and candidate receipts', async () => {
    const e = live(); let resolve;
    e.setTextGenerator(() => new Promise(r => { resolve = r; }));
    send(e, 'Z，我考试前很焦虑怎么办？'); step(e, 800); await flush();
    const original = e.getState().currentSpeechId; e.advance(); e.setPaused(true);
    e.completeSpeech(original); resolve({ text: '先休息一下，再练一道熟悉的题。', source: 'deepseek' }); await flush();
    assert.equal(e.getState().currentSpeechId, original); assert.equal(e.getState().stage, 'intro');
    e.setPaused(false); assert.equal(e.getState().stage, 'fortune');
    assert.equal(e.getState().messages.filter(x => x.mine).at(-1).replyStatus, 'seen', 'explicit stage change cancels an unspoken old response');
    send(e, 'Z，下一个问题怎么做？'); step(e, 800); await flush(); e.advance();
    const stale = e.getState().currentSpeechId; e.replay();
    assert.equal(e.getState().replyStatus, 'idle'); assert.equal(e.getState().queuedAction, null);
    assert.equal(e._chatCandidates.length, 0); assert.equal(e._continuations.length, 0);
    assert.equal(e.completeSpeech(stale), false); assert.equal(e._textPending.size, 0);
  });
  await check('seeded audience responses quote their own source snapshot after the current speech finishes', () => {
    const e = live(); e._enterFree(); done(e);
    const original = e.getState().currentSpeechId;
    e._seedCount = 4; e._seedChat();
    const npc = e.getState().messages.filter(item => item.type === 'chat' && !item.mine).at(-1);
    assert.ok(npc); assert.equal(e.getState().currentSpeechId, original);
    done(e);
    assert.ok(e.getState().currentSpeech.startsWith(`${npc.name}说：「${npc.text}」。`));
  });
  console.log('8 turn-taking scenarios passed.');
} finally {
  for (const e of engines) e.destroy();
  Object.defineProperty(globalThis, 'performance', { configurable: true, value: originals.performance });
  globalThis.setInterval = originals.setInterval; globalThis.clearInterval = originals.clearInterval; Math.random = originals.random;
}
