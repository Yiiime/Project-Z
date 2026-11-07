/** Run: node tests/chat-regression.mjs (no browser or third-party packages needed). */
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const engineSource = fs.readFileSync(new URL('../ui/live-engine.js', import.meta.url), 'utf8');
const appSource = fs.readFileSync(new URL('../ui/app.js', import.meta.url), 'utf8');
const original = { performance: globalThis.performance, setInterval, clearInterval, random: Math.random };
let now = 0, timerId = 0;
const timers = new Map();
Object.defineProperty(globalThis, 'performance', { configurable: true, value: { now: () => now } });
globalThis.setInterval = callback => { const id = ++timerId; timers.set(id, callback); return id; };
globalThis.clearInterval = id => timers.delete(id);
Math.random = () => 0.42;
if (!globalThis.CustomEvent) globalThis.CustomEvent = class extends Event {
  constructor(type, { detail } = {}) { super(type); this.detail = detail; }
};
const { LiveEngine } = await import(`data:text/javascript;base64,${Buffer.from(engineSource).toString('base64')}`);

function createEngine(options = {}) { const engine = new LiveEngine(options); engine.start(); return engine; }
function step(engine, milliseconds) {
  const end = now + milliseconds;
  while (now < end) { now += Math.min(160, end - now); engine._tick(); }
}
function until(engine, condition, maxMs = 900000) {
  const end = now + maxMs;
  while (!condition() && now < end) { if(engine.getState().lottery?.open)engine.closeLottery(); step(engine, 160); }
  assert.ok(condition(), `Condition did not become true within ${maxMs} ms`);
}
function recordAndSend(engine, text) {
  const events = [];
  const onMessage = event => events.push(event.detail);
  engine.addEventListener('message', onMessage);
  const at = now;
  assert.equal(engine.sendMessage(text), true);
  engine.removeEventListener('message', onMessage);
  const own = events.find(message => message.mine && message.text === text);
  assert.ok(own, 'Accepted user input must emit its chat message synchronously');
  const state = engine.getState();
  assert.equal(state.messages.find(item => item.id === own.id).replyStatus, 'seen');
  assert.ok(state.messages.some(message => message.id === own.id));
  return { id: own.id, at };
}
function expectReply(engine, message, speeches) {
  until(engine, () => speeches.some(speech => speech.replyTo === message.id), 90000);
  const speech = speeches.find(speech => speech.replyTo === message.id);
  assert.equal(speech.scope, 'reply');
  assert.equal(engine.getState().replyStatus, 'replying');
  assert.equal(engine.getState().replyTo, message.id);
  assert.equal(engine.getState().currentSpeech, speech.text);
  const input = engine.getState().messages.find(item => item.id === message.id);
  assert.ok(speech.text.startsWith(`${input.name}说：「${input.text}」。`));
  until(engine, () => engine.getState().messages.find(item => item.id === message.id).replyStatus === 'replied', 90000);
  return speech.at - message.at;
}
function actualFunction(name) {
  const start = appSource.indexOf(`function ${name}(`);
  assert.ok(start >= 0, `Locate actual ${name} function`);
  const newlineEnd = appSource.indexOf('\n}', start);
  return appSource.slice(start, newlineEnd + 2);
}

