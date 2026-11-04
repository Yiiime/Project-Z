export const SKILLS = ['singing', 'simple-drawing', 'entertainment-tarot'];
export const MOODS = ['warm', 'happy', 'thinking', 'serious'];
export const ACTIONS = ['reply', 'wait', 'request_skill', 'present_skill', 'offer_story'];
export const copy = value => structuredClone(value);
export function check(ok, code) { if (!ok) throw new Error(code); }
export function text(value, max = 2000) {
  check(typeof value === 'string' && value.trim().length > 0 && [...value].length <= max, 'invalid_text');
  return value.trim();
}
export function validateDecision(d, token) {
  check(d && typeof d === 'object' && !Array.isArray(d), 'invalid_decision');
  const fields = ['turnId', 'action', 'eventId', 'speech', 'mood', 'skill', 'args', 'taskId', 'storyId', 'resumePoint', 'memoryIds'];
  check(Object.keys(d).every(k => fields.includes(k)), 'unknown_decision_field');
  check(d.turnId === token.turnId && d.eventId === token.eventId, 'wrong_turn');
  check(ACTIONS.includes(d.action), 'invalid_action');
  check(MOODS.includes(d.mood), 'invalid_mood');
  check(Array.isArray(d.memoryIds) && d.memoryIds.every(x => typeof x === 'string'), 'invalid_memory_ids');
  if (d.action === 'wait' || d.action === 'present_skill') check(d.speech === null, 'speech_must_be_null');
  else text(d.speech, 160);
  if (d.action === 'request_skill') {
    check(SKILLS.includes(d.skill), 'unknown_skill');
    check(d.args && typeof d.args === 'object' && !Array.isArray(d.args), 'invalid_args');
    check(JSON.stringify(d.args).length <= 6000, 'args_too_large');
    text(d.resumePoint, 300);
  }
  if (d.action === 'present_skill') text(d.taskId, 100);
  if (d.action === 'offer_story') text(d.storyId, 100);
  return d;
}

export const DEFAULTS = Object.freeze({
  audienceTTL: 45000, audienceWindowMs: 12000, audienceQueue: 40,
  guestQueue: 20, guestTTL: 180000, historyLimit: 80,
  idleMs: 15000, maxIdlePrompts: 1, maxCallMs: 30 * 60 * 1000,
  disconnectGraceMs: 30000, decisionTimeoutMs: 8000, ttsTimeoutMs: 20000,
  outputTimeoutMs: 120000, taskTimeoutMs: 90000, inputTimeoutMs: 45000,
  storyCooldownMs: 120000, storyOfferTTL: 60000, logLimit: 500,
  maxEvents: 20000
});

export function publicMemories(records, guestId) {
  return records.filter(m => m && m.visibility === 'public' && m.status === 'confirmed'
    && m.subjectId === guestId && typeof m.id === 'string' && typeof m.text === 'string')
    .slice(0, 8).map(m => ({id: m.id, text: m.text.slice(0, 800), source: String(m.source ?? '').slice(0, 200)}));
}
