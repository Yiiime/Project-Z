import {EventEmitter} from 'node:events';
import {randomUUID} from 'node:crypto';
const clean=s=>s.normalize('NFKC').replace(/[^\p{L}\p{N}]/gu,'');
// A bounded, replayable in-memory stream. No audio/room credentials in saves.
export class PcmSpeech extends EventEmitter {
 constructor(text,open){super();this.id=randomUUID();this.text=text;this.records=[];this.bytes=0;this.words=[];this.done=false;this.closed=false;this.controller=new AbortController();this.ready=this.read(open);this.ready.catch(()=>{});}
 publish(event){this.records.push(event);this.emit('record',event);}
 async read(open){let reader;try{
  const response=await open(this.controller.signal);if(!response.ok)throw Error('tts_unavailable');reader=response.body.getReader();let pending='',index=0,started=false;const decoder=new TextDecoder();
  const record=line=>{const e=JSON.parse(line);if(e.type==='error')throw Error('tts_unavailable');
   if(e.type==='start'){if(started||e.format!=='pcm'||e.sampleRate!==24000)throw Error('invalid_pcm_stream');started=true;this.publish({type:'start',sampleRate:24000});}
   else if(e.type==='audio'){if(!started||e.index!==index++||typeof e.data!=='string'||!/^[A-Za-z0-9+/]+={0,2}$/.test(e.data))throw Error('invalid_pcm_stream');const bytes=Buffer.from(e.data,'base64');this.bytes+=bytes.length;if(!bytes.length||bytes.length%2||this.bytes>4320000)throw Error('invalid_pcm_stream');this.publish({type:'audio',index:e.index,data:e.data});}
   else if(e.type==='sentence'){if(!started||!Array.isArray(e.sentence?.words))throw Error('invalid_pcm_stream');this.words.push(...e.sentence.words);if(this.words.length>2000||!clean(this.text).startsWith(clean(this.words.map(w=>w.word||'').join(''))))throw Error('transcript_unverified');this.publish({type:'words',words:e.sentence.words});}
   else if(e.type==='done'){if(!started||!this.bytes||clean(this.text)!==clean(this.words.map(w=>w.word||'').join('')))throw Error('transcript_unverified');this.done=true;this.durationMs=this.bytes/48;this.publish({type:'done',durationMs:this.durationMs});}
   else throw Error('invalid_pcm_stream');
  };
  while(!this.done&&!this.closed){const next=await reader.read();if(next.done)break;pending+=decoder.decode(next.value,{stream:true});if(pending.length>2000000)throw Error('pcm_event_too_large');let at;while((at=pending.indexOf('\n'))>=0){const line=pending.slice(0,at);pending=pending.slice(at+1);if(line.trim())record(line);if(this.done)break;}}
  if(!this.done&&!this.closed)throw Error('tts_incomplete');
 }catch(error){if(!this.closed){this.error=error.message;this.publish({type:'error',code:'tts_stream_failed'});}throw error;}finally{await reader?.cancel().catch(()=>{});reader?.releaseLock();}}
 close(){if(this.closed)return;this.closed=true;this.controller.abort();this.emit('closed');this.removeAllListeners();}
 response(res){if(this.closed)throw Error('stale_audio_stream');res.writeHead(200,{'Content-Type':'application/x-ndjson','Cache-Control':'no-store','X-Accel-Buffering':'no'});
  const send=e=>{if(res.destroyed)return;res.write(JSON.stringify(e)+'\n');if(res.writableLength>6*1024*1024)res.destroy();if(e.type==='done'||e.type==='error')res.end();};
  const closed=()=>res.destroy();this.on('record',send);this.once('closed',closed);res.once('close',()=>{this.off('record',send);this.off('closed',closed);});for(const e of this.records)send(e);
 }
}
