import fs from 'node:fs/promises';
import path from 'node:path';
export async function singingEnvironment(root) {
  const result={};
  for(const [key,filename] of [['DEEPSEEK_API_KEY','DeepSeek配置.env'],['VOLC_TTS_API_KEY','火山语音配置.env']]){
    result[key]=process.env[key]?.trim()||'';
    for(const file of [filename,'.env.local']){
      if(result[key])break;
      try{const source=await fs.readFile(path.join(root,file),'utf8'),raw=source.match(new RegExp('^[ \\t]*(?:export[ \\t]+)?'+key+'[ \\t]*=[ \\t]*([^\\r\\n]*)','m'))?.[1]?.trim()||'';
        result[key]=/^['"]/.test(raw)?raw.match(/^(['"])(.*?)\1\s*(?:#.*)?$/)?.[2]?.trim()||'':raw.replace(/(?:^|[ \t]+)#.*$/,'').trim();}catch{}
    }
  }
  return result;
}
