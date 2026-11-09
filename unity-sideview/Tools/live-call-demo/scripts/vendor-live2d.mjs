#!/usr/bin/env node
/** Prepare the pinned Live2D demo assets only after explicit license acceptance. */
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';
import {createHash} from 'node:crypto';
import {fileURLToPath,pathToFileURL} from 'node:url';

const PROJECT_ROOT=fileURLToPath(new URL('../',import.meta.url));
export const HARU_COMMIT='ba0d4ed65e2e62736bf68ba9f237971fc9df79ac';
const DISPLAY_COMMIT='12148332b8838ca00ad76764e0e7521abf980e3b';
const PIXI_COMMIT='8cdbf55064b7adc05f65c51e177f1c22f7329f0f';
const MODEL_BASE=`https://raw.githubusercontent.com/Live2D/CubismWebSamples/${HARU_COMMIT}/Samples/Resources/Haru/`;
export const CORE_URL='https://cubism.live2d.com/sdk-web/core/05/live2dcubismcore.min.js';
const MODEL_LICENSE='Live2D Free Material License Agreement + Live2D Cubism Sample Data Terms';
const ALLOWED_HOSTS=new Set(['pixijs.download','cdn.jsdelivr.net','raw.githubusercontent.com','cubism.live2d.com','www.live2d.com']);
const MODEL_SIZES={
  'Haru.model3.json':2090,'Haru.moc3':384704,
  'Haru.2048/texture_00.png':1526786,'Haru.2048/texture_01.png':1188664,
  'Haru.physics3.json':5914,'Haru.pose3.json':274,'Haru.cdi3.json':5536,'Haru.userdata3.json':313,
  'expressions/F01.exp3.json':124,'expressions/F02.exp3.json':464,
  'expressions/F03.exp3.json':739,'expressions/F04.exp3.json':841,
  'expressions/F05.exp3.json':474,'expressions/F06.exp3.json':475,
  'expressions/F07.exp3.json':907,'expressions/F08.exp3.json':421,
  'motions/haru_g_idle.motion3.json':21183,'motions/haru_g_m15.motion3.json':13540,
  'motions/haru_g_m26.motion3.json':23275,'motions/haru_g_m06.motion3.json':15118,
  'motions/haru_g_m20.motion3.json':23651,'motions/haru_g_m09.motion3.json':25066,
};
const TERMS=[
  {id:'proprietary',name:'Live2D Proprietary Software License Agreement',url:'https://www.live2d.com/eula/live2d-proprietary-software-license-agreement_en.html'},
  {id:'open-software',name:'Live2D Open Software License Agreement',url:'https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html'},
  {id:'free-material',name:'Live2D Free Material License Agreement',url:'https://www.live2d.com/eula/live2d-free-material-license-agreement_en.html'},
  {id:'sample-data',name:'Terms of Use for Live2D Cubism Sample Data',url:'https://www.live2d.com/en/learn/sample/model-terms/'},
];
const sha256=bytes=>createHash('sha256').update(bytes).digest('hex');

