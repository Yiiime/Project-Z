import {randomUUID} from 'node:crypto';
import {normalize} from './media.mjs';
import {retrievalFailure} from './retrieval-feedback.mjs';
export function retrievalPolicy(){
 return {id:'official_source_v1',selection:'top1_matching_official',askVersion:false,rankBasis:'provider_search_order',recallLimit:100,coverFallback:false,materialListeningReview:false};
}
export class OfficialRetrieval {
 constructor(library,policy=retrievalPolicy()){this.library=library;this.policy=policy;}
 async prepare(plan,{selectedId,lookup:priorLookup,onProgress=()=>{}}={}){
  const l=this.library,source=l.sources.get('netease');
  let lookup=priorLookup;
  if(!selectedId){
   const result=await source.searchOfficial(plan,{onProgress});
   const candidates=result.candidates.map(d=>{const r=l.register({...d,descriptor:d});return {id:r.id,title:r.title,artist:r.artist,version:r.version,rank:d.searchRank,officialEvidence:d.officialEvidence,performerKey:d.performerKey};});
   // Provider order is the ranking. Album duplicates do not add singers.
   const unique=[...new Map(candidates.map(c=>[c.performerKey,candidates.find(x=>x.performerKey===c.performerKey)])).values()];
   lookup={...result,candidates:unique,artistCount:unique.length,policy:this.policy};
   const saved={...lookup,candidates:unique};l.db.prepare('INSERT INTO requests VALUES(?,?,?,?)').run(randomUUID(),JSON.stringify({title:plan.title,artist:plan.artist||'',version:plan.version||'',policy:this.policy.id}),JSON.stringify(saved),new Date().toISOString());
   if(!result.inspectionComplete)return {status:'failed',outcome:'search_incomplete',lookup,error:'official_search_incomplete',policy:this.policy};
   if(!unique.length)return {status:'unavailable',outcome:'official_not_found',lookup,error:'official_source_not_found',policy:this.policy};
   selectedId=unique[0].id;
  }
  if(!lookup?.candidates.some(c=>c.id===selectedId))throw Error('invalid_recording_selection');
  const recording=l.recording(selectedId),evidence=recording.descriptor?.officialEvidence;
  if(!evidence?.eligible||recording.descriptor?.reference?.kind==='cover_candidate')throw Error('official_source_required');
  if(plan.artist&&!recording.descriptor.performers.some(a=>normalize(a.name)===normalize(plan.artist)))throw Error('requested_artist_mismatch');
  const selected=lookup.candidates.find(c=>c.id===selectedId);
  onProgress({selected,stage:'downloading'});
  let m=l.material(selectedId);
  if(m.review?.approved===false)throw Error('material_previously_rejected');
  if(!m.audioAvailable||!m.lyricsAvailable){l.enqueue(selectedId);await l.runNext({recordingId:selectedId});m=l.material(selectedId);}
  if(!m.audioAvailable||!m.lyricsAvailable){const error=l.job(selectedId)?.error||'material_unavailable';return {status:'unavailable',outcome:'official_audio_unavailable',recordingId:selectedId,selected,lookup,error,policy:this.policy,attempts:[{recordingId:selectedId,error}],...(retrievalFailure(error)||{})};}
  m=l.acceptBySourcePolicy(selectedId,evidence);
  return {status:'completed',outcome:'retained',error:null,recordingId:selectedId,selected,lookup,policy:this.policy,materialStatus:m.status,durationMs:m.audio.durationMs,fallback:false,attempts:[{recordingId:selectedId,error:null}],generationAllowed:true};
 }
}
