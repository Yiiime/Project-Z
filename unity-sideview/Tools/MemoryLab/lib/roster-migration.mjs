import { createHash } from 'node:crypto';
import { PUBLIC_DAY4, logicalHour } from './game-protocol.mjs';

export const PERSONA_REVISION = 2;
export const ACTOR_IDS = ['c01','c02','c03','c04','c05','c06','c07','z_story','npc_aya','npc_chen'];
const idFor = text => createHash('sha256').update(text).digest('hex').slice(0,24);

// Only used for records authored before the user's setting revision. New player
// statements are not keyword-censored; they remain attributed statements.
export function retiredPlot(text = '') {
  return /公司|工作人员|受雇扮演|演员|演出岗位|布景岗位|撤场|撤离名单|替代安排|luna|幕后导演|幕后安排|运营的\s*AI|AI\s*身份|人工智能|Z.{0,8}(是\s*AI|控制系统|系统|硬件|人格)|复制Z|格式化Z|原机重启|关闭假死/i.test(text);
}
export function upgradeSnapshot(input, book) {
  const s = structuredClone(input);
  if ((s.personaRevision || 0) >= PERSONA_REVISION) return s;
  const changed = new Set(), removed = new Set();
  const textOf = r => [r.content,r.gist,r.sourceLabel,r.subject].filter(Boolean).join(' ');
  const messages = s.messages || [];
  const excludedMessages = new Set(messages.filter(m => m.worldKind === 'staff_day4' || retiredPlot(textOf(m))).map(m => m.id));
  const memories = s.memories || [];
  for (const m of memories) {
    if (m.origin === 'authored_backstory') continue;
    if (m.origin === 'staff_day4' || retiredPlot(textOf(m)) || m.evidence?.some(e => excludedMessages.has(e.messageId))) removed.add(m.id);
  }
  // Preserve IDs and recall counters for ordinary backstory, replacing only the
  // authored text that was changed by the user. Derived conclusions are retired.
  const seeds = [];
  for (const actor of book.actors) {
    const old = memories.filter(m => m.actorId === actor.id && m.origin === 'authored_backstory');
    (actor.backstory || []).forEach((story, index) => {
      const previous = old[index];
      if (previous && (previous.content !== story.content || previous.gist !== story.gist)) changed.add(previous.id);
      seeds.push({ ...(previous || {}), id: previous?.id || 'mem_' + idFor(s.worldId + '_' + actor.id + '_backstory_' + index),
        actorId: actor.id, epoch: previous?.epoch || 1, kind: 'experience', ...story,
        sourceLabel: '本人过去的生活经历（日期未记载）', evidence: [], dependencies: [],
        createdHour: previous?.createdHour ?? s.hour, originalHour: null, importance: previous?.importance || 2,
        emotion: previous?.emotion || 0, recallCount: previous?.recallCount || 0, detailRecallCount: previous?.detailRecallCount || 0,
        origin: 'authored_backstory' });
    });
    old.slice((actor.backstory || []).length).forEach(m => removed.add(m.id));
  }
  let size;
  do {
    size = removed.size;
    for (const m of memories) if (m.dependencies?.some(id => removed.has(id) || changed.has(id))) removed.add(m.id);
  } while (removed.size !== size);
  s.memories = [...memories.filter(m => m.origin !== 'authored_backstory' && !removed.has(m.id)), ...seeds];
  s.messages = messages.filter(m => !excludedMessages.has(m.id));
  const retained = new Set(s.memories.map(m => m.id)), byMessage = new Map(s.messages.map(m => [m.id,m]));
  s.pending = (s.pending || []).filter(t => t.evidenceMessages?.every(m => byMessage.has(m.id))).map(t => ({ ...t,
    evidenceMessages: t.evidenceMessages.map(m => byMessage.get(m.id)),
    selected: (t.selected || []).filter(m => retained.has(m.id) && !changed.has(m.id) && !retiredPlot(textOf(m))),
    addedIds: (t.addedIds || []).filter(id => retained.has(id)) }));
  s.reflected = Object.fromEntries(Object.entries(s.reflected || {}).map(([actor, ids]) => [actor, ids.filter(id => retained.has(id) && !changed.has(id))]));
  // Sequence receipts stay intact, even if a retired statement is no longer in
  // active memory. This keeps durable outbox replays idempotent.
  if (s.events?.some(e => e.gameKind === 'day4') && s.hour >= logicalHour(4,0)) {
    let notice = s.messages.find(m => m.worldKind === 'public_day4');
    if (!notice) {
      notice = { id: 'msg_' + idFor(s.worldId + '_public_day4'), epoch: 1, hour: logicalHour(4,0), role:'event',speakerId:'world',
        content:PUBLIC_DAY4,gist:PUBLIC_DAY4,status:'complete',worldKind:'public_day4' };
      s.messages.push(notice);
    }
    notice.participants = book.actors.map(a => a.id);
    for (const actor of book.actors) if (!s.memories.some(m => m.actorId === actor.id && m.origin === 'public_day4'))
      s.memories.push({id:'mem_' + idFor(s.worldId + '_' + actor.id + '_public_day4'),epoch:1,actorId:actor.id,kind:'experience',
        content:PUBLIC_DAY4,gist:PUBLIC_DAY4,subject:'系统公告',sourceLabel:'系统公告',evidence:[{messageId:notice.id,quote:PUBLIC_DAY4}],
        dependencies:[],createdHour:notice.hour,originalHour:notice.hour,importance:3,emotion:0,keywords:['小岛','通知'],
        recallCount:0,detailRecallCount:0,origin:'public_day4'});
  }
  s.personaRevision = PERSONA_REVISION;
  return s;
}
