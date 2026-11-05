import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {processJson,execFileAsync} from './process.mjs';
import {fail,normalize,hash,inspectWav} from './media.mjs';
import {loadEnvironment} from '../providers.mjs';
const root=fileURLToPath(new URL('../../',import.meta.url));

export async function providerCall(action,query) {
  const runtime=path.join(root,'.runtime/netease-anonymous');
  const config=loadEnvironment(root);
  await fs.mkdir(runtime,{recursive:true,mode:0o700});
  return processJson(process.execPath,[path.join(root,'scripts/netease-worker.cjs')],{
    input:{action,query},cwd:runtime,timeout:['song_url','song_url_v1','song_download_url_v1'].includes(action)?65000:40000,
    env:{PATH:path.dirname(process.execPath)+':/usr/bin:/bin',TMPDIR:runtime,
      ...(config.NETEASE_SESSION_FILE?{NETEASE_SESSION_FILE:config.NETEASE_SESSION_FILE}:{}),
      ENABLE_GENERAL_UNBLOCK:'false',ENABLE_RANDOM_CN_IP:'false',DOTENV_CONFIG_QUIET:'true'}
  });
}

export async function decodeAudio(bytes,{ffmpeg=loadEnvironment(root).FFMPEG_PATH}={}) {
  if (!ffmpeg) {
    try { await execFileAsync('ffmpeg',['-version']); ffmpeg='ffmpeg'; } catch {}
  }
  if (!ffmpeg) {
    try { ffmpeg=(await execFileAsync(path.join(root,'.venv/bin/python'),['-c','import imageio_ffmpeg; print(imageio_ffmpeg.get_ffmpeg_exe())'])).stdout.trim(); }
    catch { fail('audio_decoder_not_installed'); }
  }
  await fs.mkdir(path.join(root,'.runtime'),{recursive:true});
  const dir=await fs.mkdtemp(path.join(root,'.runtime/decode-'));
  try {
    const input=path.join(dir,'source'),output=path.join(dir,'master.wav');
    await fs.writeFile(input,bytes,{mode:0o600});
    // Whole recording; no seek, trim, speed change or silence removal.
    try { await execFileAsync(ffmpeg,['-nostdin','-v','error','-xerror','-i',input,'-map','0:a:0','-vn','-ac','2','-ar','44100','-c:a','pcm_s16le',output],{timeout:90000,maxBuffer:1024*1024}); }
    catch { fail('audio_decode_failed'); }
    const wav=await fs.readFile(output); inspectWav(wav); return wav;
  } finally { await fs.rm(dir,{recursive:true,force:true}); }
}

export async function downloadAudio(url,{expectedSize}={}) {
  const u=new URL(url);
  if (u.username || u.password || u.port || !/(^|\.)music\.126\.net$/.test(u.hostname)) fail('audio_host_not_allowed');
  if (!['http:','https:'].includes(u.protocol)) fail('audio_host_not_allowed');
  u.protocol='https:';
  const r=await fetch(u,{redirect:'error',signal:AbortSignal.timeout(90000)});
  if (!r.ok) fail('audio_download_failed');
  const max=100*1024*1024, parts=[]; let size=0;
  if (Number(r.headers.get('content-length'))>max) {await r.body?.cancel();fail('source_too_large');}
  for await (const chunk of r.body) {size+=chunk.length;if(size>max) fail('source_too_large');parts.push(chunk);}
  if (expectedSize && expectedSize!==size) fail('audio_download_incomplete');
  return Buffer.concat(parts);
}

