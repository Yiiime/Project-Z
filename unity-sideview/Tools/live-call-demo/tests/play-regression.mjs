import assert from 'node:assert/strict';
import {PlayEngine, drawingSvg} from '../ui/play-engine.js';

if (!globalThis.CustomEvent) globalThis.CustomEvent = class extends Event {constructor(type, {detail}) {super(type); this.detail = detail;}};
let now = 0;
const original = {performance: globalThis.performance, setInterval, clearInterval};
Object.defineProperty(globalThis, 'performance', {configurable: true, value: {now: () => now}});
globalThis.setInterval = () => 1;
globalThis.clearInterval = () => {};
function engine(storage = null) {
  const e = new PlayEngine({storage});
  e._lastTick = now;
  e._nextChatAt = Infinity; e._nextViewerAt = Infinity;
  e.setSpeechDriven(true);
  return e;
}
function step(e, ms) {for (let elapsed = 0; elapsed < ms; elapsed += 160) {now += Math.min(160, ms - elapsed); e._tick();}}
function finishSpeech(e) {const current = e.getState().currentSpeechId; if (current) e.completeSpeech(current); e._pumpSpeech();}
function drain(e, predicate, max = 60) {
  for (let i = 0; i < max && !predicate(); i++) {finishSpeech(e); step(e, 160);}
  assert.ok(predicate(), 'Expected the flow to reach its target state');
}
function select(e, index = 0) {
  const t = e.getState().play.tarot;
  assert.equal(e.candidatesDisplayed(t.id, t.round), true);
  assert.equal(e.chooseTarot(t.id, t.round, t.candidates[index].id), true);
}
function tarotReady(e, spread = 'single', origin = 'viewer') {
  assert.equal(e.requestTarot({origin, spread, question: '新计划可以先关注什么？'}), true);
  drain(e, () => e.getState().play.tarot?.phase === 'candidates_ready');
}
let count = 0;
function test(name, fn) {fn(); count++; console.log(`PASS ${name}`);}
try {
  test('viewer locks only one card; stale clicks cannot affect the next round', () => {
    const e = engine(); tarotReady(e, 'three');
    let t = e.getState().play.tarot;
    assert.equal(JSON.stringify(t).includes('meaning'), false);
    assert.equal(e._chooseTarot(t.id, t.round, t.candidates[0].id, 'host'), false);
    e.candidatesDisplayed(t.id, t.round);
    assert.equal(e.chooseTarot(t.id, t.round, t.candidates[1].id), true);
    assert.equal(e.chooseTarot(t.id, t.round, t.candidates[2].id), false);
    step(e, 800);
    assert.equal(e.getState().play.tarot.cards.length, 0);
    assert.equal(e.chooseTarot(t.id, t.round, t.candidates[2].id), false);
    select(e, 2); step(e, 800); select(e, 0); step(e, 800);
    t = e.getState().play.tarot;
    assert.equal(t.phase, 'revealing'); assert.equal(t.cards.length, 3);
    assert.equal(new Set(t.cards.map(c => c.name)).size, 3);
    assert.equal(e.getState().currentCard, null, 'No reading context before real display');
    assert.equal(e.tarotDisplayed(t.id), true); assert.equal(e.tarotDisplayed(t.id), false);
    drain(e, () => e.getState().play.tarot.phase === 'completed');
    assert.equal(e.getState().play.tarot.readCount, 3);
    e.destroy();
  });
  test('host chooses without granting viewer control and does not reveal unselected cards', () => {
    const e = engine(); tarotReady(e, 'single', 'host'); const t = e.getState().play.tarot;
    e.candidatesDisplayed(t.id, t.round);
    assert.equal(e.chooseTarot(t.id, t.round, t.candidates[0].id), false);
    drain(e, () => e.getState().play.tarot.phase === 'selection_locked'); step(e, 800);
    assert.equal(e.getState().play.tarot.cards.length, 1);
    e.destroy();
  });
  test('waiting never auto-selects; paused and cancelled rounds reject clicks', () => {
    const e = engine(); tarotReady(e);
    const t = e.getState().play.tarot; e.candidatesDisplayed(t.id, t.round);
    step(e, 60000); assert.equal(e.getState().play.tarot.slots.length, 0);
    assert.equal(e.getState().play.tarot.reminded, true);
    assert.equal(e.pauseTarot(), true);
    assert.equal(e.chooseTarot(t.id, t.round, t.candidates[0].id), false);
    e.pauseTarot();
    e.finishActivity(true); drain(e, () => e.getState().play.active === null);
    assert.equal(e.chooseTarot(t.id, t.round, t.candidates[0].id), false);
    e.destroy();
  });
  test('drawing continues while hidden, completion waits for actual display, edits preserve geometry', () => {
    const e = engine(); e.requestDrawing();
    drain(e, () => e.getState().play.drawing.stage === 0);
    const d = e.getState().play.drawing;
    assert.equal(d.open, true); assert.ok(d.svg.includes('id="head"')); assert.ok(!d.svg.includes('id="face"'));
    e.drawingDisplayed(d.id, 0, 0); e.setPanel('drawing', false); step(e, 14000);
    let latest = e.getState().play.drawing;
    assert.equal(latest.stage, 2); assert.equal(latest.open, false); assert.equal(latest.phase, 'ready');
    assert.equal(e.drawingDisplayed(d.id, 0, 2), false);
    e.setPanel('drawing', true); assert.equal(e.drawingDisplayed(d.id, 0, 2), true);
    drain(e, () => !e.getState().currentSpeech);
    e.editDrawing('bow'); drain(e, () => e.getState().play.drawing.revision === 1);
    drain(e, () => e.getState().play.drawing.phase === 'ready');
    latest = e.getState().play.drawing;
    assert.equal(e.drawingDisplayed(d.id, 0, 2), false);
    assert.equal(latest.svg.match(/<path id="head"[^>]+>/)[0], d.svg.match(/<path id="head"[^>]+>/)[0]);
    assert.equal(latest.svg, drawingSvg('cat', 'bow', 2)); e.destroy();
  });
  test('whole-room pause freezes drawing and queued requests start after completion', () => {
    const e = engine(); e.requestDrawing(); drain(e, () => e.getState().play.drawing.stage === 0);
    e.requestTarot({question: '如何开始？'}); e.requestTarot({question: '如何开始？'});
    assert.equal(e.getState().play.queue.length, 1);
    e.setPaused(true); step(e, 30000); assert.equal(e.getState().play.drawing.stage, 0);
    e.setPaused(false); step(e, 14000);
    const d = e.getState().play.drawing; e.drawingDisplayed(d.id, 0, 2);
    e.finishActivity(); drain(e, () => e.getState().play.active === 'tarot');
    assert.equal(e.getState().play.drawing.open, false); assert.equal(e.getState().play.queue.length, 0); e.destroy();
  });
  test('refresh restores the same candidates and locked choices; replay invalidates old callbacks', () => {
    let saved = null; const storage = {getItem: () => saved, setItem: (_, v) => {saved = v;}};
    const first = engine(storage); tarotReady(first, 'three'); select(first, 1); step(first, 800);
    const prior = first.getState().play.tarot; first.destroy();
    const second = engine(storage); second.start(); const current = second.getState().play.tarot;
    assert.equal(current.id, prior.id); assert.deepEqual(current.slots, prior.slots);
    assert.deepEqual(current.candidates, prior.candidates);
    second.replay(); assert.equal(second.candidatesDisplayed(current.id, current.round), false);
    assert.equal(second.getState().play.tarot, null); second.destroy();
  });
  test('lottery queues behind a running game instead of hiding its unfinished work', () => {
    const e = engine(); tarotReady(e); e.triggerStory();
    assert.equal(e.getState().lottery, null); assert.equal(e.getState().play.queue[0].kind, 'lottery');
    e.finishActivity(true); drain(e, () => e.getState().lottery?.open);
    assert.equal(e.getState().play.active, null); e.closeLottery(); e.destroy();
  });
  test('new rounds retain old results; feedback never redraws', () => {
    const e = engine(); tarotReady(e); select(e); step(e, 800);
    let t = e.getState().play.tarot; e.tarotDisplayed(t.id);
    drain(e, () => e.getState().play.tarot.phase === 'completed');
    const cards = e.getState().play.tarot.cards;
    e.sendMessage('感觉不太像我'); drain(e, () => !e.getState().currentSpeech);
    assert.deepEqual(e.getState().play.tarot.cards, cards);
    e.finishActivity(); tarotReady(e);
    assert.equal(e.getState().play.history.length, 1);
    assert.deepEqual(e.getState().play.history[0].cards, cards); e.destroy();
  });
  test('repeated starts coalesce at a speech boundary and a different request remains queued', () => {
    const e = engine(); e._say('这句话先说完。');
    e.requestDrawing(); e.requestDrawing(); e.requestTarot({question: '先从哪里开始？'});
    assert.equal(e.getState().play.queue.length, 1);
    finishSpeech(e); assert.equal(e.getState().play.active, 'drawing');
    assert.equal(e.getState().play.queue[0].kind, 'tarot'); e.destroy();
  });
  test('starting a different activity closes the review panel and cancelled drawing results stay cancelled', () => {
    const e = engine(); tarotReady(e); select(e); step(e, 800);
    const t = e.getState().play.tarot; e.tarotDisplayed(t.id);
    drain(e, () => e.getState().play.tarot.phase === 'completed'); e.finishActivity();
    e.setPanel('tarot', true); e.requestDrawing();
    assert.equal(e.getState().play.tarot.open, false);
    drain(e, () => e.getState().play.drawing.stage === 0);
    const d = e.getState().play.drawing; e.finishActivity(true);
    drain(e, () => e.getState().play.active === null); step(e, 20000);
    assert.equal(e.getState().play.drawing.phase, 'cancelled');
    assert.equal(e.drawingDisplayed(d.id, 0, 2), false); e.destroy();
  });
  console.log(`\n${count} play regression scenarios passed.`);
} finally {
  Object.defineProperty(globalThis, 'performance', {configurable: true, value: original.performance});
  globalThis.setInterval = original.setInterval; globalThis.clearInterval = original.clearInterval;
}
