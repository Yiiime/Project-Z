/** Run: node tests/fortune-regression.mjs (virtual time; no browser dependencies). */
import assert from 'node:assert/strict';
import fs from 'node:fs';

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
const source = fs.readFileSync(new URL('../ui/live-engine.js', import.meta.url), 'utf8');
const { LiveEngine } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);

function step(engine, milliseconds) {
  const end = now + milliseconds;
  while (now < end) { now += Math.min(160, end - now); engine._tick(); }
}
function until(engine, condition, maxMs = 900000) {
  const end = now + maxMs;
  while (!condition() && now < end) step(engine, 160);
  assert.ok(condition(), `Condition did not become true within ${maxMs} ms`);
}
function withEngine(run, speechDriven = false, engineOptions = {}) {
  const engine = new LiveEngine(engineOptions);
  engine.setSpeechDriven(speechDriven); engine.start();
  try { run(engine); } finally { engine.destroy(); }
  assert.equal(timers.size, 0, 'Destroy must leave no live-room interval');
}
function withLegacyEngine(run, textDriven = false) { return withEngine(run, textDriven, { audienceReadings: true }); }
function startVoting(engine) {
  assert.equal(engine.advance(), true);
  completeUntil(engine, () => engine.getState().stage === 'fortune');
  assert.equal(engine.getState().fortune.phase, 'inviting');
  assert.equal(engine.vote(0), false, 'Voting opens after the invitation');
  assert.equal(engine.startVoting(), true);
  completeUntil(engine, () => engine.getState().fortune.phase === 'voting');
  assert.equal(engine.getState().fortune.phase, 'voting');
}
const revealed = engine => engine.getState().fortune.options.filter(option => option.revealed).length;
function completeUntil(engine, condition, maxSpeeches = 60) {
  for (let count = 0; !condition() && count < maxSpeeches; count++) {
    const id = engine.getState().currentSpeechId;
    assert.ok(id, 'The next show transition must have an active speech');
    assert.equal(engine.completeSpeech(id), true);
  }
  assert.ok(condition(), 'Expected speech-driven state was not reached');
}
const results = [];
function check(name, run) { run(); results.push(name); console.log(`PASS ${name}`); }

