import {readFile} from 'node:fs/promises';
import {randomUUID} from 'node:crypto';

// The live process owns the singing database, worker and provider credentials.
export function localSongSubmit(connectionFile){
  return async ({worldId,day,title,sourceId})=>{
    const {baseUrl,token}=JSON.parse(await readFile(connectionFile,'utf8'));
    const url=new URL(baseUrl);
    if(url.protocol!=='http:'||url.hostname!=='127.0.0.1'||!token)throw Error('live_connection_unavailable');
    const sessionId='day:'+worldId+':'+day;
    // Transport attempts can be retried after a transient HTTP failure. The durable
    // singing key is (owner, sessionId, sourceId), including after a lost response.
    const response=await fetch(new URL('/api/singing/tasks',url),{method:'POST',redirect:'error',
      headers:{Authorization:'Bearer '+token,'Content-Type':'application/json','Idempotency-Key':randomUUID()},
      body:JSON.stringify({sessionId,sourceId,text:'请学唱《'+title+'》'}),signal:AbortSignal.timeout(4000)});
    if(!response.ok)throw Error('song_dispatch_unavailable');
    const task=await response.json();if(!task.id)throw Error('song_dispatch_unconfirmed');
  };
}
export class DaySongDispatch {
  constructor(submit){
    this.submit=submit;this.waiting=new Map();this.sent=new Set();
    this.timer=setInterval(()=>this.tick(),2000);this.timer.unref();
  }
  resetWorld(worldId){
    for(const [key,item] of this.waiting)if(item.worldId===worldId)this.waiting.delete(key);
  }
  update(worldId,day,songs){
    const active=new Set();
    for(const song of songs.filter(s=>s.dispatch)){
      const key=worldId+'|'+day+'|'+song.sourceId;active.add(key);
      if(!this.sent.has(key)&&!this.waiting.has(key))this.waiting.set(key,{worldId,day,title:song.title,sourceId:song.sourceId});
    }
    for(const [key,item] of this.waiting)if(item.worldId===worldId&&item.day===day&&!active.has(key))this.waiting.delete(key);
    void this.tick();
  }
  async tick(){
    if(this.running)return;this.running=true;
    try{
      for(const [key,item] of this.waiting){
        if((item.retryAt||0)>Date.now())continue;
        try{await this.submit(item);this.sent.add(key);this.waiting.delete(key);}
        catch{item.retryAt=Date.now()+5000;}
      }
    }finally{this.running=false;}
  }
  close(){clearInterval(this.timer);this.waiting.clear();}
}