export function safeRelativePath(value){
  if(typeof value!=='string'||!value||value.length>180||!/^[-A-Za-z0-9_./]+$/.test(value)
    ||value.startsWith('/')||value.split('/').some(part=>!part||part==='.'||part==='..')
    ||path.posix.normalize(value)!==value)throw new Error('Rejected unsafe model asset path');
  return value;
}
function assertURL(value){
  const url=new URL(value);
  if(url.protocol!=='https:'||url.username||url.password||url.port||url.hash||!ALLOWED_HOSTS.has(url.hostname))
    throw new Error('Rejected non-allowlisted asset source');
  return url.href;
}
/** Exact expected references, not a general recursive remote-model downloader. */
export function collectModelPaths(model){
  const refs=model?.FileReferences;
  if(model?.Version!==3||!refs||!Array.isArray(refs.Textures)||!Array.isArray(refs.Expressions)||!refs.Motions)
    throw new Error('Unexpected pinned Haru model settings');
  const found=[refs.Moc,...refs.Textures,refs.Physics,refs.Pose,refs.DisplayInfo,refs.UserData];
  for(const expression of refs.Expressions)found.push(expression.File);
  for(const motions of Object.values(refs.Motions)){
    if(!Array.isArray(motions))throw new Error('Unexpected Haru motion group');
    for(const motion of motions)found.push(motion.File);
  }
  const paths=new Set(found.map(value=>{
    const safe=safeRelativePath(value);
    if(!Object.hasOwn(MODEL_SIZES,safe)||safe==='Haru.model3.json')throw new Error('Model reference is outside the pinned Haru allowlist');
    return safe;
  }));
  const expected=Object.keys(MODEL_SIZES).filter(value=>value!=='Haru.model3.json');
  if(paths.size!==expected.length||expected.some(value=>!paths.has(value)))throw new Error('Pinned Haru references differ from the reviewed 22-file set');
  return [...paths].sort();
}
function withoutSound(model){
  const result=structuredClone(model);
  for(const motions of Object.values(result.FileReferences.Motions))for(const motion of motions)delete motion.Sound;
  return result;
}
async function fetchBytes(url,{fetchImpl,maxBytes=4*1024*1024,expectedSize,label=url}){
  const approved=assertURL(url),controller=new AbortController();
  const timer=setTimeout(()=>controller.abort(),30000);
  try{
    const response=await fetchImpl(approved,{redirect:'manual',signal:controller.signal,
      headers:{'User-Agent':'Z-Live2D-Asset-Preparation/1.0'}});
    if(!response.ok||response.status<200||response.status>=300)throw new Error(`Asset request failed (${response.status}): ${label}`);
    if(response.url&&response.url!==approved)throw new Error(`Unexpected source redirect: ${label}`);
    const declared=Number(response.headers.get('content-length'));
    if(Number.isFinite(declared)&&declared>maxBytes)throw new Error(`Asset exceeds size limit: ${label}`);
    const parts=[];let total=0;
    if(response.body){
      const reader=response.body.getReader();
      try{
        for(;;){const next=await reader.read();if(next.done)break;
          total+=next.value.byteLength;if(total>maxBytes)throw new Error(`Asset exceeds size limit: ${label}`);parts.push(next.value);}
      }finally{try{await reader.cancel();}catch{}}
    }else{const bytes=new Uint8Array(await response.arrayBuffer());total=bytes.length;if(total>maxBytes)throw new Error(`Asset exceeds size limit: ${label}`);parts.push(bytes);}
    if(!total||(expectedSize!==undefined&&total!==expectedSize))throw new Error(`Unexpected pinned asset size: ${label}`);
    return Buffer.concat(parts.map(bytes=>Buffer.from(bytes)),total);
  }finally{clearTimeout(timer);}
}
async function write(stage,relative,bytes){
  const file=path.join(stage,...safeRelativePath(relative).split('/'));
  await fs.mkdir(path.dirname(file),{recursive:true});await fs.writeFile(file,bytes);
}
function fileRecord(relative,url,bytes,license,extra={}){
  return{path:relative,url,sha256:sha256(bytes),size:bytes.length,license,...extra};
}
async function batches(values,count,run){
  for(let index=0;index<values.length;index+=count){
    const results=await Promise.allSettled(values.slice(index,index+count).map(run));
    const failure=results.find(result=>result.status==='rejected');if(failure)throw failure.reason;
  }
}

