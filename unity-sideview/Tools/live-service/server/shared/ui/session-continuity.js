// Session-local observations and model-authored plans stay separate. No persistence.
const copySession = value => JSON.parse(JSON.stringify(value));
export function validateAgendaUpdates(updates = [], {items = [], evidence = [], allowCurrentSpeech = false} = {}) {
  if (!Array.isArray(updates) || updates.length > 4) throw Error('invalid_agenda');
  const ids = new Set(items.map(i => i.id)), refs = new Set(evidence.map(e => e.id)), used = new Set();
  return updates.map(item => {
    if (!item || typeof item !== 'object' || Object.keys(item).some(k => !['id','goal','nextStep','status','evidenceIds'].includes(k))) throw Error('invalid_agenda');
    if (item.id !== null && (!ids.has(item.id) || used.has(item.id))) throw Error('invalid_agenda_id');
    if (item.id !== null) used.add(item.id);
    if (!['open','deferred','completed','cancelled'].includes(item.status) || item.id === null && !['open','deferred'].includes(item.status)) throw Error('invalid_agenda_status');
    for (const k of ['goal','nextStep']) if (typeof item[k] !== 'string' || !item[k].trim() || item[k].length > 160) throw Error('invalid_agenda_text');
    if (!Array.isArray(item.evidenceIds) || !item.evidenceIds.length || item.evidenceIds.length > 3 || item.evidenceIds.some(r => !refs.has(r) && !(r === 'current_speech' && allowCurrentSpeech))) throw Error('invalid_agenda_evidence');
    return copySession(item);
  });
}
export class SessionContinuity {
  constructor() {this.reset();}
  reset() {this.id = crypto.randomUUID(); this.events = []; this.items = []; this.recentInitiatives = [];}
  record(event) {
    // Partial captions remain in the current public view, never in past memories.
    if (event.type === 'speech.progress') return;
    const index = this.events.findIndex(e => e.id === event.id);
    if (index < 0) this.events.push(copySession(event)); else this.events[index] = copySession(event);
    this.events = this.events.slice(-64);
  }
  evidence() {
    const map = new Map();
    for (const item of this.items.filter(i => ['open','deferred'].includes(i.status))) for (const e of item.sources) map.set(e.id,e);
    for (const e of this.events.filter(e=>e.type==='speech.completed'||e.type==='player.message').slice(-12)) map.set(e.id,e);
    for (const e of this.events.slice(-8)) map.set(e.id,e);
    return copySession([...map.values()].slice(-32));
  }
  snapshot() {
    const evidence = this.evidence(), refs = new Set(evidence.map(e => e.id));
    return {sessionId:this.id, evidence, unfinishedItems:this.items.filter(i => ['open','deferred'].includes(i.status)).map(({sources,...i}) => ({...i,evidenceIds:i.evidenceIds.filter(r => refs.has(r))})),
      recentInitiatives:copySession(this.recentInitiatives), crossSessionMemory:{available:false,entries:[]}};
  }
  apply(updates, generationSession, completedSpeech = null, initiative = false) {
    const evidence = generationSession.evidence || [];
    const safe = validateAgendaUpdates(updates,{items:generationSession.unfinishedItems,evidence,allowCurrentSpeech:Boolean(completedSpeech)});
    const sources = new Map(evidence.map(e => [e.id,e]));
    if (completedSpeech) sources.set('current_speech',completedSpeech);
    for (const update of safe) {
      const selected = update.evidenceIds.map(r => sources.get(r));
      const item = {...update,id:update.id || crypto.randomUUID(),evidenceIds:selected.map(e => e.id),sources:copySession(selected)};
      const index = this.items.findIndex(i => i.id === item.id);
      if (index >= 0) this.items[index] = item; else this.items.push(item);
    }
    // Bounded working set; dropping an old item is not a claim it was completed.
    this.items = this.items.filter(i => ['open','deferred'].includes(i.status)).slice(-8);
    if (initiative && completedSpeech) this.recentInitiatives = [...this.recentInitiatives,{speechId:completedSpeech.id,text:completedSpeech.text}].slice(-4);
  }
}

// Structural validation only; the model judges whether a topic is ending.
export function validateTopicProgress(value, evidence = []) {
  if(!value||Object.keys(value).some(k=>!['status','reason','nextDirection','evidenceIds','anchorSpeechId'].includes(k))||!['expanding','wrapping','complete','waiting'].includes(value.status))throw Error('invalid_topic_progress');
  for(const key of ['reason','nextDirection','anchorSpeechId'])if(typeof value[key]!=='string'||value[key].length>(key==='anchorSpeechId'?100:180)||key!=='nextDirection'&&!value[key].trim())throw Error('invalid_topic_progress');
  if(!Array.isArray(value.evidenceIds)||value.evidenceIds.length>6||value.evidenceIds.some(id=>!evidence.some(e=>e.id===id)))throw Error('invalid_topic_evidence');
  return copySession(value);
}
