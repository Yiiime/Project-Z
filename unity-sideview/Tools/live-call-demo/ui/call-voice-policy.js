// Shared by the live Demo and the call-companion package. No vendor/model inference.
export const CALL_VOICE_DEFAULTS = Object.freeze({pauseMs:1200, incompletePauseMs:2200, maxNoiseWaitMs:700, restartMs:200, maxTurnMs:30000, maxText:360, minConfidence:.55, visibleRows:2, previewChars:36});
const clean = value => String(value ?? '').normalize('NFKC').trim();
const lexical = value => clean(value).replace(/[^\p{L}\p{N}]/gu,'').toLowerCase();
// Text echo guard, not acoustic echo cancellation or speaker identification.
export function filterCallRecognitionResults(results,hostTexts=[],ignored=new Set()) {
  const echoes=hostTexts.map(lexical).filter(Boolean),output=[];
  for(const [index,result] of Array.from(results??[]).entries()){
    if(ignored.has(index))continue;
    const value=lexical(result?.[0]?.transcript);if(!value)continue;
    const echo=echoes.some(host=>{
      if(host.includes(value))return true;
      if(value.length<4||host.length<4)return false;
      const pairs=new Set(Array.from({length:host.length-1},(_,i)=>host.slice(i,i+2)));
      let overlap=0;for(let i=0;i<value.length-1;i++)if(pairs.has(value.slice(i,i+2)))overlap++;
      return overlap/(value.length-1)>=.85;
    });
    if(echo){if(result.isFinal)ignored.add(index);continue;}
    output.push(result);
  }
  return output;
}
export function readRecognitionResults(results) {
  let finalText='', interimText='', uncertain=false;
  for (const result of Array.from(results ?? [])) {
    const alternative=result?.[0]; if (!alternative) continue;
    const value=clean(alternative.transcript); if (!value) continue;
    if (result.isFinal) {
      finalText+=value;
      const confidence=Number(alternative.confidence);
      uncertain ||= !Number.isFinite(confidence) || confidence<=0 || confidence<CALL_VOICE_DEFAULTS.minConfidence;
    } else interimText+=value;
  }
  return {finalText,interimText,uncertain};
}
export function turnDisposition({text, final=true, uncertain=false, manual=false}) {
  const value=clean(text), words=value.replace(/[^\p{L}\p{N}]/gu,'');
  if (!words) return 'empty';
  if (!manual && /^(嗯+|啊+|呃+|唔+|哦+)$/.test(words)) return 'filler';
  if (!final || uncertain || value.length>CALL_VOICE_DEFAULTS.maxText) return 'review';
  if (!manual && /(?:因为|然后|但是|不过|还有|比如|我觉得|其实|所以说|我想|我想说|我想要|如果|虽然|就是|以及|关于)[，,、。.!！?？\s]*$/.test(value)) return 'incomplete';
  return 'send';
}
// Display-only state: drafts/planned speech never become conversation memory.
export class CallTranscriptWindow {
  constructor({limit=CALL_VOICE_DEFAULTS.visibleRows}={}) {this.limit=limit;this.rows=[];}
  upsert({id,role,text='',status='draft',fullText=null}) {
    if (!id || !['guest','assistant'].includes(role)) throw Error('invalid_caption');
    let row=this.rows.find(item=>item.id===id);
    if (!row) {if(this.limit===2)this.rows=this.rows.filter(item=>item.role!==role);row={id,role,text:'',status};this.rows.push(row);}
    Object.assign(row,{text:String(text),status,fullText:fullText===null?String(text):String(fullText)});
    this.rows=this.rows.slice(-this.limit);return row;
  }
  update(id,patch) {const row=this.rows.find(item=>item.id===id);if(row)Object.assign(row,patch);return row;}
  remove(id) {this.rows=this.rows.filter(item=>item.id!==id);}
  clear() {this.rows=[];}
  snapshot() {return this.rows.map(row=>({...row}));}
}