/** Injectable fetch/output location permit offline contract tests without accepting or fetching real assets. */
export async function prepareLive2d({acceptLicense=false,fetchImpl=globalThis.fetch?.bind(globalThis),
  outputDir=path.join(PROJECT_ROOT,'ui/live2d'),log=console.log}={}){
  // This check must precede all network, temporary-directory and output writes.
  if(acceptLicense!==true)throw new Error('No assets fetched. Read the Live2D agreements, then explicitly pass --accept-live2d-license.');
  if(typeof fetchImpl!=='function')throw new Error('Node.js 18+ with fetch is required');
  const stage=await fs.mkdtemp(path.join(os.tmpdir(),'z-live2d-assets-'));
  const records=[];
  try{
    const vendor=[
      {id:'pixi',version:'6.5.10',path:'vendor/pixi.min.js',url:'https://pixijs.download/v6.5.10/pixi.min.js',
        source:`https://github.com/pixijs/pixijs/tree/${PIXI_COMMIT}`,sourceCommit:PIXI_COMMIT,copyright:'Copyright (c) 2013-2017 Mathew Groves, Chad Engler',expectedSize:460333},
      {id:'display',version:'0.4.0',path:'vendor/cubism4.min.js',url:'https://cdn.jsdelivr.net/npm/pixi-live2d-display@0.4.0/dist/cubism4.min.js',
        source:`https://github.com/guansss/pixi-live2d-display/tree/${DISPLAY_COMMIT}`,sourceCommit:DISPLAY_COMMIT,copyright:'Copyright (c) 2020 Guan'},
    ];
    const runtime={};
    for(const item of vendor){
      const bytes=await fetchBytes(item.url,{fetchImpl,maxBytes:2*1024*1024,expectedSize:item.expectedSize,label:item.path});
      const source=bytes.toString('utf8');
      if(source.trimStart().startsWith('<')||!source.includes(item.version)||(item.id==='display'&&!source.includes('Live2DModel')))
        throw new Error(`Unexpected JavaScript publication: ${item.path}`);
      await write(stage,item.path,bytes);
      const record=fileRecord(item.path,item.url,bytes,'MIT',{version:item.version,copyright:item.copyright,source:item.source,...(item.sourceCommit?{sourceCommit:item.sourceCommit}:{})});
      records.push(record);runtime[item.id]={...record};
    }

    const settingsURL=MODEL_BASE+'Haru.model3.json';
    const settingsBytes=await fetchBytes(settingsURL,{fetchImpl,maxBytes:16384,expectedSize:MODEL_SIZES['Haru.model3.json'],label:'Haru.model3.json'});
    const settings=JSON.parse(settingsBytes.toString('utf8')),paths=collectModelPaths(settings);
    const changed=Buffer.from(JSON.stringify(withoutSound(settings),null,2)+'\n');
    const settingsPath='models/Haru/Haru.model3.json';await write(stage,settingsPath,changed);
    records.push(fileRecord(settingsPath,settingsURL,changed,MODEL_LICENSE,{sourceCommit:HARU_COMMIT,
      upstreamSha256:sha256(settingsBytes),upstreamSize:settingsBytes.length,modification:'Removed Sound entries from motion definitions; original sample WAV files are neither downloaded nor played.'}));
    await batches(paths,4,async relative=>{
      const url=MODEL_BASE+relative,local='models/Haru/'+relative;
      const bytes=await fetchBytes(url,{fetchImpl,maxBytes:2*1024*1024,expectedSize:MODEL_SIZES[relative],label:relative});
      await write(stage,local,bytes);records.push(fileRecord(local,url,bytes,MODEL_LICENSE,{sourceCommit:HARU_COMMIT}));
    });

    const licenseSources=[
      {path:'licenses/pixi-LICENSE.txt',url:`https://raw.githubusercontent.com/pixijs/pixijs/${PIXI_COMMIT}/LICENSE`,license:'MIT'},
      {path:'licenses/pixi-live2d-display-LICENSE.txt',url:`https://raw.githubusercontent.com/guansss/pixi-live2d-display/${DISPLAY_COMMIT}/LICENSE`,license:'MIT'},
      {path:'licenses/Live2D-upstream-LICENSE.md',url:`https://raw.githubusercontent.com/Live2D/CubismWebSamples/${HARU_COMMIT}/LICENSE.md`,license:'Live2D license notice'},
      ...TERMS.map(term=>({path:`licenses/Live2D-${term.id}.source.html.txt`,url:term.url,license:term.name})),
    ];
    for(const item of licenseSources){
      const bytes=await fetchBytes(item.url,{fetchImpl,maxBytes:1024*1024,label:item.path});
      await write(stage,item.path,bytes);records.push(fileRecord(item.path,item.url,bytes,item.license));
    }
    const attribution=Buffer.from([
      'Live2D official Haru sample — temporary rendering demonstration, not original Z model assets.',
      'Copyright © Live2D Inc. All rights reserved.',
      'This content uses sample data owned and copyrighted by Live2D Inc. The sample data are utilized in accordance with terms and conditions set by Live2D Inc. This content itself is created at the author’s sole discretion.',
      '',
      'Model source: '+MODEL_BASE,
      'Upstream model settings were changed only to remove Sound references; no sample WAV files were fetched.',
      'PixiJS and pixi-live2d-display are MIT-licensed; this does NOT license the Haru model or Cubism Core under MIT.',
      'Core is loaded only from the official remote URL. Its JavaScript is not included in this directory or in the source package.',
      'The /05/ Core URL fixes the compatibility family, not immutable bytes. sources.json records the measured SHA-384 SRI and SHA-256.',
      'Review the terms for your actual use and business scale. The CLI flag acknowledges the agreements; it does not grant additional publishing or redistribution rights.',
      'Source/share packages should include the loader and this preparation script, not independently redistribute the Haru binaries.',
      '',...TERMS.map(term=>term.name+': '+term.url),'',
    ].join('\n'));
    await write(stage,'licenses/NOTICE.txt',attribution);
    records.push(fileRecord('licenses/NOTICE.txt','local:generated-attribution',attribution,'Attribution and source notice'));

    // Read the official Core exactly once, only to hash it; never write or execute it.
    const coreBytes=await fetchBytes(CORE_URL,{fetchImpl,maxBytes:5*1024*1024,label:'official Cubism Core /05/ (hash only)'});
    if(coreBytes.toString('utf8',0,Math.min(coreBytes.length,200000)).trimStart().startsWith('<')||!coreBytes.includes(Buffer.from('Live2DCubismCore')))
      throw new Error('Unexpected official Cubism Core response');
    runtime.core={url:CORE_URL,compatibilityFamily:'05 / Cubism 5.2',embedded:false,
      integrity:'sha384-'+createHash('sha384').update(coreBytes).digest('base64'),crossOrigin:'anonymous',
      sha256:sha256(coreBytes),size:coreBytes.length,license:'Live2D Proprietary Software License Agreement',
      copyright:'Copyright © Live2D Inc. All rights reserved.',
      source:'https://www.live2d.com/en/sdk/download/web/'};
    records.sort((a,b)=>a.path<b.path?-1:a.path>b.path?1:0);
    const manifest={schemaVersion:1,runtime,
      model:{name:'Haru',role:'Official Live2D sample stand-in; not the original Z character model',entry:settingsPath,
        sourceCommit:HARU_COMMIT,sourceTag:'4-r.5',sourceBase:MODEL_BASE,license:MODEL_LICENSE,copyright:'Copyright © Live2D Inc. All rights reserved.',
        fileCount:22,sampleAudioIncluded:false,files:records.filter(record=>record.path.startsWith('models/Haru/')).map(record=>record.path)},
      terms:TERMS,licenseAcknowledgement:{requiredCliFlag:'--accept-live2d-license',scope:'Acknowledgement only; actual use and publication remain subject to all linked agreements.'},files:records};
    await write(stage,'sources.json',Buffer.from(JSON.stringify(manifest,null,2)+'\n'));

    // Commit only reviewed paths, after every request and validation succeeds.
    await fs.mkdir(outputDir,{recursive:true});
    for(const folder of ['vendor','models/Haru','licenses']){
      const destination=path.join(outputDir,...folder.split('/'));
      await fs.mkdir(path.dirname(destination),{recursive:true});
      await fs.rm(destination,{recursive:true,force:true});
      await fs.rename(path.join(stage,...folder.split('/')),destination);
    }
    await fs.rename(path.join(stage,'sources.json'),path.join(outputDir,'sources.json'));
    log(`Prepared pinned Live2D assets: ${records.length} local files; Haru 22 files; Core remote only.`);
    return manifest;
  }finally{await fs.rm(stage,{recursive:true,force:true});}
}

async function main(){
  const args=process.argv.slice(2);
  if(args.includes('--help')||args.includes('-h')){
    console.log('Usage: node scripts/vendor-live2d.mjs --accept-live2d-license\n\nRead and accept the linked Live2D agreements before using this flag:\n'+TERMS.map(term=>term.url).join('\n')+'\n\nWrites pinned Pixi/display and official Haru assets under ui/live2d/. Core stays remote; only its hashes are saved.');return;
  }
  if(args.some(arg=>arg!=='--accept-live2d-license'))throw new Error('Unknown argument; use --help.');
  await prepareLive2d({acceptLicense:args.includes('--accept-live2d-license')});
}
if(process.argv[1]&&pathToFileURL(path.resolve(process.argv[1])).href===import.meta.url)
  main().catch(error=>{console.error(error.message);process.exitCode=1;});
