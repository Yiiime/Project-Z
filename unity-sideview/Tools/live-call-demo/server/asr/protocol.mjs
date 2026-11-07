import {gzipSync,gunzipSync} from 'node:zlib';

export const ASR_ENDPOINT='wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async';
export function encodePacket(payload,{audio=false,last=false}={}){
  const body=gzipSync(audio?Buffer.from(payload):Buffer.from(JSON.stringify(payload)));
  const length=Buffer.alloc(4);length.writeUInt32BE(body.length);
  return Buffer.concat([Buffer.from([0x11,(audio?0x20:0x10)|(last?2:0),audio?1:0x11,0]),length,body]);
}
export function decodePacket(input){
  const b=Buffer.from(input),type=b[1]>>4,flags=b[1]&15,format=b[2]>>4,compression=b[2]&15;
  let at=(b[0]&15)*4;
  if(b.length<8||b.length>2*1024*1024||b[0]>>4!==1||at<4||![9,15].includes(type)||![0,1].includes(format)||![0,1].includes(compression))throw Error('asr_invalid_packet');
  let code,sequence;
  if(type===15||flags&1){if(b.length<at+8)throw Error('asr_invalid_packet');if(type===15)code=b.readUInt32BE(at);else sequence=b.readInt32BE(at);at+=4;}
  if(b.length<at+4)throw Error('asr_invalid_packet');const size=b.readUInt32BE(at);at+=4;
  if(at+size!==b.length)throw Error('asr_invalid_packet');
  const bytes=compression?gunzipSync(b.subarray(at),{maxOutputLength:2*1024*1024}):b.subarray(at);
  if(type===15)throw Object.assign(Error('asr_provider_error'),{providerCode:code});
  const body=JSON.parse(bytes.toString('utf8'));
  if(body.code!==undefined&&![0,1000,20000000].includes(body.code))throw Object.assign(Error('asr_provider_error'),{providerCode:body.code});
  return {body,sequence,last:!!(flags&2)||body.is_last_package===true};
}
export function recognitionResults(body,{last=false}={}){
  const result=Array.isArray(body.result)?body.result[0]:body.result;
  if(!result)return [];
  const utterances=result.utterances;
  if(Array.isArray(utterances)&&utterances.length){
    if(utterances.length>128)throw Error('asr_result_too_large');
    return utterances.filter(u=>typeof u.text==='string'&&u.text.trim()).map(u=>({text:u.text.slice(0,4000),final:last||u.definite===true,startMs:Number(u.start_time)||0}));
  }
  return typeof result.text==='string'&&result.text.trim()?[{text:result.text.slice(0,4000),final:last,startMs:0}]:[];
}