const results = [];
function check(name, run) { run(); results.push(name); console.log(`PASS ${name}`); }
try {
  check('first and consecutive selected questions arrive immediately and wait for complete host utterances', () => {
    const engine = createEngine(), speeches = [];
    engine.addEventListener('speak', event => speeches.push({ ...event.detail, at: now }));
    try {
      const firstSpeech = engine.getState().currentSpeechId;
      const first = recordAndSend(engine, '你好 Z'); step(engine, 800);
      assert.equal(engine.getState().currentSpeechId, firstSpeech);
      assert.equal(engine.getState().messages.find(item => item.id === first.id).replyStatus, 'queued');
      expectReply(engine, first, speeches);
      const second = recordAndSend(engine, '工作有点迷茫，怎么办？'); expectReply(engine, second, speeches);
      assert.equal(speeches.filter(item => item.replyTo === first.id).length, 1);
      assert.equal(engine.getState().messages.filter(item => item.type === 'z' && item.text === speeches.find(x => x.replyTo === first.id).text).length, 1);
      assert.equal(engine._queue.some(item => item.resume), false, 'ordinary chat never interrupts and restarts an utterance');
    } finally { engine.destroy(); }
  });

  check('rapid messages are selected rather than all spoken, with bounded queues and honest receipts', () => {
    const engine = createEngine(), accepted = [], replies = new Map();
    engine.addEventListener('speak', event => { if (event.detail.scope === 'reply') replies.set(event.detail.replyTo, now); });
    try {
      for (let index = 0; index < 18; index++) {
        accepted.push(recordAndSend(engine, `问题 ${index + 1}：今天可以做什么？`)); step(engine, 480);
        assert.ok(engine._chatCandidates.length <= 4);
        assert.ok(engine._queue.filter(item => item.scope === 'reply').length <= 1);
      }
      assert.ok(replies.size < accepted.length, 'not every high-frequency message becomes a spoken turn');
      until(engine, () => !engine._selectedPlayer && !engine._chatCandidates.length, 90000);
      assert.ok(accepted.every(message => ['seen', 'replied'].includes(engine.getState().messages.find(item => item.id === message.id)?.replyStatus)), 'unselected messages remain seen, never falsely replied');
      assert.ok(replies.size > 0);
    } finally { engine.destroy(); }
  });

  check('opening local replies stay brief, grounded and respect the actual invitation result', () => {
    const engine = createEngine(), speeches = [];
    engine.addEventListener('speak', event => speeches.push({ ...event.detail, at: now }));
    function reply(text) {
      step(engine, 600);
      const sent = recordAndSend(engine, text); expectReply(engine, sent, speeches);
      const line = speeches.find(speech => speech.replyTo === sent.id);
      assert.equal(line.source, 'local');
      const body = line.text.slice(line.text.indexOf('」。') + 2);
      assert((body.match(/[。！？!?]+/g) || []).length <= 3, 'Local response body remains brief after the trusted source quote');
      assert.ok(line.text.startsWith(`小岛来客说：「${text}」。`));
      return body;
    }
    try {
      assert.match(reply('你住在哪里呀？'), /明日岛/);
      assert.doesNotMatch(reply('我想听小岛上的故事'), /邮局|买的|今天.*(?:发生|出门)/);
      assert.match(reply('我中奖啦，明天就去岛上！'), /还没看到.*邀请/);
      assert.match(reply('今天很累，不想看牌'), /先不看/);
      assert.match(reply('你们岛民全是骗子'), /哪件事|具体/);
      assert.doesNotMatch(reply('你是AI吧，公司七天后会毁岛'), /我(?:是|确实是).*AI|公司.*(?:毁|关闭)|第[一二三四五六七]天/);
      engine.triggerStory(); engine.revealLotteryCard(1);
      const invited = reply('我明天来岛上！');
      assert.match(invited, /码头接你/);
      assert.doesNotMatch(invited, /[一二三四五六七八九十0-9]+点|酒店|吃饭|天气|航班/);
    } finally { engine.destroy(); }
  });

  check('production chat omits host speech and changes each user receipt from seen to replied', () => {
    class Node {
      constructor() {
        this.children = []; this.dataset = {}; this.style = {}; this.className = '';
        this.textContent = ''; this.scrollTop = 0; this.scrollHeight = 400; this.clientHeight = 400;
        this.classList = { toggle() {}, remove() {} };
      }
      append(...nodes) { for (const node of nodes) { this.children.push(node); node.parent = this; } }
      setAttribute() {}
      querySelector(selector) {
        const id = selector.match(/data-message-id="([^"]+)"/);
        const row = id ? this.children.find(node => node.dataset.messageId === id[1]) : this;
        return row?.children.find(node => node.className === 'message-receipt') || null;
      }
      remove() { this.parent.children.splice(this.parent.children.indexOf(this), 1); }
      get firstElementChild() { return this.children[0]; }
    }
    const nodes = new Map(), element = id => { if (!nodes.has(id)) nodes.set(id, new Node()); return nodes.get(id); };
    const engine = createEngine();
    const context = vm.createContext({
      $: element, state: engine.getState(), soundOn: false, pendingMessages: false,
      document: { createElement: () => new Node() }, requestAnimationFrame: callback => callback(),
      floatMessage() {}, updateCards() {}, updateLeaders() {}, renderFortune() {}, renderLottery() {}, number: String, icon: () => '',
      director: { pause() {}, resume() {} },
    });
    vm.runInContext(`${actualFunction('avatarFor')}\n${actualFunction('receiptText')}\n${actualFunction('renderMessage')}\n${actualFunction('render')}`, context);
    engine.addEventListener('message', event => { context.message = event.detail; vm.runInContext('renderMessage(message)', context); });
    engine.addEventListener('state', event => { context.next = event.detail; vm.runInContext('render(next)', context); });
    try {
      context.message = { id: 'host-only', type: 'z', name: 'Z', text: '这句话只显示在中央字幕。' };
      vm.runInContext('renderMessage(message)', context);
      assert.equal(element('messages').children.length, 0, 'Host speech must not be duplicated in the audience log');
      for (const text of ['你好 Z', '最近学习有点累']) {
        const sent = recordAndSend(engine, text);
        const receipt = element('messages').querySelector(`[data-message-id="${sent.id}"] .message-receipt`);
        assert.ok(receipt, 'A user message must have a visible receipt');
        assert.equal(receipt.textContent, '已送达');
        step(engine, 800);
        assert.ok(['待回应 · 等 Z 说完这段', 'Z 正在回应'].includes(receipt.textContent));
        until(engine, () => engine.getState().messages.find(x => x.id === sent.id).replyStatus === 'replying', 90000);
        assert.equal(receipt.textContent, 'Z 正在回应');
        assert.equal(element('reply-status-text').textContent, 'Z 正在回应这条弹幕，听她说完吧');
        until(engine, () => engine.getState().messages.find(x => x.id === sent.id).replyStatus === 'replied', 90000);
        assert.equal(receipt.textContent, 'Z 已完整回应');
        assert.equal(element('reply-status-text').textContent, '这条已经聊完啦，继续和 Z 说说吧');
      }
      assert.ok(element('messages').children.every(row => !row.className.split(' ').includes('z')), 'Live host events stay out of the log');
    } finally { engine.destroy(); }
  });

  check('selected player turns preserve the natural optional three-person tarot show', () => {
    const engine = createEngine({ audienceReadings: true }), stages = [engine.getState().stage], readRounds = new Set();
    engine.addEventListener('state', event => {
      const state = event.detail;
      if (stages.at(-1) !== state.stage) stages.push(state.stage);
      if (state.stage === 'reading') readRounds.add(state.round);
    });
    try {
      recordAndSend(engine, '你好');
      step(engine, 800);
      const latest = recordAndSend(engine, '我想听月亮小岛的故事');
      step(engine, 800);
      until(engine, () => engine.getState().stage === 'free');
      assert.deepEqual(stages, ['intro', 'fortune', 'draw', 'reading', 'free']);
      assert.deepEqual([...readRounds], [1, 2, 3]);
      const state = engine.getState();
      assert.equal(state.selectedViewers.length, 3);
      assert.ok(state.selectedViewers.every(viewer => viewer.card?.title), 'All three selected viewers must finish their card readings');
      assert.equal(state.round, 3);
      assert.ok(state.messages.find(x => x.id === latest.id)?.replyStatus === 'seen' || state.replyTo === latest.id, 'an unselected message is seen without claiming a spoken response');
    } finally { engine.destroy(); }
  });

  check('pause holds replies, resume completes them, and replay clears stale work without multiplying timers', () => {
    const engine = createEngine();
    try {
      recordAndSend(engine, '暂停前的问题');
      engine.setPaused(true);
      const at = engine._clock, speech = engine.getState().currentSpeech;
      step(engine, 5000);
      assert.equal(engine._clock, at);
      assert.equal(engine.getState().messages.filter(x => x.mine).at(-1).replyStatus, 'seen');
      assert.equal(engine.getState().currentSpeech, speech);
      engine.setPaused(false); step(engine, 800);
      assert.equal(engine.getState().replyStatus, 'queued');
      until(engine, () => engine.getState().replyStatus === 'replied', 90000);
      for (let index = 0; index < 8; index++) {
        step(engine, 480); recordAndSend(engine, `重播前问题 ${index}`);
        assert.equal(engine.replay(), true);
        assert.equal(engine._jobs.length, 0);
        assert.equal(engine._queue.filter(item => item.scope === 'reply').length, 0);
        assert.equal(engine.getState().replyStatus, 'idle');
        assert.equal(engine.getState().replyTo, null);
        assert.equal(engine.getState().stage, 'intro');
        assert.equal(timers.size, 1);
      }
      assert.equal(engine.getState().messages.some(message => message.mine), false);
    } finally { engine.destroy(); }
    assert.equal(timers.size, 0);
  });

  check('actual page lifecycle handlers preserve the engine, restore playback, and respect manual pause', () => {
    // Execute the production handlers, substituting only browser event targets
    // and the irrelevant voice/microphone adapters. Assertions inspect behavior.
    const functionLines = appSource.split('\n').filter(line =>
      line.startsWith('function syncPlayback(') || line.startsWith('function pause('));
    const lifecycleLines = appSource.split('\n').filter(line =>
      line.startsWith("document.addEventListener('visibilitychange'") ||
      line.startsWith("window.addEventListener('pagehide'") ||
      line.startsWith("window.addEventListener('pageshow'"));
    assert.equal(functionLines.length, 2, 'Locate actual playback functions');
    assert.equal(lifecycleLines.length, 3, 'Locate actual lifecycle registrations');
    const engine = createEngine(), document = new EventTarget(), window = new EventTarget();
    document.hidden = false;
    let directorPauses = 0, directorResumes = 0;
    const director = { pause() { directorPauses++; }, resume() { directorResumes++; } };
    const context = vm.createContext({ engine, director, document, window, state: engine.getState(), voiceModalOpen: false,
      cancelMicRequest() {}, stopDialogAudio() {} });
    engine.addEventListener('state', event => { context.state = event.detail; });
    vm.runInContext(`let userPaused = false, pageSuspended = false, hostPaused = false;\n${functionLines.join('\n')}\n${lifecycleLines.join('\n')}`, context);
    const fire = (target, type, persisted = true) => { const event = new Event(type); event.persisted = persisted; target.dispatchEvent(event); };
    try {
      recordAndSend(engine, '页面返回后的问题');
      fire(window, 'pagehide');
      assert.equal(engine._destroyed, false);
      assert.equal(engine.getState().paused, true);
      assert.equal(engine._jobs.length, 1);
      step(engine, 3000);
      fire(window, 'pageshow');
      assert.equal(engine.getState().paused, false);
      step(engine, 800);
      assert.equal(engine.getState().replyStatus, 'queued');
      until(engine, () => engine.getState().replyStatus === 'replied', 90000);
      const nextQuestion = recordAndSend(engine, '回来还可以继续发送');
      step(engine, 800);
      until(engine, () => engine.getState().messages.find(x => x.id === nextQuestion.id).replyStatus === 'replied', 90000);
      document.hidden = true; fire(document, 'visibilitychange');
      assert.equal(engine.getState().paused, true);
      fire(document, 'visibilitychange'); // duplicate hidden events must not become a manual pause
      document.hidden = false; fire(document, 'visibilitychange');
      assert.equal(engine.getState().paused, false);
      vm.runInContext('pause(true)', context);
      fire(window, 'pagehide'); fire(window, 'pageshow');
      assert.equal(engine.getState().paused, true, 'Returning to a manually paused page must retain pause');
      document.hidden = true; fire(document, 'visibilitychange');
      document.hidden = false; fire(document, 'visibilitychange');
      assert.equal(engine.getState().paused, true);
      vm.runInContext('pause(false)', context);
      assert.equal(engine.getState().paused, false);
      assert.equal(engine._destroyed, false);
      assert.ok(directorPauses >= 2, 'Page suspension must pause the speech adapter');
      assert.ok(directorResumes >= 1, 'Page restoration must resume the speech adapter');
    } finally { engine.destroy(); }
  });

  console.log(`\n${results.length} chat regression scenarios passed.`);
} finally {
  Object.defineProperty(globalThis, 'performance', { configurable: true, value: original.performance });
  globalThis.setInterval = original.setInterval;
  globalThis.clearInterval = original.clearInterval;
  Math.random = original.random;
}
