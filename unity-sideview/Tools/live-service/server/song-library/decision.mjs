import {normalize} from './media.mjs';
import {selector,Z_VOICE} from './library.mjs';
export const ROUTES={login_required:'需要登录网易云',cached:'已有存储直接唱',generate:'有歌曲 · 在线生歌',missing:'无歌曲落库',clarify:'需要补充信息',unavailable:'指定音源暂不可用'};
export function planSelector(plan) {
  const s=plan.segment;
  if(s.kind==='lyric_start')return selector({kind:s.kind,text:s.lyricStart,...(s.ordinal>1?{occurrence:s.ordinal}:{})});
  return selector(s);
}
export function decide(library,plan,{officialOnly=false,recordingId=null}={}) {
  if(plan.intent!=='request')return {route:'clarify',reason:plan.question||'这句话没有明确的点歌请求。'};
  if(['full','other'].includes(plan.segment.kind))return {route:'clarify',reason:'当前生成链路支持第一句、主歌、副歌、桥段或90秒以内的明确范围，请指定一个唱段。'};
  const scope=planSelector(plan);
  let records=library.db.prepare('SELECT id FROM recordings').all().map(r=>library.recording(r.id)).filter(r=>r.descriptor?.reference?.kind!=='cover_candidate').filter(r=>(normalize(r.title)===normalize(plan.title)||(plan.version&&normalize(r.title.replace(/\s*[（(].*$/,''))===normalize(plan.title)))&&(!plan.artist||normalize(r.artist)===normalize(plan.artist)||r.descriptor?.performers?.some(a=>normalize(a.name)===normalize(plan.artist)))&&(!plan.version||normalize(r.title+' '+r.version).includes(normalize(plan.version))||/现场|live/i.test(plan.version)&&/现场|live/i.test(r.title+' '+r.version)));
  const rejected=records.filter(r=>library.material(r.id).status==='review_rejected');
  records=records.filter(r=>library.material(r.id).status!=='review_rejected');
  if(!records.length&&rejected.length)return {route:'unavailable',scope,reason:'material_previously_rejected'};
  if(officialOnly)records=records.filter(r=>{const st=library.state(r.id,scope,Z_VOICE);return st.masteredSegments.length||st.material.audioAvailable&&st.material.lyricsAvailable;});
  if(recordingId)records=records.filter(r=>r.id===recordingId);
  if(officialOnly&&records.length>1){
    // Reuse the last selection for the same explicit constraints; otherwise let search rank decide.
    const saved=library.db.prepare('SELECT input, result FROM requests ORDER BY created_at DESC,rowid DESC').all();
    const choice=saved.find(row=>{try{const q=JSON.parse(row.input);return normalize(q.title)===normalize(plan.title)&&normalize(q.artist||'')===normalize(plan.artist||'')&&normalize(q.version||'')===normalize(plan.version||'')&&JSON.parse(row.result).policy?.selection==='top1_matching_official';}catch{return false;}});
    const id=choice?JSON.parse(choice.result).candidates?.[0]?.id:null;
    const selected=records.find(r=>r.id===id);
    if(!selected)return {route:'missing',scope,reason:'本地有多个版本，按当前点歌条件检索 Top1，不向观众追问版本。',candidates:records.map(summary)};
    records=[selected];
  }
  if(!officialOnly&&new Set(records.map(r=>normalize(r.artist))).size>1)return {route:'clarify',reason:'曲库里有不同歌手的同名歌曲，请补充歌手。',candidates:records.map(summary)};
  const states=records.map(r=>library.state(r.id,scope,Z_VOICE));
  const hits=states.filter(s=>s.assetId);
  if(hits.length>1)return {route:'clarify',reason:'有多个版本的合格唱段，请选择版本。',candidates:records.map(summary)};
  if(hits.length===1){const s=hits[0];return {route:'cached',recording:s.recording,assetId:s.assetId,scope,reason:'命中范围与当前 Z 音色一致、技术检查通过且文件完整的唱段。',evidence:compact(s),versionNote:s.recording.version.includes('待绑定')?'使用原 demo 可用唱段；尚未绑定网易云原曲时间轴。':null};}
  const learned=states.find(s=>s.masteredSegments.length);
  if(learned)return {route:'generate',recording:learned.recording,scope,reason:'这首歌已有可用的 Z 唱段；复用歌曲身份，跳过检索，后台准备新片段。',knownScopes:learned.masteredSegments.map(x=>x.segment.kind),generationMode:'vivi_text_request',referenceConditioning:false};
  const references=library.db.prepare('SELECT id FROM recordings').all().map(x=>library.recording(x.id)).filter(r=>r.descriptor?.reference?.kind==='cover_candidate'&&normalize(r.descriptor.reference.work.title)===normalize(plan.title)&&(!plan.artist||normalize(r.descriptor.reference.work.artist)===normalize(plan.artist))&&library.material(r.id).status==='ready');
  if(!officialOnly&&!plan.version&&references.length===1)return {route:'generate',recording:summary(references[0]),scope,reason:'已有通过试听的歌曲参考资料，跳过检索，后台准备唱段；Vivi 仍使用文本指令。',generationMode:'vivi_text_request',referenceConditioning:false};
  const materials=states.filter(s=>s.material.status==='ready');
  if(materials.length>1)return {route:'clarify',reason:'有多个已落库录音，请选择版本。',candidates:materials.map(s=>summary(s.recording))};
  if(materials.length===1){const s=materials[0];return {route:'generate',recording:s.recording,scope,reason:'整曲音频与歌词已保存，但所点范围没有合格 Z 唱段。',evidence:compact(s),knownScopes:[...new Set(states.flatMap(x=>x.masteredSegments.map(a=>a.segment.kind)))],generationMode:'vivi_text_request',referenceConditioning:false};}
  return {route:'missing',scope,reason:'本地没有可用的整曲音频和歌词，也没有所点范围的合格 Z 唱段。',partialMastery:states.flatMap(s=>s.masteredSegments),candidates:records.map(summary)};
}
const summary=r=>({id:r.id,title:r.title,artist:r.artist,version:r.version});
function compact(s){return {materialStatus:s.material.status,audioAvailable:s.material.audioAvailable,lyricsAvailable:s.material.lyricsAvailable,indexStatus:s.material.indexStatus,mastery:s.status,locatorStatus:s.locator.status,voice:Z_VOICE};}
