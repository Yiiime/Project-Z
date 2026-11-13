import {AUDIENCE_NAMES} from './audience-roster.js';
const names={z:'Z',player:'玩家',...AUDIENCE_NAMES};
const canonical=id=>id==='Z'?'z':id;
const virtualAudienceId=id=>typeof id==='string'&&/^virtual_[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id);
export function recipientIds(value,speaker='player'){
 if(!Array.isArray(value)||value.length>8||new Set(value).size!==value.length||value.some(id=>(!Object.hasOwn(names,id)&&!virtualAudienceId(id))||id===canonical(speaker)))throw Error('invalid_recipient_ids');
 return [...value];
}
// Explicit UI selection/@/vocatives are routing evidence. Merely discussing a
// person's name is not addressing them. Ambiguous follow-ups keep their partner.
export function addressMessage(text,{speakerId='player',explicit,previous=['z'],replyTo=null,events=[]}={}){
 if(explicit!==undefined)return recipientIds(explicit,speakerId);
 const s=String(text||'').trim(),explicitNames=[],leadingNames=[],trailingNames=[];
 for(const [id,name] of Object.entries(names)){
  if(id===canonical(speakerId))continue;
  const escaped=name.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');
  const named=new RegExp('(?:^|[\\s，,。！!？?])@'+escaped+'(?:$|[\\s，,。！!？?]|你|请|能|唱)','i').test(s);
  const leading=new RegExp('^'+escaped+'(?:[，,：:\\s]|你|请|能|在吗)','i').test(s);
  const trailing=new RegExp('[你您].{0,48}'+escaped+'[！!？?。\\s]*$','i').test(s);
  if(named)explicitNames.push(id);if(leading)leadingNames.push(id);if(trailing)trailingNames.push(id);
 }
 if(explicitNames.length)return explicitNames;
 if(leadingNames.length)return leadingNames;
 if(trailingNames.length)return trailingNames;
 const parent=events.find(e=>e.id===replyTo);
 if(parent?.speakerId&&Object.hasOwn(names,canonical(parent.speakerId))&&canonical(parent.speakerId)!==speakerId)return [canonical(parent.speakerId)];
 return speakerId==='player'?recipientIds(previous,speakerId):[];
}
export function pendingFor(message,actor){return (message.pendingRecipientIds||message.recipientIds||['z']).includes(actor);}
export function hostMessage(message){const {pendingRecipientIds,...rest}=message;return rest;}
