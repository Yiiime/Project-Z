// Server-owned review projection. No generated summaries, keyword routing or
// candidate self-attestation. Preserve every player correction and cited fact.
export const REVIEW_ISSUES=['contradiction','unsupported_fact','private_memory','false_action','fabricated_reaction'];
export const QUALITY_ISSUES=['repetitive','over_questioning','overlong','missed_request'];
import {AUDIENCE_NAMES} from '../audience-roster.js';
export const SPEAKER_NAMES={Z:'Z',player:'连麦来客',...AUDIENCE_NAMES};
const key=e=>JSON.stringify([e.id,e.type,e.text,e.speakerRole,e.speakerId,e.status,e.speechId,e.replyTo]);
export function releaseInput(c,{expanded=false,persona='',world={}}={}) {
 const seen=new Set(),all=[...(c.sessionEvents||[]),...(c.publicEvents||[])].filter(e=>{const k=key(e);if(seen.has(k))return false;seen.add(k);return true;});
 const cited=new Set([...(c.candidate?.replyTo||[]),...(c.candidate?.evidenceIds||[])]);
 const recent=new Set((c.publicEvents||[]).slice(-8).map(key));
 const selected=expanded?all:all.filter(e=>recent.has(key(e))||e.speakerRole==='player'||e.type==='player.message'||e.speakerRole==='system'||cited.has(e.id));
 const tasks=c.singing?.tasks||[],target=c.candidate?.plannedTool?.targetId;
 return {revision:c.revision,candidate:c.candidate,publicEvents:selected,waiting_messages:c.waiting_messages||[],speakerNames:SPEAKER_NAMES,
  historyOmitted:all.length-selected.length,personaExpanded:expanded,
  characterReference:expanded?{material:persona,world}:{identity:'Z，明日岛明星主播。好奇、外向、护短，遇明确反证会认错。直播是工作。黑色披发、月牙发卡、紫色开衫、深蓝长裙。手里常拿一张不断添改的小岛地图。',world,scope:'只证明角色设定，不证明本场经历；需要更多设定时请求完整资料。'},
  executionContext:{plannedTool:c.candidate?.plannedTool||null,playbackScheduled:['play_prepared_song','start_singing','replay_talent'].includes(c.candidate?.plannedTool?.name),
   taskStates:tasks.map(t=>({id:t.id,status:t.status,title:t.title})),...(target?{targetTask:tasks.find(t=>t.id===target)||null}:{})}};
}
export function hardReviewCheck(c) {
 const all=[...(c.publicEvents||[]),...(c.sessionEvents||[])],known=new Set([...all,...(c.waiting_messages||[]),...(c.singing?.tasks||[])].map(e=>e.id));
 for(const id of [...c.candidate.replyTo,...c.candidate.evidenceIds])if(!known.has(id))throw Error('review_unknown_reference');
 const tool=c.candidate.plannedTool;
 if(tool?.name==='play_prepared_song'&&!c.singing?.tasks?.some(t=>t.id===tool.targetId&&t.status==='ready'))throw Error('review_tool_not_ready');
}
