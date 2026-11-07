/** Same-origin text API. Secrets and provider credentials stay on the server. */
export class ChatClient {
  constructor({fetchImpl=globalThis.fetch?.bind(globalThis)}={}){this.fetch=fetchImpl;this.controllers=new Set();}
  async configure(){
    if(globalThis.location?.protocol==='file:'||!this.fetch)return {enabled:false};
    const controller=new AbortController(),timer=setTimeout(()=>controller.abort(),6000);
    try{const response=await this.fetch('/api/chat/config',{signal:controller.signal,cache:'no-store'});if(!response.ok)return {enabled:false};return await response.json();}catch{return {enabled:false};}finally{clearTimeout(timer);}
  }
  async generate(payload){
    const controller=new AbortController();this.controllers.add(controller);const timer=setTimeout(()=>controller.abort(),24000);
    try{
      const response=await this.fetch('/api/chat',{method:'POST',headers:{'Content-Type':'application/json'},signal:controller.signal,body:JSON.stringify({...payload,requestId:payload.requestId||crypto.randomUUID()})});
      const data=await response.json();if(!response.ok||typeof data.text!=='string'||!data.text.trim())throw new Error(data.message||'文字连接暂时没有回应');return data;
    }catch(error){if(error.name==='AbortError')throw new Error('等待文字回复超时');throw error;}
    finally{clearTimeout(timer);this.controllers.delete(controller);}
  }
  cancel(){for(const controller of this.controllers)controller.abort();this.controllers.clear();}
}