try {
  check('the default show runs intro, daily guidance, player story and free chat with the fortune result preserved', () => withEngine(engine => {
    const stages = [engine.getState().stage], lines = [engine.getState().currentSpeech], openedEvents = new Set(), cardReadings = new Set();
    engine.addEventListener('state', ({ detail }) => {
      if (stages.at(-1) !== detail.stage) stages.push(detail.stage);
      if (detail.lottery?.open) openedEvents.add(detail.lottery.eventId);
    });
    engine.addEventListener('speak', ({ detail }) => {
      lines.push(detail.text);
      const state = engine.getState();
      if (detail.scope === 'phase' && state.stage === 'fortune' && state.fortune.phase === 'revealing'
        && state.currentCard && detail.text.includes(state.currentCard.meaning)) cardReadings.add(detail.text);
    });
    assert.equal(engine.getState().features.audienceReadings, false);
    until(engine, () => {
      const state = engine.getState();
      if (state.lottery?.open) {
        assert.equal(state.fortune.phase, 'complete');
        for (const index of [2, 0, 1]) assert.equal(engine.revealLotteryCard(index), true);
        engine.closeLottery();
      }
      return engine.getState().stage === 'free';
    });
    const state = engine.getState(), result = JSON.stringify(state.fortune);
    assert.deepEqual(stages, ['intro', 'fortune', 'free']); assert.equal(openedEvents.size, 1);
    assert.equal(state.fortune.phase, 'complete'); assert(state.fortune.options.every(option => option.revealed && option.card));
    assert.equal(state.selectedViewers.length, 0); assert.equal(state.currentViewer, null); assert.equal(state.round, 0);
    assert.equal(state.claimedRewards.length, 3); assert.equal(state.islandInvited, true);
    assert(lines.every(text => !/抽三|三个人|报名|专属占卜|再抽一轮/.test(text)), 'The visible default storyline cannot advertise a disabled segment');
    assert(lines.filter(text => !cardReadings.has(text)).every(text => (text.match(/[。！？!?]+/g) || []).length <= 3), 'Conversational turns stay short; each card meaning and its advice form one complete reading');
    assert.equal(cardReadings.size, 3, 'Each fortune card receives one complete spoken reading');
    for (const { card } of state.fortune.options) {
      const readings = [...cardReadings].filter(text => text.includes(card.meaning));
      assert.equal(readings.length, 1); assert.ok(readings[0].includes(card.title));
      const guidance = readings[0].slice(readings[0].indexOf(card.meaning) + card.meaning.length);
      assert.match(guidance, /今天|挑|找|核实|收个尾/, 'Each full reading contains an actionable suggestion after its meaning');
    }
    assert(lines.every(text => !/明天.*(?:到港|入住)|第[一二三四五六七]天|公司|邮局/.test(text)), 'Opening lines contain neither future plot nor invented daily island events');
    assert.equal(engine.advance(), false); assert.equal(engine.joinDraw('我想报名'), false); assert.equal(engine.drawNext(), false); assert.equal(engine.selectCard(0), false);
    assert.equal(engine.sendMessage('我想报名单独占卜'), true); step(engine, 800);
    assert.equal(engine.getState().stage, 'free'); assert.equal(engine.getState().drawJoined, false);
    assert.equal(engine.openLottery(), true); assert.equal(engine.revealLotteryCard(1), false); engine.closeLottery();
    assert.equal(engine.getState().stage, 'free'); assert.equal(JSON.stringify(engine.getState().fortune), result, 'Reviewing the completed reward event must not reroll or discard daily guidance');
    assert.equal(engine.getState().claimedRewards.length, 3);
    engine.replay(); assert.equal(engine.getState().fortune, undefined); assert.equal(engine.getState().features.audienceReadings, false);
  }));

  check('all default legacy entry points remain disabled before, during and after fortune without skipping the story', () => withEngine(engine => {
    const unavailable = () => {
      const stage = engine.getState().stage;
      for (const action of [() => engine.joinDraw('报名'), () => engine.drawNext(), () => engine.selectCard(0), () => engine._enterDraw(), () => engine._enterReading(), () => engine._advanceReading()]) assert.equal(action(), false);
      assert.equal(engine.getState().stage, stage); assert.equal(engine.getState().selectedViewers.length, 0);
    };
    unavailable(); startVoting(engine); unavailable(); assert.equal(engine.closeVoting(), true);
    completeUntil(engine, () => engine.getState().fortune.phase === 'revealing'); unavailable();
    completeUntil(engine, () => engine.getState().fortune.phase === 'complete');
    assert.equal(engine.getState().fortune.phase, 'complete'); unavailable();
    assert.equal(engine.advance(), true); completeUntil(engine, () => engine.getState().lottery?.open);
    assert.equal(engine.getState().stage, 'fortune');
    assert.equal(engine.advance(), true); assert.equal(engine.getState().claimedRewards.length, 0);
    engine.closeLottery(); completeUntil(engine, () => engine.getState().stage === 'free'); unavailable();
  }, true));

  check('finishing the fortune summary before the delayed hook still opens the story and closes into free chat', () => withEngine(engine => {
    startVoting(engine); assert.equal(engine.closeVoting(), true);
    completeUntil(engine, () => engine.getState().fortune.phase === 'complete');
    assert.equal(engine.getState().lottery, null);
    const summaryId = engine.getState().currentSpeechId;
    engine.completeSpeech(summaryId);
    assert.equal(engine.getState().lottery.open, true, 'The summary callback must introduce the event even before its delayed hook');
    assert.equal(engine.getState().stage, 'fortune');
    engine.closeLottery(); completeUntil(engine, () => engine.getState().stage === 'free');
    assert.equal(engine.getState().fortune.phase, 'complete');
    step(engine, 6000); assert.equal(engine.getState().lottery.open, false, 'The old delayed hook cannot reopen the finished event');
  }, true));

  check('one local viewer has one replaceable vote; simulated totals and percentages remain consistent', () => withEngine(engine => {
    assert.equal(engine.vote(0), false);
    startVoting(engine);
    for (const invalid of [-1, 3, 0.5, '1', null]) assert.equal(engine.vote(invalid), false);
    assert.equal(engine.vote(0), true);
    assert.deepEqual(engine.getState().fortune.options.map(option => option.votes), [1, 0, 0]);
    assert.equal(engine.vote(0), true);
    assert.equal(engine.getState().fortune.totalVotes, 1, 'Repeated clicks must not add extra ballots');
    assert.equal(engine.vote(2), true);
    assert.deepEqual(engine.getState().fortune.options.map(option => option.votes), [0, 0, 1]);
    step(engine, 6000);
    const before = engine.getState().fortune;
    assert.ok(before.totalVotes > 1, 'The live-room audience must contribute cumulative votes');
    assert.equal(before.totalVotes, before.options.reduce((sum, option) => sum + option.votes, 0));
    assert.equal(before.options.reduce((sum, option) => sum + option.percent, 0), 100);
    assert.ok(before.totalVotes <= engine.getState().viewers);
    assert.equal(engine.vote(1), true);
    const after = engine.getState().fortune;
    assert.equal(after.totalVotes, before.totalVotes);
    assert.equal(after.options[2].votes, before.options[2].votes - 1);
    assert.equal(after.options[1].votes, before.options[1].votes + 1);
    assert.equal(after.myVote, 1);
  }));

  check('chat ballots update the same tally and retain immediate chat receipts', () => withEngine(engine => {
    startVoting(engine);
    const messages = [], speeches = [], activeId = engine.getState().currentSpeechId;
    engine.addEventListener('message', event => messages.push(event.detail));
    engine.addEventListener('speak', event => speeches.push(event.detail));
    assert.equal(engine.sendMessage('我选 A'), true);
    const own = messages.find(message => message.mine && message.type === 'chat');
    assert.ok(own, 'A chat ballot must still appear as the user\'s chat message');
    assert.equal(own.replyStatus, 'seen', 'A ballot has an immediate receipt without promising a spoken reply');
    assert.equal(engine.getState().fortune.myVote, 0);
    assert.equal(engine.getState().fortune.totalVotes, 1);
    step(engine, 800);
    assert.equal(engine.getState().currentSpeechId, activeId, 'A ballot cannot interrupt the current announcement');
    assert.equal(speeches.filter(speech => speech.scope === 'reply').length, 0, 'Routine ballots update the poll without reading every vote aloud');
    const before = engine.getState().fortune;
    assert.equal(engine.sendMessage('我选 C'), true);
    const after = engine.getState().fortune;
    assert.equal(after.myVote, 2);
    assert.equal(after.totalVotes, before.totalVotes);
    assert.equal(after.options[0].votes, before.options[0].votes - 1);
    assert.equal(after.options[2].votes, before.options[2].votes + 1);
  }));

  check('pause freezes the poll and locked results reject all later ballots', () => withEngine(engine => {
    startVoting(engine); step(engine, 4200); engine.vote(1);
    engine.setPaused(true);
    const frozen = engine.getState().fortune;
    step(engine, 25000);
    assert.deepEqual(engine.getState().fortune, frozen);
    assert.equal(engine.vote(0), false);
    assert.equal(engine.closeVoting(), false);
    engine.setPaused(false);
    until(engine, () => engine.getState().fortune.phase === 'revealing', 20000);
    const locked = engine.getState().fortune;
    assert.equal(locked.remainingSeconds, 0);
    assert.equal(engine.vote(0), false);
    assert.equal(engine.closeVoting(), false);
    step(engine, 9000);
    assert.deepEqual(engine.getState().fortune.options.map(option => option.votes), locked.options.map(option => option.votes));
    assert.equal(engine.getState().fortune.totalVotes, locked.totalVotes);
    assert.equal(locked.options[locked.winnerIndex].votes, Math.max(...locked.options.map(option => option.votes)));
  }));

  check('the poll locks on its real countdown while a reply keeps the floor through its natural completion', () => withEngine(engine => {
    let latestSpeech;
    engine.addEventListener('speak', ({ detail }) => { latestSpeech = detail; });
    startVoting(engine); engine.vote(2);
    assert.equal(engine.sendMessage('Z，你怎么看我最近工作的选择？'), true); step(engine, 800);
    completeUntil(engine, () => latestSpeech?.scope === 'reply');
    const replyId = engine.getState().currentSpeechId;
    step(engine, 19000);
    const locked = engine.getState().fortune;
    assert.equal(locked.phase, 'revealing'); assert.equal(locked.remainingSeconds, 0);
    assert.equal(engine.getState().currentSpeechId, replyId, 'The countdown must not stop a playing reply');
    assert.equal(revealed(engine), 0, 'The lock announcement waits for the complete reply');
    assert.equal(engine.vote(0), false); assert.equal(engine.closeVoting(), false);
    step(engine, 6000);
    assert.deepEqual(engine.getState().fortune.options.map(option => option.votes), locked.options.map(option => option.votes));
    assert.equal(engine.getState().fortune.totalVotes, locked.totalVotes);
    assert.equal(engine.completeSpeech(replyId), true);
    assert.equal(revealed(engine), 0, 'The lock announcement is spoken before any card is flipped');
    completeUntil(engine, () => revealed(engine) === 1);
  }, true));

  check('manual reveals wait for the complete current turn, coalesce clicks and ignore obsolete completions', () => withLegacyEngine(engine => {
    startVoting(engine); engine.vote(1); engine.closeVoting();
    completeUntil(engine, () => engine.getState().fortune.phase === 'revealing');
    const resultId = engine.getState().currentSpeechId;
    assert.equal(revealed(engine), 0);
    const ids = [resultId];
    for (let index = 0; index < 3; index++) {
      assert.equal(engine.revealNext(), true);
      assert.equal(engine.revealNext(), true, 'Repeated clicks describe the same pending action');
      assert.equal(revealed(engine), index, 'A click never cuts off the current card or announcement');
      assert.equal(engine.getState().currentSpeechId, ids.at(-1));
      assert.ok(engine.getState().queuedAction);
      assert.equal(engine.completeSpeech(ids.at(-1)), true);
      const state = engine.getState();
      assert.equal(revealed(engine), index + 1);
      assert.equal(state.fortune.activeIndex, index);
      assert.ok(state.fortune.options[index].card?.title);
      assert.equal(state.currentCard.title, state.fortune.options[index].card.title);
      assert.equal(engine.completeSpeech(ids.at(-1)), false, 'A completed utterance cannot flip a second card');
      ids.push(state.currentSpeechId);
    }
    assert.equal(new Set(engine.getState().fortune.options.map(option => option.card.title)).size, 3);
    assert.equal(engine.revealNext(), true);
    assert.equal(engine.getState().fortune.phase, 'revealing', 'The last interpretation is still playing');
    assert.equal(engine.completeSpeech(ids.at(-1)), true);
    assert.equal(engine.getState().fortune.phase, 'complete');
    assert.equal(engine.revealNext(), false);
    assert.equal(engine.advance(), true);
    assert.equal(engine.getState().stage, 'fortune', 'Explicit chapter navigation waits for the summary');
    completeUntil(engine, () => engine.getState().stage === 'draw');
    assert.equal(engine.getState().stage, 'draw');
    assert.equal(engine.completeSpeech(ids.at(-1)), false);
  }, true));

  check('director completion holds the entire invitation and combined card reading before moving the show on', () => withEngine(engine => {
    let latestSpeech;
    engine.addEventListener('speak', ({ detail }) => { latestSpeech = detail; });
    engine.advance();
    completeUntil(engine, () => engine.getState().stage === 'fortune');
    const inviteId = engine.getState().currentSpeechId;
    step(engine, 130000);
    assert.equal(engine.getState().fortune.phase, 'inviting', 'An estimated script duration must not open the poll mid-speech');
    assert.equal(engine.getState().currentSpeechId, inviteId);
    engine.completeSpeech(inviteId);
    assert.equal(engine.getState().fortune.phase, 'voting');
    engine.vote(1); engine.closeVoting();
    const resultId = engine.getState().currentSpeechId;
    step(engine, 18000);
    assert.equal(revealed(engine), 0, 'The result announcement must finish before the first flip');
    completeUntil(engine, () => revealed(engine) === 1);
    const firstId = engine.getState().currentSpeechId;
    const first = engine.getState(), firstText = first.currentSpeech, firstCard = first.fortune.options[0].card;
    assert.ok(firstText.includes(firstCard.title), 'The first card speech must identify the revealed card');
    assert.ok(firstText.includes(firstCard.meaning), 'The meaning and practical guidance are spoken in the same uninterrupted turn');
    assert.match(firstText.slice(firstText.indexOf(firstCard.meaning) + firstCard.meaning.length), /今天|挑|找|核实|收个尾/, 'The complete reading includes practical guidance after the meaning');
    step(engine, 17000);
    assert.equal(revealed(engine), 1, 'The current card stays visible while Z is speaking');
    assert.equal(engine.completeSpeech(resultId), false);
    assert.equal(latestSpeech.id, firstId);
    engine.setPaused(true); assert.equal(engine.completeSpeech(firstId), true); step(engine, 6000);
    assert.equal(revealed(engine), 1, 'A completion callback received during pause cannot advance the scene');
    engine.setPaused(false); step(engine, 160);
    assert.equal(revealed(engine), 2);
    assert.equal(engine.completeSpeech(firstId), false, 'A repeated completion cannot advance another card');
  }, true));

  check('legacy personal selection waits for older turns and new questions wait for the complete contextual reading', () => {
    for (const previousSpeech of ['player reply', 'signup reaction', 'show speech']) withLegacyEngine(engine => {
      let latestSpeech; const speeches = [];
      engine.addEventListener('speak', ({ detail }) => { latestSpeech = detail; speeches.push(detail); });
      if (previousSpeech === 'player reply') {
        startVoting(engine);
        assert.equal(engine.sendMessage('Z，我最近换工作该怎么准备？'), true);
        step(engine, 800);
        completeUntil(engine, () => latestSpeech?.scope === 'reply');
        assert.equal(latestSpeech.scope, 'reply');
        assert.ok(latestSpeech.text.includes('Z，我最近换工作该怎么准备？'));
        assert.equal(engine.advance(), true);
        completeUntil(engine, () => engine.getState().stage === 'draw');
      } else if (previousSpeech === 'signup reaction') {
        engine.joinDraw('明天的面试应该怎样准备？');
        engine.completeSpeech(engine.getState().currentSpeechId);
        assert.equal(latestSpeech.scope, 'reaction', 'The old speech must be the signup reaction');
        assert.equal(engine.getState().drawJoined, true);
        assert.equal(engine.getState().drawQuestion, '明天的面试应该怎样准备？');
      }
      if (engine.getState().stage !== 'draw') {
        assert.equal(engine.drawNext(), true);
        completeUntil(engine, () => engine.getState().stage === 'draw');
      }
      assert.equal(engine.drawNext(), true);
      completeUntil(engine, () => engine.getState().stage === 'reading');
      assert.equal(engine.getState().stage, 'reading');
      const oldId = engine.getState().currentSpeechId;
      const { question } = engine.getState().currentViewer;
      assert.equal(engine.sendMessage('刚才还排着队的旧问题'), true);
      assert.equal(engine.selectCard(0), true);
      assert.equal(engine.getState().currentSpeechId, oldId, 'Selecting a card does not cut the invitation to select');
      assert.equal(engine.getState().currentCard, null);
      completeUntil(engine, () => Boolean(engine.getState().currentCard));
      const reading = engine.getState(), readingId = reading.currentSpeechId;
      assert.notEqual(readingId, oldId, previousSpeech);
      assert.ok(reading.currentSpeech.includes(question), 'The selected viewer receives a contextual interpretation after the previous turn ends');
      assert.ok(reading.currentSpeech.includes(reading.currentCard.title));
      assert.ok(reading.currentSpeech.includes(reading.currentViewer.reading.advice), 'The complete personal turn includes the practical advice before releasing the floor');
      assert.equal(engine.completeSpeech(oldId), false, 'An obsolete completion cannot advance this reading');
      step(engine, 800);
      assert.equal(engine.getState().currentSpeechId, readingId, 'A delayed question from before selection cannot take the floor back');

      assert.equal(engine.sendMessage('这张牌我还有一个新的问题'), true);
      step(engine, 800);
      assert.equal(engine.getState().currentSpeechId, readingId, 'The full current interpretation keeps the floor while the reply becomes ready');
      assert.equal(engine.completeSpeech(readingId), true);
      const newReply = engine.getState();
      assert.notEqual(newReply.currentSpeechId, readingId);
      assert.equal(latestSpeech.scope, 'reply'); assert.equal(newReply.replyStatus, 'replying');
      assert.ok(newReply.currentSpeech.includes('这张牌我还有一个新的问题'), 'The spoken reply accurately quotes the selected question');
      engine.completeSpeech(newReply.currentSpeechId);
      const resumed = engine.getState();
      assert.equal(speeches.filter(speech => speech.text === reading.currentSpeech).length, 1, 'The finished interpretation is not restarted after answering');
      assert.notEqual(resumed.currentSpeech, reading.currentSpeech, 'A fully completed reading is not resumed after answering');
      assert.equal(resumed.currentCard.title, reading.currentCard.title);
      assert.equal(resumed.round, reading.round);
    }, true);
  });

  check('ordinary chat preserves the registered question and personal advice depends on both question and card', () => {
    function read(question, selectedIndex) {
      let result;
      withLegacyEngine(engine => {
        assert.equal(engine.joinDraw(question), true);
        assert.equal(engine.sendMessage('嗨，Z！'), true); step(engine, 800);
        assert.equal(engine.getState().drawQuestion, question);
        assert.equal(engine.drawNext(), true); completeUntil(engine, () => engine.getState().stage === 'draw');
        assert.equal(engine.drawNext(), true); completeUntil(engine, () => engine.getState().stage === 'reading');
        for (let count = 0; !engine.getState().currentViewer.mine && count < 3; count++) {
          const round = engine.getState().round;
          assert.equal(engine.selectCard(0), true); completeUntil(engine, () => Boolean(engine.getState().currentCard));
          assert.equal(engine.drawNext(), true); completeUntil(engine, () => engine.getState().round !== round);
        }
        assert.equal(engine.getState().currentViewer.mine, true);
        assert.equal(engine.selectCard(selectedIndex), true);
        completeUntil(engine, () => Boolean(engine.getState().currentCard));
        const state = engine.getState();
        assert.equal(state.currentViewer.question, question);
        assert.equal(state.currentViewer.reading.question, question);
        assert.ok(state.currentViewer.reading.interpretation.includes(state.currentCard.title));
        assert.ok(state.currentViewer.reading.advice.length > 20);
        result = state.currentViewer;
      });
      return result;
    }
    const question = '最近准备跳槽，想判断新的工作机会是否适合我？';
    const first = read(question, 0), second = read(question, 1);
    assert.notEqual(first.card.title, second.card.title);
    assert.notEqual(first.reading.interpretation, second.reading.interpretation);
    assert.notEqual(first.reading.advice, second.reading.advice, 'Different cards must give different actions for the same question');
    assert.match(first.reading.advice, /跳槽|岗位|面试|工作/);
    const study = read('准备考试，总在错题和时间分配上卡住，想找学习方法。', 0);
    assert.match(study.reading.advice, /考试|错题|限时/);
    assert.notEqual(study.reading.advice, first.reading.advice);
  });

  check('the unattended show completes all three fortune cards and all three personal readings; replay clears the poll', () => withLegacyEngine(engine => {
    const question = '我想向喜欢的人表达心意，怎样开始比较自然？';
    engine.joinDraw(question);
    let fortuneCards = [], completedFortune = false;
    const rounds = new Set();
    engine.addEventListener('state', ({ detail }) => {
      if (detail.fortune?.phase === 'complete') {
        completedFortune = true;
        fortuneCards = detail.fortune.options.map(option => option.card?.title);
      }
      if (detail.stage === 'reading') rounds.add(detail.round);
    });
    let storySeen = false;
    until(engine, () => {
      if (engine.getState().lottery?.open) { storySeen = true; engine.closeLottery(); }
      return engine.getState().stage === 'free';
    });
    assert.equal(storySeen, true, 'Completing the collective reading must introduce the player story');
    assert.equal(completedFortune, true);
    assert.equal(new Set(fortuneCards).size, 3);
    assert.deepEqual([...rounds], [1, 2, 3]);
    const state = engine.getState();
    assert.equal(state.selectedViewers.length, 3);
    assert.ok(state.selectedViewers.every(viewer => viewer.card?.title && viewer.reading?.advice));
    assert.equal(state.selectedViewers.find(viewer => viewer.mine).question, question);
    engine.replay();
    assert.equal(engine.getState().stage, 'intro');
    assert.equal(engine.getState().fortune, undefined);
    assert.equal(engine.getState().selectedViewers.length, 0);
    assert.equal(engine.vote(0), false);
    assert.equal(timers.size, 1, 'Replay must not duplicate the room timer');
  }));

  console.log(`\n${results.length} fortune regression scenarios passed.`);
} finally {
  Object.defineProperty(globalThis, 'performance', { configurable: true, value: original.performance });
  globalThis.setInterval = original.setInterval; globalThis.clearInterval = original.clearInterval;
  Math.random = original.random;
}
