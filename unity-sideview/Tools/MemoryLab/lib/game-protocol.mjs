import { AppError } from './provider.mjs';
export const PERIOD_HOURS = [9+20/60,14+10/60,18+15/60,20+40/60];
export const PUBLIC_DAY4 = '小岛将要毁灭。';
export function logicalHour(day,period) {
  if(!Number.isInteger(day)||day<1||day>10000||!Number.isInteger(period)||period<0||period>3)throw new AppError('游戏日期无效');
  return (day-1)*24+PERIOD_HOURS[period];
}
export function identifier(value,label='标识') {
  if(typeof value!=='string'||!/^[a-zA-Z0-9_-]{1,100}$/.test(value))throw new AppError(`${label}无效`);
  return value;
}
export function text(value,max=400) { if(typeof value!=='string'||!value.trim()||value.length>max)throw new AppError('文本为空或超过长度限制');return value.trim(); }
export function scene(value) { if(!['Street','Cafe','Lighthouse'].includes(value))throw new AppError('场景无效');return value; }
export function clone(value) { return JSON.parse(JSON.stringify(value)); }
export function publicActor(a) { return {id:a.id,name:a.name,role:a.role,pronoun:a.pronoun}; }
export function validateSnapshot(snapshot,worldId,hour,actorIds) {
  if(snapshot.version!==1||snapshot.worldId!==worldId||!Number.isInteger(snapshot.seq)||snapshot.seq<0||!Number.isFinite(snapshot.hour)||snapshot.hour>hour+.001)throw new AppError('存档中的记忆快照与游戏时间不匹配');
  for(const type of ['memories','messages','events']) {
    if(!Array.isArray(snapshot[type])||snapshot[type].length>20000)throw new AppError('记忆快照过大或无效');
    for(const r of snapshot[type]) {
      identifier(r.id);
      const time=r.createdHour??r.hour;
      if(!Number.isFinite(time)||time>hour+.001)throw new AppError('拒绝载入未来记忆');
      if(type==='memories'&&(!actorIds.includes(r.actorId)||!['experience','hearsay','commitment','reflection'].includes(r.kind)))throw new AppError('记忆归属或类型无效');
      if(type==='messages'&&(!Array.isArray(r.participants)||r.participants.some(a=>a!=='player'&&!actorIds.includes(a))))throw new AppError('对话听众无效');
    }
  }
  const messages=new Map(snapshot.messages.map(m=>[m.id,m]));
  for(const pending of snapshot.pending||[]) {
    if(!Number.isFinite(pending.hour)||pending.hour>hour+.001||!actorIds.includes(pending.actorId)||!Array.isArray(pending.evidenceMessages)||!Array.isArray(pending.observers)||pending.observers.some(a=>!actorIds.includes(a)))throw new AppError('待整理记忆与当前存档不一致');
    for(const m of pending.evidenceMessages){const stored=messages.get(m.id);if(!stored||stored.content!==m.content||stored.speakerId!==m.speakerId||JSON.stringify(stored.participants)!==JSON.stringify(m.participants))throw new AppError('待整理记忆的来源不匹配');}
  }
  return snapshot;
}
