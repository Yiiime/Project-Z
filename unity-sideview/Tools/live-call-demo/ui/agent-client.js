// Same-origin agent requests; audience work has its own lane and never blocks Z.
const SAFE_AGENT_ERRORS=['agent_insufficient_balance','agent_timeout','agent_invalid_reply','agent_provider_unavailable','agent_not_configured','agent_busy','invalid_input','agent_unavailable'];
export class AgentClient {
  constructor({fetchImpl = globalThis.fetch?.bind(globalThis),onTiming = () => {}} = {}) {
    this.fetch = fetchImpl; this.onTiming=onTiming; this.controllers = new Set(); this.queue = []; this.running = false; this.blockedUntil=0;
    this.sentenceStreaming=true;
  }
  async configure() {
    this.blockedUntil=0;
    if (!this.fetch || globalThis.location?.protocol === 'file:') return {enabled:false};
    const controller = new AbortController(), timer = setTimeout(() => controller.abort(), 6000);
    try {
      const response = await this.fetch('/api/agent/config', {signal:controller.signal, cache:'no-store'});
      return response.ok ? await response.json() : {enabled:false};
    } catch { return {enabled:false}; } finally { clearTimeout(timer); }
  }
  generate(payload, {signal,onSentence,compactReview=false} = {}) {
    if(Date.now()<this.blockedUntil)return Promise.reject(Object.assign(new Error('agent_unavailable'),{code:'agent_insufficient_balance'}));
    payload={...payload,requestId:payload.requestId||crypto.randomUUID()};
    if(onSentence&&payload.kind==='host')return this._fetch(payload,24000,signal,{onSentence});
    if (payload.kind === 'host' && payload.context?.host?.turn?.mode === 'prepare') return this._fetch(payload, 24000, signal);
    if (payload.kind === 'topic') return this._fetch(payload, 3500, signal);
    if (payload.kind === 'review') return this._fetch(payload, 4500, signal,{compactReview});
    if (payload.kind === 'artwork') return this._fetch(payload,50000,signal);
    if (payload.kind === 'audience') return this._fetch(payload, 4000, signal);
    if (this.queue.length >= 40) return Promise.reject(new Error('agent_queue_full'));
    return new Promise((resolve,reject) => {
      const entry = {payload,signal,resolve,reject,at:Date.now()};
      if (payload.kind === 'decision') {
        const index = this.queue.findIndex(item => item.payload.kind !== 'decision');
        index < 0 ? this.queue.push(entry) : this.queue.splice(index,0,entry);
      } else this.queue.push(entry);
      this._drain();
    });
  }
  async _drain() {
    if (this.running) return;
    this.running = true;
    try {
      while (this.queue.length) {
        const item = this.queue.shift(), remaining = (item.payload.kind==='host'?24000:8000) - (Date.now() - item.at);
        try {
          if (remaining <= 0 || item.signal?.aborted) throw new Error('agent_request_expired');
          item.resolve(await this._fetch(item.payload,remaining,item.signal));
        } catch(error) {item.reject(error);}
      }
    } finally {this.running = false;}
  }
  async _fetch(payload, timeout, parentSignal,{onSentence,compactReview=false}={}) {
    const started=performance.now();let status='cancelled',errorCode=null,timedOut=false;
    const controller = new AbortController(), abort = () => controller.abort();
    if (parentSignal?.aborted) throw new Error('cancelled');
    parentSignal?.addEventListener('abort',abort,{once:true});
    this.controllers.add(controller);
    const timer = setTimeout(()=>{timedOut=true;abort();},timeout);
    try {
      const response = await this.fetch('/api/agent',{method:'POST',headers:{'Content-Type':'application/json',...(onSentence?{Accept:'application/x-ndjson'}:{}),...(compactReview?{'X-Z-Compact-Review':'1'}:{})},
        body:JSON.stringify({...payload,requestId:payload.requestId||crypto.randomUUID()}),signal:controller.signal});
      let data;
      if(response.ok&&onSentence&&response.headers.get('Content-Type')?.includes('application/x-ndjson')){
        const reader=response.body.getReader(),decoder=new TextDecoder('utf-8',{fatal:true});let pending='',bytes=0,index=0;
        try{for(;;){const part=await reader.read();if(controller.signal.aborted)throw Error('cancelled');if(part.done)break;bytes+=part.value.length;if(bytes>131072)throw Error('invalid_stream');pending+=decoder.decode(part.value,{stream:true});let n;
          while((n=pending.indexOf('\n'))>=0){const line=pending.slice(0,n);pending=pending.slice(n+1);if(!line.trim())continue;const event=JSON.parse(line);if(event.requestId!==payload.requestId||data)throw Error('invalid_stream');
            if(event.type==='sentence'){if(event.index!==index++||index>6||typeof event.text!=='string'||!event.text.trim()||event.text.length>360)throw Error('invalid_sentence');onSentence(event);}
            else if(event.type==='result')data=event.result;
            else if(event.type==='error'){errorCode=SAFE_AGENT_ERRORS.includes(event.error)?event.error:'agent_unavailable';if(errorCode==='agent_insufficient_balance')this.blockedUntil=Date.now()+60000;throw Object.assign(Error('agent_unavailable'),{code:errorCode});}else throw Error('invalid_stream');
          }
        }if(!data||pending.trim())throw Error('incomplete_stream');}finally{await reader.cancel().catch(()=>{});}
      }else data=await response.json();
      if (!response.ok || data.requestId !== payload.requestId) {
        const safeErrors=SAFE_AGENT_ERRORS;
        errorCode=safeErrors.includes(data.error)?data.error:'agent_unavailable';
        if(errorCode==='agent_insufficient_balance')this.blockedUntil=Date.now()+60000;
        throw Object.assign(new Error(payload.kind==='artwork'&&!response.ok&&safeErrors.includes(data.error)?data.error:'agent_unavailable'),{code:errorCode});
      }
      status='ok';return data;
    } catch(error){
      status=controller.signal.aborted&&!timedOut?'cancelled':'error';
      if(timedOut)errorCode='agent_timeout';
      else if(!errorCode&&!controller.signal.aborted)errorCode=error instanceof TypeError&&/fetch|network/i.test(error.message)?'agent_connection_failed':error.code||'agent_invalid_reply';
      if(errorCode)throw Object.assign(new Error(payload.kind==='artwork'&&SAFE_AGENT_ERRORS.includes(error.message)?error.message:'agent_unavailable'),{code:errorCode});throw error;
    } finally {try{this.onTiming({requestId:payload.requestId,kind:payload.kind,mode:payload.context?.host?.turn?.mode||null,status,errorCode,durationMs:Math.round(performance.now()-started)});}catch{}clearTimeout(timer);this.controllers.delete(controller);parentSignal?.removeEventListener('abort',abort);}
  }
  cancel() {
    for (const controller of this.controllers) controller.abort();
    for (const item of this.queue.splice(0)) item.reject(new Error('cancelled'));
  }
}
