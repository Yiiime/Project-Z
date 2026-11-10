// Only this projection may cross from a save into a public broadcast.
const anchors = [
  {id:'childhood_friend', layer:'anchor', topics:['发小','童年','小时候','李庭','咖啡店老板'], aliases:[['是我的发小','是我发小','从小就是朋友'],['开咖啡店','经营咖啡店','咖啡店的老板'],['出行闹过别扭','出门闹过别扭','出游吵过架']], steps:[
    '李庭是我的发小','他现在是咖啡店老板','我们因为一次出行闹过别扭']},
  {id:'family_choices', layer:'anchor', topics:['父母','爸妈','家里人'], aliases:[['我很信任他们','我信得过爸妈'],['他们更清楚','他们比我清楚'],['自己决定去哪','自己选要走的路']], steps:[
    '我挺信任爸妈的','他们总说他们更清楚','我想自己决定要去哪儿']},
];
const crack = {id:'conflicting_evidence', layer:'crack', topics:['对不上','不对劲','证据','矛盾','那份记录'], aliases:[['我记得不一样','和我记得的不一样'],['两件事对不上','这两件事不一样'],['我想再核实','我得亲自确认']], steps:[
  '等一下，我记得不太一样','这两件事我还对不上','我想自己再核实一次']};
const labels=['hinted','unfolding','unfolded'];
const normalize=s=>s.replace(/[\p{P}\p{Z}\s]/gu,'');
export function matchesStep(text, line) {
  return typeof text==='string' && line && text.split(/[。！？!?]/u).some(clause=>{
    const value=normalize(clause), at=value.indexOf(normalize(line));
    return at>=0 && !/(?:不|并非|没有说|不能说|难道)$/.test(value.slice(0,at));
  });
}
export function isPublic(event, world) {
  if (!event.visible?.includes('z') || event.channel==='private_player_chat') return false;
  if (event.channel==='livestream') return true;
  return event.kind==='fact' && event.speaker==='engine' &&
    ['player',...Object.keys(world.actors)].every(id=>event.disclose_to?.includes(id));
}
export function memoryProjection(world, events, {privateChat=false}={}) {
  const visible=events.filter(e=>isPublic(e,world) || (privateChat &&
    e.visible?.includes('z') && e.visible.includes('player') && e.channel==='private_player_chat'));
  const info=world.actors.z.info;
  const evidence=visible.findLast(e=>e.kind==='fact' && e.speaker==='engine' &&
    e.channel!=='livestream' && /矛盾|不符|对不上|不一致/.test(e.text));
  const cards=[...anchors];
  if(evidence && world.day>=2 && info.trust>=50 && ['tense','anxious','scared','shocked','紧张','害怕','焦虑','震惊'].includes(info.emotion))
    cards.push({...crack,evidenceId:evidence.id});
  const history=(world.zNarrated||[]).filter(r=>privateChat||r.channel==='livestream');
  const stories=cards.map(card=>{
    const count=history.reduce((n,r)=>r.id===card.id&&r.step===n+1?n+1:n,0);
    const depth=card.layer==='crack'?3:info.trust<40||info.emotion==='tired'?1:info.trust<50?2:3;
    return {...card,steps:card.steps.slice(0,Math.max(depth,count)),aliases:card.aliases.slice(0,Math.max(depth,count)),count,status:labels[count-1]||null};
  });
  const eligible=visible.filter(e=>e.channel==='livestream'||e.channel==='private_player_chat'||e.kind==='fact');
  const retained=new Set([...eligible.filter(e=>e.channel==='livestream'&&e.speaker==='engine').slice(-8),
    ...eligible.filter(e=>e.speaker==='player').slice(-8),...eligible.slice(-16)]);
  const entries=eligible.filter(e=>retained.has(e)).map(e=>({id:e.id,day:1+Math.floor(e.hour/24),speaker:e.speaker,
      layer:(e.channel==='private_player_chat'||e.subject==='player_and_z')?'shared':'daily',text:e.text.slice(0,240)}));
  return {day:world.day,stage:evidence?"verified_clue":"island_daily",entries,stories};
}
export function narrativeChange(offer,text) {
  if (!offer || offer.count>=offer.steps.length || ![offer.steps[offer.count],...(offer.aliases?.[offer.count]||[])].some(s=>matchesStep(text,s))) return null;
  return {id:offer.id,step:offer.count+1};
}
export function rememberNarration(world,change,channel,eventId){
  if(!change)return;
  const history=world.zNarrated||=[];
  if(!history.some(r=>r.id===change.id&&r.step===change.step&&r.channel===channel))
    history.push({...change,channel,eventId});
}
export function privateOffer(projection,topic,prior){
  const follow=/然后|后来|再讲|继续|接着|还有呢/.test(topic);
  return projection.stories.find(s=>s.count<s.steps.length && (s.topics.some(t=>topic.includes(t)) ||
    follow&&s.count>0&&(s.steps.slice(0,s.count).some(line=>matchesStep(prior,line))||!prior)));
}

