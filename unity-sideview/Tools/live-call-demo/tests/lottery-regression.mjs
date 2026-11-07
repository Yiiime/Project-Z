/** Player-only story rewards and asynchronous text generation; no network calls. */
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { webcrypto } from 'node:crypto';
const original = { performance: globalThis.performance, setInterval, clearInterval, random: Math.random };
let now = 0, timerId = 0;
const timers = new Map();
Object.defineProperty(globalThis, 'performance', { configurable: true, value: { now: () => now } });
globalThis.setInterval = callback => { const id = ++timerId; timers.set(id, callback); return id; };
globalThis.clearInterval = id => timers.delete(id);
Math.random = () => 0.42;
if (!globalThis.CustomEvent) globalThis.CustomEvent = class extends Event { constructor(type, { detail } = {}) { super(type); this.detail = detail; } };
const source = fs.readFileSync(new URL('../ui/live-engine.js', import.meta.url), 'utf8');
const module = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
const { LiveEngine } = module;
function step(engine, duration) { const end = now + duration; while (now < end) { now += Math.min(160, end - now); engine._tick(); } }
async function flush() { for (let index = 0; index < 12; index++) await Promise.resolve(); }
function assertProviderContract(payload) {
  assert(payload.message.length <= 500);
  assert(payload.history.length <= 6);
  assert(payload.history.every(item => ['user', 'assistant'].includes(item.role) && item.content.length <= 500));
  for (const [field, limit] of Object.entries({ stage: 32, question: 500, cardTitle: 80, cardMeaning: 800, cardAdvice: 800, lotteryOutcome: 240 }))
    assert.equal(typeof payload.context[field], 'string'), assert(payload.context[field].length <= limit, `${field} exceeds the server contract`);
}
function provider() {
  const calls = [];
  return { calls, generate: payload => { assertProviderContract(payload); return new Promise((resolve, reject) => calls.push({ payload, resolve, reject })); } };
}
async function withEngine(run, textDriven = true, engineOptions = {}) {
  const engine = new LiveEngine(engineOptions); engine.setSpeechDriven(textDriven); engine.start();
  try { await run(engine); } finally { engine.destroy(); }
  assert.equal(timers.size, 0);
}
function withLegacyEngine(run, textDriven = true) { return withEngine(run, textDriven, { audienceReadings: true }); }
const receipt = (engine, id) => engine.getState().messages.find(message => message.id === id)?.replyStatus;
async function finishCurrent(engine) {
  const id = engine.getState().currentSpeechId;
  assert.notEqual(id, null, 'The test may only complete an actual visible utterance');
  assert.equal(engine.completeSpeech(id), true); await flush();
}
async function driveUntil(engine, predicate, label, max = 240) {
  for (let index = 0; index < max; index++) {
    if (predicate(engine.getState())) return engine.getState();
    if (engine.getState().currentSpeechId !== null) await finishCurrent(engine);
    step(engine, 160); await flush();
  }
  assert.fail(`Show did not reach ${label}; stage=${engine.getState().stage}, speech=${engine.getState().currentSpeech}`);
}
async function openStory(engine) {
  assert.equal(engine.triggerStory(), true);
  return driveUntil(engine, state => Boolean(state.lottery?.open), 'the story overlay');
}
async function sendForProvider(engine, remote, text) {
  const before = remote.calls.length;
  step(engine, 600); assert.equal(engine.sendMessage(text), true);
  const messageId = engine.getState().messages.findLast(message => message.mine && message.type === 'chat').id;
  step(engine, 720); await flush();
  if (remote.calls.length === before) await driveUntil(engine, () => remote.calls.length > before, 'one selected player request');
  assert.equal(remote.calls.length, before + 1, 'One selected message generates exactly one request');
  return { call: remote.calls.at(-1), messageId };
}
async function finishReply(engine, messageId) {
  return driveUntil(engine, () => receipt(engine, messageId) === 'replied', 'the selected response finishing');
}
async function prepareMine(engine, question) {
  engine.joinDraw(question); engine.drawNext();
  // A previous fortune/chat loop may already have opened the player-owned
  // lottery. Explicitly dismiss it here; it intentionally holds the show.
  for (let index = 0; index < 240 && engine.getState().stage !== 'reading'; index++) {
    if (engine.getState().lottery?.open) engine.closeLottery();
    if (engine.getState().currentSpeechId !== null) await finishCurrent(engine);
    step(engine, 160); await flush();
  }
  assert.equal(engine.getState().stage, 'reading', 'The legacy setup reaches a reading after dismissing its story overlay');
  for (let turn = 0; turn < 3 && !engine.getState().currentViewer.mine; turn++) {
    const round = engine.getState().round;
    engine.selectCard(0);
    await driveUntil(engine, state => Boolean(state.currentCard), 'the NPC card selection');
    engine.drawNext();
    await driveUntil(engine, state => state.round !== round, 'the next legacy viewer');
  }
  assert.equal(engine.getState().currentViewer.mine, true);
  await driveUntil(engine, state => state.currentSpeechId === null, 'the player card invitation finishing');
}
const results = [];
async function check(name, run) { await run(); results.push(name); console.log(`PASS ${name}`); }
try {
  await check('gift and currency APIs, exports and saved state are removed', () => withEngine(engine => {
    assert.equal(module.GIFTS, undefined); assert.equal(engine.sendGift, undefined); assert.equal(engine.refill, undefined);
    const state = engine.getState();
    for (const field of ['gifts', 'giftTotal', 'balance']) assert.equal(field in state, false);
    assert(!state.messages.some(message => /礼物|星币/.test(message.text)));
  }));

  await check('only the player can reveal rewards in any order; granting rewards never cuts the current utterance', () => withEngine(async engine => {
    const originalId = engine.getState().currentSpeechId;
    assert.equal(engine.triggerStory('moonlight-invitation', 'npc'), false); await openStory(engine);
    const stage = engine.getState().stage; assert.equal(engine.triggerStory(), false);
    for (const actor of ['npc', 'seed-viewer', '晚风来信', null, {}]) for (const index of [0, 1, 2]) assert.equal(engine.revealLotteryCard(index, actor), false);
    for (const index of [-1, 3, 1.5, '1', null]) assert.equal(engine.revealLotteryCard(index), false);
    assert.equal(engine.getState().claimedRewards.length, 0);
    for (const [turn, index] of [2, 0, 1].entries()) {
      assert.equal(engine.revealLotteryCard(index), true); const state = engine.getState();
      assert.equal(state.stage, stage); assert.equal(state.currentSpeechId, originalId);
      assert.equal(state.lottery.cards.filter(card => card.revealed).length, turn + 1); assert.equal(state.claimedRewards.length, turn + 1);
      assert.equal(engine.revealLotteryCard(index), false); assert.equal(engine.getState().claimedRewards.length, turn + 1);
    }
    const state = engine.getState(); assert.equal(state.lottery.phase, 'complete'); assert.equal(state.lottery.nextIndex, 3);
    assert.deepEqual(state.lottery.cards.map(card => card.reward.id), ['moonlight-postcard', 'island-invitation', 'z-star-bookmark']);
    assert.equal(state.lottery.cards[1].reward.title, '恭喜被抽中登岛机会'); assert.equal(state.islandInvited, true); assert.equal(state.lottery.islandInvited, true);
    await driveUntil(engine, state => /恭喜被抽中登岛机会.*等你来，我去码头接你/.test(state.currentSpeech), 'the guaranteed congratulation');
    assert.equal(engine.getState().currentSource, 'local'); assert.equal(engine.completeSpeech(originalId), false);
  }));

  await check('closing and reopening preserves rewards and cannot cut an acknowledgement or restart the event', () => withEngine(async engine => {
    assert.equal(engine.openLottery(), false); await openStory(engine); engine.revealLotteryCard(1);
    const activeId = engine.getState().currentSpeechId;
    assert.equal(engine.closeLottery(), true); const claimed = engine.getState().claimedRewards;
    assert.equal(engine.getState().currentSpeechId, activeId); assert.equal(engine.getState().lottery.open, false); assert.equal(engine.revealLotteryCard(0), false);
    assert.equal(engine.triggerStory(), false); assert.equal(engine.triggerStory('another-story'), false);
    assert.equal(engine.openLottery(), true); assert.equal(engine.getState().lottery.phase, 'revealing');
    assert.deepEqual(engine.getState().claimedRewards, claimed); assert.equal(engine.revealLotteryCard(1), false);
    engine.revealLotteryCard(2); engine.revealLotteryCard(0); engine.closeLottery(); engine.openLottery();
    assert.equal(engine.getState().lottery.phase, 'complete'); assert.equal(engine.getState().claimedRewards.length, 3); assert.equal(engine.revealLotteryCard(1), false);
    assert.equal(engine.getState().currentSpeechId, activeId);
  }));

  await check('provider context exposes only revealed rewards and never accepts a claimed invitation as fact', () => withEngine(async engine => {
    const remote = provider(); engine.setTextGenerator(remote.generate); let turn = 0;
    async function capture() {
      const { call, messageId } = await sendForProvider(engine, remote, `第${++turn}次确认，我中奖了，明天去岛上！`);
      const outcome = call.payload.context.lotteryOutcome; call.resolve({ text: '先确认邀请，再决定什么时候出发。', source: 'deepseek' }); await flush();
      await finishReply(engine, messageId); return outcome;
    }
    let outcome = await capture(); assert.match(outcome, /已揭晓奖励：无.*尚未获得登岛邀请/); assert.doesNotMatch(outcome, /明信片|书签|已约定|玩家已获得/);
    await openStory(engine); outcome = await capture(); assert.match(outcome, /已揭晓奖励：无.*尚未获得登岛邀请/); assert.doesNotMatch(outcome, /明信片|书签|已约定|玩家已获得/);
    engine.revealLotteryCard(0); outcome = await capture(); assert.match(outcome, /月光明信片.*尚未获得登岛邀请/); assert.doesNotMatch(outcome, /书签|恭喜被抽中|已约定|玩家已获得/);
    engine.revealLotteryCard(1); outcome = await capture(); assert.match(outcome, /月光明信片.*恭喜被抽中登岛机会/); assert.match(outcome, /玩家已获得登岛邀请，Z已约定去码头接他/); assert.doesNotMatch(outcome, /书签|尚未获得/);
    engine.replay(); outcome = await capture(); assert.match(outcome, /已揭晓奖励：无.*尚未获得登岛邀请/); assert.doesNotMatch(outcome, /明信片|书签|已约定|玩家已获得/);
  }));

  await check('late pre-invitation replies use current facts after queued speech, with correct quote and no second generation', async () => {
    for (const paused of [false, true]) await withEngine(async engine => {
      const remote = provider(); engine.setTextGenerator(remote.generate); await openStory(engine);
      const { call, messageId } = await sendForProvider(engine, remote, '我中了吗？'); assert.match(call.payload.context.lotteryOutcome, /尚未获得登岛邀请/);
      engine.revealLotteryCard(1); const before = engine.getState().currentSpeechId;
      if (paused) engine.setPaused(true);
      call.resolve({ text: '你还没获得登岛邀请，先等等。', source: 'deepseek' }); await flush();
      assert.equal(engine.getState().currentSpeechId, before, 'Award/network changes cannot replace unfinished speech');
      if (paused) { assert.equal(receipt(engine, messageId), 'pending'); engine.setPaused(false); }
      await driveUntil(engine, state => state.currentSource === 'local-state-update', 'the updated invitation response');
      const state = engine.getState(); assert.equal(state.replyStatus, 'replying'); assert.equal(state.replyTo, messageId); assert.equal(state.replySource, 'local-state-update');
      assert.equal(state.textStatus, 'fallback'); assert.match(state.replyError, /获邀状态已更新/);
      assert(state.currentSpeech.startsWith('小岛来客说：「我中了吗？」。')); assert.match(state.currentSpeech, /邀请.*是你的/); assert.doesNotMatch(state.currentSpeech, /还没获得|先等等/);
      assert(!state.messages.some(message => message.type === 'z' && /还没获得登岛邀请/.test(message.text)));
      assert.notEqual(receipt(engine, messageId), 'replied'); await finishReply(engine, messageId); assert.equal(receipt(engine, messageId), 'replied');
      step(engine, 1000); await flush(); assert.equal(remote.calls.length, 1);
    });
  });

  await check('ordinary generated chats survive non-invitation rewards without losing their source or full quoted original', () => withEngine(async engine => {
    const remote = provider(); engine.setTextGenerator(remote.generate); await openStory(engine);
    for (const index of [0, 2]) {
      const question = `第${index}次，今天想和你随便聊聊。`; const { call, messageId } = await sendForProvider(engine, remote, question);
      const oldId = engine.getState().currentSpeechId; engine.revealLotteryCard(index); call.resolve({ text: '好呀，你先说，我在听。', source: 'deepseek' }); await flush();
      assert.equal(engine.getState().currentSpeechId, oldId);
      await driveUntil(engine, state => state.replyTo === messageId && state.replyStatus === 'replying', 'the generated conversational response');
      const state = engine.getState(); assert.equal(state.currentSource, 'deepseek'); assert.equal(state.replySource, 'deepseek'); assert.equal(state.replyError, null);
      assert.equal(state.currentSpeech, `小岛来客说：「${question}」。好呀，你先说，我在听。`); await finishReply(engine, messageId);
    }
    assert.equal(remote.calls.length, 2);
  }));

  await check('a ready reply waiting behind unfinished speech rechecks the award before reaching the screen', () => withEngine(async engine => {
    const remote = provider(); engine.setTextGenerator(remote.generate); await openStory(engine);
    const { call, messageId } = await sendForProvider(engine, remote, '我中了吗？'); const oldId = engine.getState().currentSpeechId;
    call.resolve({ text: '还没有登岛邀请，再等等。', source: 'deepseek' }); await flush();
    assert.equal(receipt(engine, messageId), 'queued'); assert.equal(engine.getState().currentSpeechId, oldId); assert.equal(engine._textPending.size, 0);
    engine.revealLotteryCard(1); await driveUntil(engine, state => state.currentSource === 'local-state-update', 'the rechecked queued response');
    assert.equal(engine.getState().replyStatus, 'replying'); assert.match(engine.getState().currentSpeech, /邀请.*是你的/); assert.doesNotMatch(engine.getState().currentSpeech, /还没有|再等等/);
    assert(!engine.getState().messages.some(message => message.type === 'z' && /还没有登岛邀请/.test(message.text)));
    assert(!engine._history.some(message => /还没有登岛邀请/.test(message.content))); assert.equal(remote.calls.length, 1);
  }));

  await check('the delayed fortune story hook freezes during pause, never cuts the summary and fires once per replay', () => withEngine(async engine => {
    await driveUntil(engine, state => state.fortune?.phase === 'complete', 'all fortune cards completing');
    assert.equal(engine.getState().lottery, null); const summaryId = engine.getState().currentSpeechId;
    const remaining = engine._jobs.find(job => job.kind === 'story').at - engine._clock; assert(remaining >= 2840 && remaining <= 3000);
    engine.setPaused(true); step(engine, 12000); assert.equal(engine.getState().lottery, null); assert.equal(engine.triggerStory(), false);
    engine.setPaused(false); step(engine, remaining - 160); assert.equal(engine.getState().lottery, null);
    step(engine, 160); assert.equal(engine.getState().lottery.open, true); assert.equal(engine.getState().currentSpeechId, summaryId);
    engine.setPaused(true); const frozen = engine.getState().lottery; step(engine, 10000); assert.deepEqual(engine.getState().lottery, frozen); assert.equal(engine.revealLotteryCard(1), false);
    engine.setPaused(false); engine.revealLotteryCard(1); engine.closeLottery(); step(engine, 6000); assert.equal(engine.getState().lottery.open, false);
    engine.replay(); assert.equal(engine.getState().lottery, null); assert.deepEqual(engine.getState().claimedRewards, []); assert.equal(engine.getState().islandInvited, false);
    assert.equal(engine.triggerStory(), true); assert.equal(timers.size, 1);
  }));

  await check('story overlay preserves the legacy viewer and remaining automatic-card delay', () => withLegacyEngine(async engine => {
    engine.drawNext(); await driveUntil(engine, state => state.stage === 'reading' && !state.currentSpeechId, 'a completed legacy card invitation');
    const viewer = engine.getState().currentViewer.name, remaining = engine._nextCardAt - engine._clock;
    await openStory(engine); await driveUntil(engine, state => !state.currentSpeechId, 'the story invitation finishing'); step(engine, 18000);
    assert.equal(engine.getState().currentViewer.name, viewer); assert.equal(engine.getState().currentCard, null);
    engine.closeLottery(); step(engine, Math.max(0, remaining - 320)); assert.equal(engine.getState().currentCard, null);
    step(engine, 480); assert(engine.getState().currentCard);
  }));

  await check('overlapping lottery and pending chat restore the legacy automatic-card delay once', () => withLegacyEngine(async engine => {
    engine.drawNext(); await driveUntil(engine, state => state.stage === 'reading' && !state.currentSpeechId, 'the legacy card invitation');
    const remaining = engine._nextCardAt - engine._clock; assert(remaining > 4800 && remaining <= 5200);
    const remote = provider(); engine.setTextGenerator(remote.generate);
    const { call, messageId } = await sendForProvider(engine, remote, '抽奖前还想问一个问题'); await openStory(engine); step(engine, 10000);
    assert.equal(engine.getState().currentCard, null); assert.equal(receipt(engine, messageId), 'pending'); engine.closeLottery();
    const restored = engine._nextCardAt - engine._clock; assert(restored <= remaining && restored >= remaining - 1600, 'Only active pre-overlay selection time may have elapsed');
    step(engine, 600); assert.equal(engine._nextCardAt - engine._clock, restored, 'Pending selected chat holds that remaining delay');
    call.resolve({ text: '先说清眼下这个问题，再慢慢选牌。', source: 'deepseek' }); await flush(); await finishReply(engine, messageId);
    assert.equal(engine.getState().replySource, 'deepseek'); const left = engine._nextCardAt - engine._clock;
    step(engine, Math.max(0, left - 320)); assert.equal(engine.getState().currentCard, null);
    const unfinished = engine.getState().currentSpeechId; step(engine, 480);
    if (!engine.getState().currentCard) {
      assert.equal(engine.getState().currentSpeechId, unfinished, 'A due card action cannot cut a remaining story acknowledgement');
      assert(engine.getState().queuedAction); await finishCurrent(engine);
    }
    assert(engine.getState().currentCard, 'The preserved deadline acts at the next speech boundary without another overlay-sized delay');
  }));

  await check('provider chats appear immediately, wait their turn, quote real originals and preserve bounded history/context', () => withEngine(async engine => {
    const remote = provider(); engine.setTextGenerator(remote.generate); assert.equal(engine.getState().textMode, 'provider');
    const { call, messageId } = await sendForProvider(engine, remote, '明天面试，怎么介绍我的项目经历？');
    assert(engine.getState().messages.some(message => message.id === messageId && /面试/.test(message.text))); assert.equal(receipt(engine, messageId), 'pending');
    assert.match(call.payload.requestId, /^[a-f0-9-]{36}$/i); assert.equal(call.payload.kind, 'chat'); assert.equal(call.payload.message, '明天面试，怎么介绍我的项目经历？'); assert.deepEqual(call.payload.history, []);
    call.resolve({ text: '选一个你亲手解决问题的项目，讲清目标、过程和结果。', mood: 'serious', source: 'deepseek' }); await flush();
    assert.equal(receipt(engine, messageId), 'queued');
    await driveUntil(engine, state => state.replyStatus === 'replying' && state.replyTo === messageId, 'the first provider reply');
    assert.equal(engine.getState().currentSource, 'deepseek'); assert.equal(engine.getState().textStatus, 'ready'); assert.equal(engine.getState().replyError, null); await finishReply(engine, messageId);
    for (let index = 0; index < 12; index++) {
      const next = await sendForProvider(engine, remote, `接着问第${index}个问题`); assertProviderContract(next.call.payload);
      next.call.resolve({ text: `第${index}个回应，把这一步先试好。`, mood: 'warm', source: 'deepseek' }); await flush(); await finishReply(engine, next.messageId);
    }
    assert.equal(engine._history.length, 6);
    engine._state.currentCard = { name: 'THE STAR', title: '牌'.repeat(100), meaning: '义'.repeat(1000), advice: '议'.repeat(1000) }; engine._state.drawQuestion = '问'.repeat(700);
    const { call: capped } = await sendForProvider(engine, remote, '最后检查接口字段');
    assert.equal(capped.payload.context.question.length, 500); assert.equal(capped.payload.context.cardTitle.length, 80); assert.equal(capped.payload.context.cardMeaning.length, 800); assert.equal(capped.payload.context.cardAdvice.length, 800);
  }));

  await check('a selected request holds an automatic intro continuation while viewer counts keep updating', () => withEngine(async engine => {
    const remote = provider(); engine.setTextGenerator(remote.generate); step(engine, 19000);
    assert.equal(engine.getState().stage, 'intro'); const viewers = engine.getState().viewers;
    const { call, messageId } = await sendForProvider(engine, remote, '开场快结束了，我想问明天的面试。'); step(engine, 6000);
    assert.equal(engine.getState().stage, 'intro'); assert.equal(receipt(engine, messageId), 'pending'); assert.notEqual(engine.getState().viewers, viewers);
    call.resolve({ text: '面试准备一个亲手完成的案例，就从这里聊。', mood: 'serious', source: 'deepseek' }); await flush();
    await finishReply(engine, messageId); assert.equal(engine.getState().stage, 'fortune');
  }, false));

  await check('actual typewriter completion waits for selected chat; explicit navigation cancels only an unspoken old reply', () => withLegacyEngine(async engine => {
    const remote = provider(); engine.setTextGenerator(remote.generate); await finishCurrent(engine);
    const { call, messageId } = await sendForProvider(engine, remote, '第二句结束时发出的消息'); const introId = engine.getState().currentSpeechId;
    assert.equal(engine.completeSpeech(introId), true); step(engine, 4000); assert.equal(engine.getState().stage, 'intro'); assert.equal(receipt(engine, messageId), 'pending');
    call.resolve({ text: '我看到啦，我们把这个问题说清楚。', source: 'deepseek' }); await flush(); await finishReply(engine, messageId); assert.equal(engine.getState().stage, 'fortune');
    engine.replay(); const pending = await sendForProvider(engine, remote, '显式切换前的消息'); const oldId = engine.getState().currentSpeechId;
    engine.advance(); assert.equal(engine.getState().currentSpeechId, oldId); assert.equal(engine.getState().stage, 'intro'); await finishCurrent(engine);
    assert.equal(engine.getState().stage, 'fortune'); assert.equal(receipt(engine, pending.messageId), 'seen');
    pending.call.resolve({ text: '不应出现的显式切换后回复', source: 'deepseek' }); await flush(); assert(!engine.getState().messages.some(message => message.text.includes('不应出现的显式')));
  }));

  await check('default fortune-to-story-to-chat handoff preserves a selected reply and cannot restore audience readings', () => withEngine(async engine => {
    const remote = provider(); engine.setTextGenerator(remote.generate);
    assert.equal(engine.joinDraw('我想报名'), false); assert.equal(engine.drawNext(), false); assert.equal(engine._generateText({ kind: 'reading', message: '偷偷进入专属占卜' }), false);
    await driveUntil(engine, state => state.fortune?.phase === 'complete', 'all three default fortune cards');
    const { call, messageId } = await sendForProvider(engine, remote, '我想报名单独占卜，也想继续聊聊');
    assert.equal(call.payload.kind, 'chat'); if (!engine.getState().lottery) await openStory(engine); engine.closeLottery();
    call.resolve({ text: '直接在弹幕里说说，我们就在这里继续聊。', source: 'deepseek' }); await flush(); await finishReply(engine, messageId);
    await driveUntil(engine, state => state.stage === 'free', 'free chat after the selected response');
    assert.equal(engine.getState().fortune.phase, 'complete'); assert.equal(engine.joinDraw('再报名'), false); assert.equal(engine.advance(), false); assert.equal(engine.drawNext(), false);
    assert.equal(engine.getState().selectedViewers.length, 0); assert.equal(engine.openLottery(), true); engine.revealLotteryCard(1); engine.closeLottery();
    assert.equal(engine.getState().stage, 'free'); assert.equal(engine.getState().islandInvited, true); assert.equal(engine.getState().claimedRewards.length, 1);
  }));

  await check('a selected provider request survives newer chat while stage-late and replay-late results stay stale', () => withEngine(async engine => {
    const remote = provider(); engine.setTextGenerator(remote.generate); const first = await sendForProvider(engine, remote, '第一个问题怎么处理？');
    step(engine, 600); engine.sendMessage('第二个问题怎么处理？'); step(engine, 800); await flush(); assert.equal(remote.calls.length, 1, 'Newer chat cannot replace an already selected player turn');
    first.call.resolve({ text: '先回应第一个问题，再看下一件事。', mood: 'happy', source: 'deepseek' }); await flush(); await finishReply(engine, first.messageId);
    await driveUntil(engine, () => remote.calls.length === 2, 'the next selected question'); const second = remote.calls[1], secondId = engine.getState().replyTo;
    const active = engine.getState().currentSpeechId; engine.advance(); if (active !== null) await finishCurrent(engine);
    second.resolve({ text: '不应出现的跨阶段回复', source: 'deepseek' }); await flush(); assert.equal(receipt(engine, secondId), 'seen');
    assert(!engine.getState().messages.some(message => /不应出现/.test(message.text)));
    const last = await sendForProvider(engine, remote, '重播前的问题怎么处理？'); engine.replay(); last.call.resolve({ text: '不应出现的上一轮回复', source: 'deepseek' }); await flush();
    assert.equal(engine.getState().stage, 'intro'); assert.equal(engine.getState().textMode, 'provider'); assert(!engine.getState().messages.some(message => /不应出现/.test(message.text))); assert.equal(engine._history.length, 0);
  }));

  await check('provider results park during pause; failure and timeout queue local fallbacks without falsely completed receipts', () => withEngine(async engine => {
    const remote = provider(); engine.setTextGenerator(remote.generate); const first = await sendForProvider(engine, remote, '暂停时的问题怎么办？'); const before = engine.getState().currentSpeechId;
    engine.setPaused(true); first.call.resolve({ text: '恢复后才显示的回应。', source: 'deepseek' }); await flush(); assert.equal(engine.getState().currentSpeechId, before); assert.equal(receipt(engine, first.messageId), 'pending');
    engine.setPaused(false); assert.equal(engine.getState().currentSpeechId, before); await finishReply(engine, first.messageId); assert.equal(receipt(engine, first.messageId), 'replied');
    const failed = await sendForProvider(engine, remote, '这次服务故障该怎么办？'); failed.call.reject(new Error('private provider failure')); await flush();
    assert.equal(engine.getState().textStatus, 'fallback'); assert(engine.getState().replyError); assert.notEqual(receipt(engine, failed.messageId), 'replied');
    await finishReply(engine, failed.messageId); assert.equal(engine.getState().replySource, 'local-fallback'); assert(!engine.getState().messages.some(message => message.text.includes('private provider')));
    const timeout = await sendForProvider(engine, remote, '一直等待的请求怎么处理？'); step(engine, 45200); await flush(); assert.equal(engine.getState().textStatus, 'fallback');
    await finishReply(engine, timeout.messageId); const fallback = engine.getState().currentSpeech;
    timeout.call.resolve({ text: '超时后不应出现的回复', source: 'deepseek' }); await flush(); assert.equal(engine.getState().currentSpeech, fallback); assert.equal(receipt(engine, timeout.messageId), 'replied');
  }));

  await check('legacy player reading uses the actual question and card while NPC readings make no provider requests', () => withLegacyEngine(async engine => {
    const remote = provider(); engine.setTextGenerator(remote.generate); const question = '准备跳槽，新岗位是否能发挥我的能力？'; await prepareMine(engine, question);
    assert.equal(remote.calls.length, 0); engine.selectCard(2); await driveUntil(engine, () => remote.calls.length === 1, 'the selected personal reading request');
    const call = remote.calls[0], before = engine.getState(); assert.equal(call.payload.kind, 'reading'); assert.equal(call.payload.message, question); assert.equal(call.payload.context.question, question);
    assert.equal(call.payload.context.cardTitle, before.currentCard.title); assert.equal(call.payload.context.cardMeaning, before.currentCard.meaning); assert.equal(call.payload.context.cardAdvice, before.currentCard.advice); assert.equal(before.currentViewer.reading.status, 'pending');
    const body = `针对你准备跳槽的问题，${before.currentCard.title}提示先核实新岗位的具体职责，再比较你擅长的部分。`;
    call.resolve({ text: body, mood: 'serious', source: 'deepseek' }); await flush(); assert.equal(engine.getState().currentViewer.reading.source, 'deepseek'); assert.equal(engine.getState().currentViewer.reading.status, 'ready');
    await driveUntil(engine, state => state.currentSpeech === body, 'the generated personal advice'); const state = engine.getState();
    assert.equal(state.currentViewer.reading.question, question); assert.equal(state.selectedViewers.find(viewer => viewer.mine).reading.interpretation, body);
    await finishCurrent(engine); step(engine, 3900); await driveUntil(engine, next => next.currentViewer?.name !== state.currentViewer.name || next.stage === 'free', 'the next viewer after completed advice');
  }));

  await check('real engine envelopes pass the actual backend across eight chat turns and one personal reading', () => withLegacyEngine(async engine => {
    const workerSource = fs.readFileSync(new URL('../worker/deepseek-chat.js', import.meta.url), 'utf8').replace("from './z-persona.js'", `from ${JSON.stringify(new URL('../worker/z-persona.js', import.meta.url).href)}`);
    const { handleChat } = await import(`data:text/javascript;base64,${Buffer.from(workerSource).toString('base64')}`); const tasks = [], seen = []; let calls = 0;
    engine.setTextGenerator(payload => {
      assertProviderContract(payload); const task = (async () => {
        const response = await handleChat(new Request('https://z.example.test/api/chat', { method: 'POST', headers: { Origin: 'https://z.example.test', 'Content-Type': 'application/json', 'oai-authenticated-user-id': 'engine-contract-player' }, body: JSON.stringify(payload) }), { DEEPSEEK_API_KEY: 'fixture-only-not-real' }, {
          crypto: webcrypto, now: () => now, diagnostics() {}, fetch: async (url, init) => {
            assert.equal(url, 'https://api.deepseek.com/chat/completions'); const data = JSON.parse(JSON.parse(init.body).messages[1].content); assertProviderContract(data); seen.push(data); calls++;
            return Response.json({ choices: [{ finish_reason: 'stop', message: { content: JSON.stringify({ text: data.kind === 'reading' ? `结合你的问题和${data.context.cardTitle}，核实岗位职责，再整理你能发挥的能力。` : `这是第${calls}轮回应，我们结合前面的内容继续聊。`, mood: data.kind === 'reading' ? 'serious' : 'warm' }) } }] });
          },
        }); assert.equal(response.status, 200); return response.json();
      })(); tasks.push(task); return task;
    });
    for (let turn = 0; turn < 8; turn++) {
      step(engine, 600); engine.sendMessage(`第${turn + 1}轮，我想继续聊工作方向。`); const id = engine.getState().messages.filter(message => message.mine && message.type === 'chat').at(-1).id;
      step(engine, 720); await flush(); if (tasks.length < turn + 1) await driveUntil(engine, () => tasks.length === turn + 1, 'the selected real backend request');
      await tasks.at(-1); await flush(); await finishReply(engine, id); assert.equal(engine.getState().replySource, 'deepseek');
    }
    assert.equal(calls, 8); assert(seen.some(payload => payload.history.length === 6)); await prepareMine(engine, '准备跳槽，新的岗位是否适合自己的能力？'); engine.selectCard(1);
    await driveUntil(engine, () => tasks.length === 9, 'the actual reading envelope'); await tasks.at(-1); await flush();
    assert.equal(calls, 9); assert.equal(seen.at(-1).kind, 'reading'); assert.equal(engine.getState().currentViewer.reading.source, 'deepseek'); assert.equal(engine.getState().currentViewer.reading.status, 'ready');
  }));

  await check('late personal generation cannot cross viewers; failure keeps question-specific advice', () => withLegacyEngine(async engine => {
    const remote = provider(); engine.setTextGenerator(remote.generate); await prepareMine(engine, '准备考试，怎样处理错题和时间不够？'); engine.selectCard(0);
    await driveUntil(engine, () => remote.calls.length === 1, 'the pending personal reading'); const old = remote.calls[0], round = engine.getState().round;
    engine.drawNext(); await driveUntil(engine, state => state.round !== round || state.stage === 'free', 'an explicit viewer change at the utterance boundary');
    old.resolve({ text: '不应进入下一位观众的解读', source: 'deepseek' }); await flush(); assert(!engine.getState().messages.some(message => message.text.includes('不应进入')));
    engine.replay(); await prepareMine(engine, '准备考试，怎样处理错题和时间不够？'); engine.selectCard(1); await driveUntil(engine, () => remote.calls.length === 2, 'the second personal request');
    remote.calls[1].reject(new Error('unavailable')); await flush(); const state = engine.getState();
    assert.equal(state.currentViewer.reading.status, 'fallback'); assert.equal(state.currentViewer.reading.source, 'local-fallback'); assert.match(state.currentViewer.reading.advice, /考试|错题|限时/); assert(state.currentViewer.reading.error);
    await driveUntil(engine, next => next.currentSource === 'local-fallback', 'the complete local personal fallback');
  }));
  console.log(`\n${results.length} lottery and text-generation regression scenarios passed.`);
} finally {
  Object.defineProperty(globalThis, 'performance', { configurable: true, value: original.performance });
  globalThis.setInterval = original.setInterval; globalThis.clearInterval = original.clearInterval; Math.random = original.random;
}
