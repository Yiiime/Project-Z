/** Caption timing only. Never requests or plays audio. */
export class TextDirector extends EventTarget {
  constructor(){super();this.current=null;this.paused=false;this.timer=null;this.next=null;this.deadline=0;this.remaining=0;}
  emit(type,detail){this.dispatchEvent(new CustomEvent(type,{detail}));}
  later(callback,delay){clearTimeout(this.timer);this.next=callback;this.remaining=delay;this.deadline=performance.now()+delay;if(!this.paused)this.timer=setTimeout(()=>{this.next=null;callback();},delay);}
  speak(detail){
    this.cancel();const current={id:detail.id??detail.speechId,chars:Array.from(detail.text),index:0,done:false};this.current=current;
    this.emit('text',{id:current.id,text:''});this.emit('activity',{id:current.id,speaking:true});
    const tick=()=>{if(this.current!==current||current.done)return;current.index++;
      this.emit('text',{id:current.id,text:current.chars.slice(0,current.index).join('')});
      if(current.index>=current.chars.length){this.hold(current);return;}
      const char=current.chars[current.index-1];this.later(tick,/[。！？!?]/.test(char)?280:/[，、；：,.]/.test(char)?150:72);
    };this.later(tick,80);
  }
  hold(current){this.emit('activity',{id:current.id,speaking:false});this.later(()=>{if(this.current!==current||current.done)return;current.done=true;this.emit('complete',{id:current.id});},1100);}
  finishNow(){const current=this.current;if(!current||current.done)return;clearTimeout(this.timer);current.index=current.chars.length;this.emit('text',{id:current.id,text:current.chars.join('')});this.hold(current);}
  pause(){if(this.paused)return;this.paused=true;this.remaining=Math.max(0,this.deadline-performance.now());clearTimeout(this.timer);}
  resume(){if(!this.paused)return;this.paused=false;if(this.next)this.later(this.next,this.remaining);}
  cancel(){clearTimeout(this.timer);this.next=null;if(this.current)this.emit('activity',{id:this.current.id,speaking:false});this.current=null;}
  destroy(){this.cancel();}
}
