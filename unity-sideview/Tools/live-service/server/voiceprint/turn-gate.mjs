const lexical = text => text.replace(/[^\p{L}\p{N}]/gu, '');
const useful = text => !!lexical(text) && !/^(嗯+|啊+|呃+|唔+|哦+)$/.test(lexical(text));

// The transport must call this ONLY for authenticated MMPG events.
// Browser messages must never be allowed to mark a speaker verified.
// No audio or unverified transcript is logged or written to disk.
export class VoiceprintTurnGate {
  constructor({ now = Date.now, holdMs = 350, pendingMs = 30000 } = {}) {
    if (!Number.isFinite(holdMs) || holdMs < 0 || holdMs > 1000) throw new Error('invalid_hold');
    this.now = now; this.holdMs = holdMs; this.pendingMs = pendingMs;
    this.active = null; this.retired = new Set();
  }
  reset() { this.active = null; this.retired.clear(); }
  retire(id) {
    this.retired.add(id);
    while (this.retired.size > 256) this.retired.delete(this.retired.values().next().value);
  }
  discard(reason) {
    const turn = this.active;
    if (!turn) return [];
    this.retire(turn.id); this.active = null;
    return turn.published ? [{ type: 'discard', conversationId: turn.id, reason }] : [];
  }
  receive(event) {
    const id = event?.conv_id;
    if (typeof id !== 'string' || !id || id.length > 128 || this.retired.has(id)) return [];
    let turn = this.active;
    if (event.type === 'user.conversation.created') {
      if (turn?.id === id) return [];
      const result = this.discard('superseded');
      this.active = { id, createdAt: this.now(), startedAt: null, verified: false, svActive: false, text: '', final: false, published: false, lastText: '' };
      return result;
    }
    // Late packets from a previous round never authorize a new round.
    if (!turn || turn.id !== id) return [];
    if (this.now() - turn.createdAt > this.pendingMs) return this.discard('expired');
    if (event.type === 'user.conversation.canceled') return this.discard('canceled');
    if (event.type === 'user.audio.transcription.started') {
      // Unlike the vendor demo's fast path, absent/false sv_active fails closed.
      if (event.sv_active !== true) return this.discard('verification_inactive');
      turn.svActive = true; turn.startedAt ??= this.now();
    } else if (event.type === 'user.audio.speaker.verification') {
      turn.verified = true;
    } else if (['user.audio.transcription.delta', 'user.audio.transcription.segment', 'user.audio.transcription.done'].includes(event.type)) {
      if (typeof event.transcript !== 'string' || event.transcript.length > 4000) return this.discard('invalid_transcript');
      if (turn.final) return [];
      turn.text = event.transcript.trim();
      // A sentence segment is not a finished conversational turn.
      turn.final = event.type === 'user.audio.transcription.done';
    } else return [];
    return this.flush();
  }
  flush() {
    const turn = this.active;
    if (!turn) return [];
    if (this.now() - turn.createdAt > this.pendingMs) return this.discard('expired');
    if (turn.final && !useful(turn.text)) return this.discard('empty_final');
    if (!turn.verified || !turn.svActive || turn.startedAt === null || !useful(turn.text)) return [];
    // This is a minimum observation window, not a measurement of speech duration.
    if (this.now() - turn.startedAt < this.holdMs) return [];
    const result = [];
    if (!turn.published) {
      result.push({ type: 'player-speaking', conversationId: turn.id });
      turn.published = true;
    }
    if (turn.text !== turn.lastText || turn.final) {
      result.push({ type: 'transcript', conversationId: turn.id, text: turn.text, final: turn.final, speakerVerified: true });
      turn.lastText = turn.text;
    }
    if (turn.final) { this.retire(turn.id); this.active = null; }
    return result;
  }
}
