// Keep the maximum message bounded, allowing a full base64 PCM envelope within
// the existing 180-second audio cap. ws owns fragmentation, control frames and masking.
import WebSocket from 'ws';
export const VIVI_AUDIO_BYTES=180*48000;
export function connectVivi(url,secret){
 return new WebSocket(url,{headers:{'X-Api-Key':secret},followRedirects:false,perMessageDeflate:false,
   handshakeTimeout:25000,maxPayload:4*Math.ceil(VIVI_AUDIO_BYTES/3)+65536});
}
