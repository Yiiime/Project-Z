import {completeHostSentences} from './sentence-boundaries.js';

const reviewError = code => Object.assign(new Error(code), {code});

export function repairSpan(text, span) {
  const at = text.indexOf(span);
  if (!span || at < 0) throw reviewError('agent_reply_repair_failed');
  let offset = 0;
  for (const sentence of completeHostSentences(text, true)) {
    const start = text.indexOf(sentence, offset), end = start + sentence.length;
    if (at >= start && at + span.length <= end) return {start, end, text:sentence};
    offset = end;
  }
  throw reviewError('agent_reply_repair_failed');
}

export async function approveCallCandidate(engine, {candidate, result, signal, valid, budget = {remaining:1}, beforeRepair = async () => {}}) {
  const dialogue = engine.audience.dialogue;
  let current = candidate;
  for (let attempt = 0; attempt < 2; attempt++) {
    if (!valid()) throw reviewError('agent_reply_stale');
    engine._warmCandidate(current.id, {...result, speech:current.text});
    let outcome, issue;
    const ticket = await dialogue.inspect(current, {
      signal, compact:true, sessionEvents:engine.session.evidence(),
      onOutcome:(value, detail) => { outcome = value; issue = detail; },
    });
    if (ticket && valid()) return {ticket, candidate:current, repaired:attempt === 1};
    engine.speechAudio?.dropWarmSpeech(current.id);
    if (!valid() || outcome === 'stale' || outcome === 'cancelled') throw reviewError('agent_reply_stale');
    if (outcome !== 'rejected') throw reviewError('agent_review_unavailable');
    if (attempt || !budget.remaining || current.intent !== 'speak' || current.plannedTool || result.sessionDecision || !issue?.issue || !issue?.span) {
      throw reviewError('agent_reply_repair_failed');
    }
    budget.remaining--;
    await beforeRepair();
    if (!valid()) throw reviewError('agent_reply_stale');
    const part = repairSpan(current.text, issue.span);
    const reviewContext = {...dialogue.snapshot(), sessionEvents:engine.session.evidence(), candidate:{...current, text:part.text.trim()}};
    dialogue.log({stage:'host.repair.started', candidateId:current.id, issue:issue.issue});
    const alreadySpoken = engine.facts.filter(e => e.type === 'speech.completed' || e.type === 'speech.progress').slice(-6).map(e => e.text);
    let response;
    try {
      response = await engine.client.generate({
        requestId:crypto.randomUUID(), kind:'repair', eventId:current.id, message:'只修正被指出的这一句',
        context:{reviewContext, issue:{issue:issue.issue, span:issue.span, reason:issue.reason || '请依据现有事实澄清'}, alreadySpoken},
      }, {signal});
    } catch {
      throw reviewError(valid() ? 'agent_reply_repair_failed' : 'agent_reply_stale');
    }
    if (!valid()) throw reviewError('agent_reply_stale');
    const replacement = response?.speech?.trim();
    if (!replacement || replacement.length > 120 || completeHostSentences(replacement, true).length !== 1) throw reviewError('agent_reply_repair_failed');
    const text = current.text.slice(0, part.start) + replacement + current.text.slice(part.end);
    if (text.length > 360) throw reviewError('agent_reply_repair_failed');
    current = {...current, id:crypto.randomUUID(), text};
  }
  throw reviewError('agent_reply_repair_failed');
}
