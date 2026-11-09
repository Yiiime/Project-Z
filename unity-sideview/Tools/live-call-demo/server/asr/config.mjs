import fs from 'node:fs/promises';
import path from 'node:path';
export const ASR_KEYS=['VOLC_ASR_API_KEY','VOLC_ASR_RESOURCE_ID','VOLC_ASR_APP_ID','VOLC_ASR_ACCESS_KEY'];
export async function loadAsrConfig(root,environment=process.env){
  const values={};let files=[];
  for(const name of ['火山识别配置.env','.env.local'])try{files.push(await fs.readFile(path.join(root,name),'utf8'));}catch{}
  for(const name of ASR_KEYS){
    values[name]=environment[name]?.trim()||'';
    for(const source of files){if(values[name])break;const match=source.match(new RegExp('^[ \\t]*(?:export[ \\t]+)?'+name+'[ \\t]*=[ \\t]*([^\\r\\n]*)','m'));if(match){let v=match[1].trim();if(v[0]==='"'||v[0]==="'")v=v.match(/^(['"])(.*?)\1\s*(?:#.*)?$/)?.[2]||'';else v=v.replace(/\s+#.*$/,'');values[name]=v.trim();}}
  }
  const resource=values.VOLC_ASR_RESOURCE_ID||'volc.seedasr.sauc.duration';
  return {apiKey:values.VOLC_ASR_API_KEY,appId:values.VOLC_ASR_APP_ID,accessKey:values.VOLC_ASR_ACCESS_KEY,resource,
    enabled:!!(values.VOLC_ASR_API_KEY||values.VOLC_ASR_APP_ID&&values.VOLC_ASR_ACCESS_KEY)&&['volc.seedasr.sauc.duration','volc.seedasr.sauc.concurrent'].includes(resource)};
}
export function publicAsrConfig(c){return {provider:'doubao',model:'streaming-asr-2.0',enabled:c.enabled,configured:c.enabled,sampleRate:16000,channels:1,frameMs:200,
  message:c.enabled?'豆包流式识别 2.0':'请在火山识别配置.env 填入已开通 ASR 2.0 的凭证',missing:c.enabled?[]:['VOLC_ASR_API_KEY（或 APP_ID + ACCESS_KEY）']};}
