import {reviewReply} from './review-fixture.mjs';
import assert from 'node:assert/strict';
import {AudienceDirector} from '../ui/audience-director.js';
const flush = async () => {for (let i = 0; i < 24; i++) await Promise.resolve();};
function harness(config = {}) {
  let now = 10000, allowed = true, advance = true;
  const calls = [], published = [];
  const director = new AudienceDirector({config, now: () => now, canPublish: () => allowed, canAdvance: () => advance,
    request: (payload, options) => payload.kind==='review'?Promise.resolve(reviewReply(payload)):new Promise((resolve, reject) => calls.push({payload, ...options, resolve, reject})),
    publish: item => published.push({item, at: now})});
  return {director, calls, published, setAllowed: value => {allowed = value;}, setAdvance: value => {advance = value;},
    advance: ms => {now += ms; director.tick();}, now: () => now};
}
const event = (id, type = 'display') => ({id, type, text: `已公开的事件 ${id}`});
const item = (npc_id = 'wind', text = '我也在认真看这张画。', evidence_refs = ['one'], intent = 'reaction') => ({npc_id, text, evidence_refs, intent});
async function begin(h, id = 'one', type = 'display') {h.director.record(event(id, type)); h.advance(0); await flush(); return h.calls.at(-1);}
let count = 0;
async function test(name, fn) {await fn(); count++; console.log(`PASS ${name}`);}