export class NeteaseSource {
  constructor({id='netease',call=providerCall,download=downloadAudio,decode=decodeAudio}={}) {
    this.id=id;this.call=call;this.download=download;this.decode=decode;
    this.artistEvidenceCache=new Map();
  }
  async artistEvidence(id){
    if(this.artistEvidenceCache.has(id))return this.artistEvidenceCache.get(id);
    const b=await this.call('artist_detail',{id}),d=b.data;
    if(b.code!==200||!d?.artist||String(d.artist.id)!==String(id))throw Error('artist_identity_unavailable');
    const identityLabel=d.identify?.imageDesc||'',tags=d.artist.identifyTag||[],rank=d.artist.rank;
    const basis=/歌手|乐队|组合|singer|band/i.test(identityLabel)?'platform_identity_label':tags.includes('入驻艺人')?'platform_resident_artist':Number.isInteger(rank?.rank)&&rank.rank>0?'platform_artist_rank':null;
    const result={id,name:d.artist.name,eligible:!!basis,basis,identityLabel,tags,rank:rank||null,checkedAt:new Date().toISOString()};
    this.artistEvidenceCache.set(id,result);return result;
  }
  async searchOfficial(q,{onProgress=()=>{}}={}){
    const b=await this.call('cloudsearch',{keywords:[q.title,q.artist,q.version].filter(Boolean).join(' '),limit:100});
    if(b.code!==200||!b.result||!Array.isArray(b.result.songs)&&b.result.songCount!==0)throw Error('search_response_invalid');
    const songs=(b.result.songs||[]).map((s,i)=>({...s,searchRank:i+1})).filter(s=>{
      const titleMatch=normalize(s.name)===normalize(q.title)||(q.version&&normalize(s.name.replace(/\s*[（(].*$/,''))===normalize(q.title));
      const versionMatch=!q.version||normalize(s.name+' '+s.al?.name).includes(normalize(q.version))||/现场|live/i.test(q.version)&&/现场|live/i.test(s.name+' '+s.al?.name);
      return titleMatch&&versionMatch&&(!q.artist||(s.ar||[]).some(a=>normalize(a.name)===normalize(q.artist)));
    });
    const candidates=[],excluded=[],errors=[],seen=new Set();let stoppedEarly=false;
    for(let start=0;start<songs.length;start+=1){
      const batch=await Promise.all(songs.slice(start,start+1).map(async s=>{
        const artist=(s.ar||[]).map(a=>a.name).join(' / '),arrangement=s.name+' '+(s.al?.name||'');
        if(!s.ar?.length||s.ar.some(a=>!(a.id>0))||!s.al?.id||/cover|翻唱|伴奏|纯音乐|串烧|剪辑|片段|remix|dj/i.test(arrangement))return {excluded:{id:s.id,artist,reason:'non_official_or_alternate_arrangement'}};
        try{
          const performerEvidence=await Promise.all(s.ar.map(a=>this.artistEvidence(a.id)));
          if(!performerEvidence.some(e=>e.eligible))return {excluded:{id:s.id,artist,reason:'no_platform_artist_identity'}};
          const externalId=String(s.id);
          return {candidate:{sourceId:this.id,externalId,title:s.name,artist,version:`网易云录音 ${externalId}｜专辑 ${s.al.name||'未知'}`,evidence:`https://music.163.com/song?id=${externalId}`,audio:{songId:externalId,expectedDurationMs:s.dt},lyrics:{songId:externalId},performers:s.ar.map(a=>({id:a.id,name:a.name})),performerKey:String(s.ar[0].id),searchRank:s.searchRank,providerPopularity:s.pop??null,officialEvidence:{eligible:true,basis:'platform_artist_metadata',performers:performerEvidence,albumId:s.al.id,notCopyrightCertification:true}}};
        }catch(e){return {error:{id:s.id,artist,code:/^[a-z_]+$/.test(e.message)?e.message:'artist_identity_unavailable'}};}
      }));
      for(const r of batch){if(r.candidate){candidates.push(r.candidate);seen.add(r.candidate.performerKey);}if(r.excluded)excluded.push(r.excluded);if(r.error)errors.push(r.error);}
      onProgress({stage:'searching',eligibleArtists:seen.size,inspected:Math.min(start+1,songs.length)});
      if(candidates.length||errors.length){stoppedEarly=start+1<songs.length;break;}
    }
    return {candidates,excluded,errors,inspectionComplete:!errors.length,recallScope:'provider_top_100',providerSongCount:b.result.songCount||0,rankBasis:'provider_search_order',countIsLowerBound:stoppedEarly||b.result.songCount>100};
  }
  async search(q) {
    const result=await this.call('cloudsearch',{keywords:[q.title,q.artist].filter(Boolean).join(' '),limit:100});
    if(result.code===200&&result.result?.songCount===0)return [];
    if (result.code!==200 || !result.result || !Array.isArray(result.result.songs)) fail('search_response_invalid');
    if (result.result.songCount>100 && result.result.hasMore) fail('search_candidates_truncated');
    return result.result.songs.filter(s=>normalize(s.name)===normalize(q.title) && (!q.artist || (s.ar||[]).some(a=>normalize(a.name)===normalize(q.artist)))).map(s=>{
      const externalId=String(s.id),version=`网易云录音 ${externalId}｜专辑 ${s.al?.name||'未知'}`;
      return {sourceId:this.id,externalId,title:s.name,artist:s.ar.map(a=>a.name).join(' / '),version,
        evidence:`https://music.163.com/song?id=${externalId}`,
        audio:{songId:externalId,expectedDurationMs:s.dt},lyrics:{songId:externalId}};
    }).filter(s=>!q.version || normalize(q.version)===normalize(s.version));
  }
  async searchCovers(q) {
    const candidates=[],rejected=[],seen=new Set();let searchComplete=false;
    for(let page=0;page<3;page++) {
      const body=await this.call('cloudsearch',{keywords:q.title,limit:100,offset:page*100});
      if(body.code!==200||!body.result)fail('search_response_invalid');
      for(const s of body.result.songs||[]) {
        const externalId=String(s.id);if(seen.has(externalId))continue;seen.add(externalId);
        const base=s.name.replace(/\s*[（(](?:cover|翻唱)[^）)]*[）)]\s*$/i,'').trim();
        if(normalize(base)!==normalize(q.title)||(s.ar||[]).some(a=>normalize(a.name)===normalize(q.artist)))continue;
        const artist=(s.ar||[]).map(a=>a.name).join(' / '),album=s.al?.name||'未知';
        const reason=s.dt<90000?'short_recording':/(伴奏|纯音乐|串烧|片段|剪辑|加速|remix|dj)/i.test(s.name+' '+album)?'unsupported_arrangement':!artist?'artist_missing':null;
        if(reason){rejected.push({externalId,title:s.name,artist,durationMs:s.dt,reason});continue;}
        candidates.push({sourceId:this.id,externalId,title:s.name,artist,version:`网易云录音 ${externalId}｜专辑 ${album}`,evidence:`https://music.163.com/song?id=${externalId}`,audio:{songId:externalId,expectedDurationMs:s.dt},lyrics:{songId:externalId},reference:{kind:'cover_candidate',work:{title:q.title,artist:q.artist},providerTitle:s.name,reviewRequired:true,selectionBasis:'title_match_other_performer_not_proof_of_work_identity'}});
      }
      if((page+1)*100>=(body.result.songCount||0)||body.result.hasMore===false){searchComplete=true;break;}
    }
    return {candidates,rejected,searchComplete};
  }
  async fetch(ref,kind) {
    if (!/^\d+$/.test(ref.songId||'')) fail('source_resource_missing');
    if (kind==='lyrics') {
      const body=await this.call('lyric_new',{id:ref.songId});
      const lrc=body.lrc?.lyric;
      if (body.code!==200 || !lrc?.trim() || body.nolyric || body.uncollected) fail('lyrics_unavailable');
      return {bytes:Buffer.from(lrc),original:Buffer.from(JSON.stringify({songId:ref.songId,lrc:body.lrc,yrc:body.yrc})),
        metadata:{format:'netease-lyrics-json',hasWordTiming:Boolean(body.yrc?.lyric)}};
    }
    if (kind!=='audio') fail('invalid_resource_kind');
    const detail=await this.call('song_detail',{ids:ref.songId});
    const song=detail.songs?.find(s=>String(s.id)===ref.songId);
    if (!song || Math.abs(song.dt-ref.expectedDurationMs)>500) fail('recording_duration_changed');
    // Standard player endpoint; no VIP unlocking or alternative recording fallback.
    const result=await this.call('song_url',{id:ref.songId,br:128000});
    const info=result.data?.find(d=>String(d.id)===ref.songId);
    if (!info) fail('audio_url_response_invalid');
    if (info.freeTrialInfo && info.freeTrialInfo!=='null') fail('audio_trial_only');
    if (!info.url || info.code!==200) fail('audio_access_unavailable');
    if (info.time && Math.abs(info.time-ref.expectedDurationMs)>1500) fail('audio_trial_or_duration_mismatch');
    const original=await this.download(info.url,{expectedSize:info.size});
    const bytes=await this.decode(original), audio=inspectWav(bytes);
    if (Math.abs(audio.durationMs-ref.expectedDurationMs)>500) fail('recording_duration_mismatch');
    return {bytes,original,metadata:{format:info.type||'unknown',sourceAudioSha256:hash(original),
      expectedDurationMs:ref.expectedDurationMs,decodedDurationMs:audio.durationMs,
      access:result.sessionAccess||'anonymous',trial:false,providerSongId:ref.songId,quality:info.level||'standard'}};
  }
}
