export const LABELS = { experience: '亲身经历', hearsay: '听别人说', commitment: '约定', reflection: '自己的看法' };
function tokens(text = '') {
  const lower = text.toLowerCase(), words = lower.match(/[a-z0-9_]{2,}/g) || [];
  for (const run of lower.match(/[\u3400-\u9fff]+/g) || []) for (let i = 0; i < run.length - 1; i++) words.push(run.slice(i, i + 2));
  return new Set(words);
}
export function similarity(a, b) {
  const aa = tokens(a), bb = tokens(b); let overlap = 0;
  aa.forEach(t => { if (bb.has(t)) overlap++; });
  return overlap / Math.max(1, Math.min(aa.size, bb.size));
}
export function strength(m, hour) {
  const importance = m.importance || 1, emotion = m.emotion || 0;
  const stability = 48 + importance * 60 + emotion * 24 + Math.min(m.recallCount || 0, 6) * 24;
  const detailStability = 18 + importance * 30 + emotion * 18 + Math.min(m.detailRecallCount || 0, 5) * 18;
  let availability = Math.round(100 * Math.exp(-Math.max(0, hour - (m.lastRecalledAt ?? m.createdHour)) / stability));
  if (m.kind === 'commitment' && m.commitmentStatus === 'pending') availability = Math.max(40, availability);
  return { availability, clarity: Math.round(100 * Math.exp(-Math.max(0, hour - (m.lastDetailRecalledAt ?? m.createdHour)) / detailStability)) };
}
export function memoryState(m, exp, all = []) {
  if (m.epoch !== exp.epoch) return { code: 'archived', label: '重置前', reason: '属于重置前的经历，当前 NPC 不能访问。' };
  if (m.createdHour > exp.hour) return { code: 'future', label: '尚未发生', reason: '不属于当前存档时间。' };
  if (m.supersededBy) return { code: 'superseded', label: '已更新', reason: '已被新记录更新，旧内容保留供核对。' };
  if (m.kind === 'reflection' && (m.dependencies || []).some(id => { const s = all.find(x => x.id === id); return !s || s.epoch !== exp.epoch || s.supersededBy; })) return { code: 'review', label: '需要重新考虑', reason: '支持这一看法的记录已经改变，重新思考前不用于回答。' };
  const { availability, clarity } = strength(m, exp.hour);
  if (availability < 8) return { code: 'dormant', label: '平时想不起', reason: '很久没有想起，相关线索仍可能唤起留下的大意。' };
  if (availability < 30) return { code: 'cue', label: '需要提示', reason: '有相关的人或话题时更容易想起；细节是否清楚单独计算。' };
  if (clarity < 65) return { code: 'fading', label: '细节模糊', reason: '通常只提供大意；尚存的细节遇到直接线索时可能唤起。' };
  return { code: 'clear', label: '记得清楚', reason: '经历较新或被实际使用过，容易想起，细节也还清楚。' };
}
export const accessible = s => ['clear', 'fading', 'cue', 'dormant'].includes(s.code);
export function memoryView(m, exp, query = '') {
  const values = strength(m, exp.hour);
  const cueScore = similarity(query, `${m.gist} ${m.content} ${(m.keywords || []).join(' ')}`);
  const detail = values.clarity >= 65 || (values.clarity >= 35 && cueScore >= .16);
  return { id: m.id, actorId: m.actorId, kind: m.kind, content: detail ? m.content : m.gist,
    detailLevel: detail ? 'detail' : 'gist', sourceLabel: m.sourceLabel, speakerId: m.speakerId || null,
    commitmentStatus: m.commitmentStatus,
    certainty: m.kind === 'reflection' ? '主观判断，可能有误' : m.kind === 'hearsay' ? '对方的陈述，未必属实' : '有在场或原话记录',
    ...values, cueScore, cued: cueScore >= .16,
    reason: detail ? (values.clarity < 65 ? '相关线索唤起尚存的细节' : '细节仍清楚') : '只记得大意，原话与细节未提供' };
}
export function retrievalCandidates(memories, exp, query, actorId, limit = 40) {
  const candidates = [], excluded = [];
  for (const m of memories.filter(m => m.actorId === actorId)) {
    const state = memoryState(m, exp, memories);
    if (!accessible(state)) { excluded.push({ id: m.id, content: m.gist, reason: state.reason, state: state.code }); continue; }
    const view = memoryView(m, exp, query);
    if (view.availability < 30 && !view.cued) { excluded.push({ id: m.id, content: m.gist, reason: '平时不易想起，本轮缺少相关线索', state: state.code }); continue; }
    candidates.push({ ...view, state, rank: view.cueScore * 65 + view.availability * .25 + m.importance * 4 });
  }
  candidates.sort((a, b) => b.rank - a.rank);
  return { candidates: candidates.slice(0, limit), excluded: [...excluded, ...candidates.slice(limit).map(m => ({ id: m.id, content: m.content, reason: '候选数量上限', state: 'overflow' }))] };
}
export const fallbackSelection = (candidates, topK) => candidates.filter(m => m.cueScore > .06).slice(0, topK).map(m => ({ id: m.id, relevance: m.cueScore, reason: '相关词语唤起' }));
export function parseObject(text) {
  let clean = text.trim();
  if (clean.startsWith('```')) clean = clean.replace(/^```(?:json)?\s*/i, '').replace(/\s*```$/, '');
  const obj = JSON.parse(clean);
  if (!obj || typeof obj !== 'object' || Array.isArray(obj)) throw new Error('模型未返回 JSON 对象');
  return obj;
}