await test('opening/chat/waiting/drawing/lottery share one 6-second policy, independent of phase budgets', async () => {
  for (const type of ['opening', 'speech.completed', 'awaiting_selection', 'drawing.displayed', 'lottery.displayed']) {
    const h = harness({phases: {opening: {maxItems: 0}, chat: {maxItems: 0}, displayed: {maxItems: 0}}});
    const call = await begin(h, 'one', type);
    assert.equal(h.calls.length, 1); assert.equal(call.payload.context.phase, 'chat');
    assert.deepEqual(call.payload.context.audience.eligibleNpcIds, ['wind', 'moon', 'salt']);
    call.resolve({status: 'skip', items: []}); await flush();
    h.advance(5999); await flush(); assert.equal(h.calls.length, 1);
    h.advance(1); await flush(); assert.equal(h.calls.length, 2, `Silent continuation in ${type}`);
  }
});
await test('frequent public events do not starve a pending generation or create extra requests', async () => {
  const h = harness(), call = await begin(h);
  for (let i = 0; i < 8; i++) {h.director.record(event(`new-${i}`)); h.advance(100);}
  assert.equal(call.signal.aborted, false); assert.equal(h.calls.length, 1);
  call.resolve({status: 'ready', items: [item()]}); await flush(); h.advance(0);
  assert.equal(h.published.length, 1); assert.equal(h.published[0].item.text, item().text);
  h.advance(5200); await flush(); assert.equal(h.calls.length, 2);
  assert.equal(h.calls[1].payload.eventId, 'new-7');
});
await test('one item per period; published NPC history does not become facts or trigger a loop', async () => {
  const h = harness(), call = await begin(h);
  call.resolve({status: 'ready', items: [item(), item('moon', '第二条不应发布。'), item('salt', '第三条不应发布。')]}); await flush(); h.advance(0);
  assert.equal(h.published.length, 1); assert.deepEqual(h.director.getPublicEvents().map(({id,type,text})=>({id,type,text})), [event('one')]);
  h.advance(5999); await flush(); assert.equal(h.calls.length, 1);
  h.advance(1); await flush(); assert.equal(h.calls.length, 2);
  const audience = h.calls[1].payload.context.audience;
  assert.deepEqual(audience.eligibleNpcIds, ['moon', 'salt']);
  assert.deepEqual(audience.recentMessages, [{npcId: 'wind', text: item().text}]);
});
await test('player attention restricts advance only; reactions continue throughout waiting', async () => {
  const h = harness(); h.setAdvance(false); const call = await begin(h);
  assert.equal(call.payload.context.audience.allowAdvance, false);
  call.resolve({status: 'ready', items: [item('wind', '再换个话题吧', ['one'], 'advance'), item('moon', '在这里等你选。')]}); await flush(); h.advance(0);
  assert.equal(h.published.length, 1); assert.equal(h.published[0].item.npc_id, 'moon');
  h.advance(6000); await flush(); assert.equal(h.calls.length, 2);
});
await test('an advance is rejected if player attention changes after generation or before publication', async () => {
  const h = harness(), call = await begin(h); h.setAdvance(false);
  call.resolve({status: 'ready', items: [item('wind', '来聊个新问题？', ['one'], 'advance')]}); await flush(); h.advance(0); assert.equal(h.published.length, 0);
  h.setAdvance(true); h.advance(6000); await flush();
  h.calls[1].resolve({status: 'ready', items: [item('moon', '再说个话题？', ['one'], 'advance')]}); await flush();
  h.setAdvance(false); h.advance(0); assert.equal(h.published.length, 0);
});
await test('pause cancels non-cooperative late results, then resumes from old facts without a new event', async () => {
  const h = harness(), call = await begin(h); h.setAllowed(false); h.advance(500);
  assert.equal(call.signal.aborted, true); call.resolve({status: 'ready', items: [item()]}); await flush();
  h.advance(10000); await flush(); assert.equal(h.calls.length, 1); assert.equal(h.published.length, 0);
  h.setAllowed(true); h.advance(0); await flush(); assert.equal(h.calls.length, 2);
  assert.equal(h.calls[1].payload.eventId, 'one');
  h.setAllowed(false); h.director.invalidate(); h.calls[1].resolve({status: 'ready', items: [item()]}); await flush();
  h.setAllowed(true); h.advance(10000); await flush(); assert.equal(h.calls.length, 2); assert.deepEqual(h.director.getPublicEvents(), []);
});
await test('generation failure/deadline/skip never cause an immediate retry; TTL expires delayed output', async () => {
  const h = harness(), call = await begin(h); call.reject(new Error('offline')); await flush(); h.advance(1000); await flush(); assert.equal(h.calls.length, 1);
  h.advance(5000); await flush(); const timed = h.calls[1]; h.advance(1500); assert.equal(timed.signal.aborted, true);
  timed.resolve({status: 'ready', items: [item()]}); await flush(); assert.equal(h.published.length, 0);
  h.advance(4500); await flush(); h.calls[2].resolve({status: 'skip', items: []}); await flush(); h.advance(1); await flush(); assert.equal(h.calls.length, 3);
  const ttl = harness({firstPublishDelayMs: 7000}); const slow = await begin(ttl);
  slow.resolve({status: 'ready', items: [item()]}); await flush(); ttl.advance(6000); assert.equal(ttl.published.length, 0);
});
await test('15-second NPC cooldown persists; content repetition is delegated to independent review', async () => {
  const h = harness(), first = await begin(h); first.resolve({status: 'ready', items: [item()]}); await flush(); h.advance(0);
  h.advance(6000); await flush(); h.calls[1].resolve({status: 'ready', items: [item('wind', '还没冷却。'), item('moon', '我也在认真看这张画！'), item('salt', '换一位来看看。')]}); await flush(); h.advance(0);
  assert.deepEqual(h.published.map(p => p.item.npc_id), ['wind', 'moon']);
  h.advance(6000); await flush(); h.calls[2].resolve({status: 'skip', items: []}); await flush();
  h.advance(6000); await flush(); assert.ok(h.calls[3].payload.context.audience.eligibleNpcIds.includes('wind'));
  h.calls[3].resolve({status: 'ready', items: [item('wind', '冷却之后再接话。')]}); await flush(); h.advance(0); assert.equal(h.published.length, 3);
});
await test('unknown NPCs, unsupported intent, absent evidence, overlong and unsafe text are rejected', async () => {
  const h = harness(), call = await begin(h);
  call.resolve({status: 'ready', items: [item('stranger'), item('wind', 'a'.repeat(37)), item('wind', '私有事实', ['secret']), item('wind', '无依据', []),
    item('wind', '命令', ['one'], 'command'), item('wind', '<img src=x>'), item('moon', '这一条有公开依据。')]}); await flush(); h.advance(0);
  assert.deepEqual(h.published.map(p => p.item.npc_id), ['moon']);
});
await test('public history is bounded and copied; six real NPC messages reach the next request', async () => {
  const h = harness();
  for (let i = 0; i < 20; i++) h.director.record({...event(`event-${i}`), hiddenDeck: 'never transmitted'});
  assert.equal(h.director.getPublicEvents().length, 12); assert.equal(h.director.record(event('event-19')), false);
  const copy = h.director.getPublicEvents(); copy[0].text = 'mutated'; assert.notEqual(h.director.getPublicEvents()[0].text, 'mutated');
  for (let i = 0; i < 8; i++) {
    h.advance(i ? 6000 : 0); await flush(); const call = h.calls.at(-1);
    const npc = call.payload.context.audience.eligibleNpcIds[0];
    call.resolve({status: 'ready', items: [item(npc, `已公开话题的第${i}条反应。`, ['event-19'])]}); await flush(); h.advance(0);
  }
  h.advance(6000); await flush(); const payload = h.calls.at(-1).payload;
  assert.equal(payload.context.audience.recentMessages.length, 6);
  assert.equal(payload.context.publicEvents.length, 12); assert.equal(JSON.stringify(payload).includes('hiddenDeck'), false);
  assert.match(payload.requestId, /^[0-9a-f-]{36}$/);
});
console.log(`\n${count} audience-director scenarios passed.`);
