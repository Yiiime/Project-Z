import test from 'node:test';
import assert from 'node:assert/strict';
import {DialogueReview} from '../server/shared/ui/dialogue-review.js';
const candidate={id:'host-reply',role:'host',speakerId:'Z',text:'今天很高兴认识你。',replyTo:['selected']};
const selected={id:'selected',speakerId:'virtual_first',name:'海风',text:'今天认识了谁？',mine:false,recipientIds:['z']};
function fixture(){let finish;const review=new DialogueReview({request:p=>new Promise(resolve=>{finish=()=>resolve({decision:'allow',candidateId:p.context.candidate.id,revision:p.context.revision,promptVersion:'test'});})});review.setConcurrent(true);review.coalesceHostAudience=true;review.record({id:selected.id,type:'npc.message',text:selected.text,speakerId:selected.speakerId});review.setWaiting([selected]);return {review,finish:()=>finish()};}
test('busy spectator chat does not restart a reviewed selected reply',async()=>{
 const f=fixture(),pending=f.review.inspect(candidate);await Promise.resolve();
 const fresh={id:'other',speakerId:'virtual_second',text:'晚上好',mine:false,recipientIds:['z']};
 f.review.record({...fresh,type:'npc.message'});f.review.setWaiting([selected,fresh]);f.finish();
 const ticket=await pending;assert(ticket);let published=false;assert(f.review.commit(ticket,()=>published=true));assert(published);
});
test('player input still invalidates a spectator reply during review',async()=>{
 const f=fixture(),pending=f.review.inspect(candidate);await Promise.resolve();
 const player={id:'player-new',speakerId:'player',text:'Z，我有问题',mine:true,recipientIds:['z']};
 f.review.record({...player,type:'player.message'});f.review.setWaiting([selected,player]);f.finish();assert.equal(await pending,null);
});
test('changed selected evidence cannot reuse an old review',async()=>{
 const f=fixture(),pending=f.review.inspect(candidate);await Promise.resolve();
 f.review.setWaiting([{...selected,text:'changed'}]);f.finish();assert.equal(await pending,null);
});

test('virtual chat cannot starve an NPC reaction but new player input still invalidates it',async()=>{
 for(const player of [false,true]){
  const f=fixture(),pending=f.review.inspect({...candidate,role:'audience',speakerId:'liting'});await Promise.resolve();
  const fresh={id:'fresh',speakerId:player?'player':'virtual_second',text:'晚上好',mine:player,recipientIds:['z']};
  f.review.record({...fresh,type:player?'player.message':'npc.message'});f.review.setWaiting([selected,fresh]);f.finish();
  assert.equal(!!(await pending),!player);
 }
});
