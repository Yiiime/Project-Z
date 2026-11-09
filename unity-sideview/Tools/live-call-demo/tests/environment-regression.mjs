import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {singingEnvironment} from '../server/singing/environment.mjs';
const root=await fs.mkdtemp(path.join(os.tmpdir(),'z-config-test-'));
const old={DEEPSEEK_API_KEY:process.env.DEEPSEEK_API_KEY,VOLC_TTS_API_KEY:process.env.VOLC_TTS_API_KEY};
try {
 delete process.env.DEEPSEEK_API_KEY;delete process.env.VOLC_TTS_API_KEY;
 await fs.copyFile(new URL('../.env.example',import.meta.url),path.join(root,'.env.local'));
 assert.deepEqual(await singingEnvironment(root),{DEEPSEEK_API_KEY:'',VOLC_TTS_API_KEY:''});
 await fs.writeFile(path.join(root,'.env.local'),`DEEPSEEK_API_KEY= # comment\r\nVOLC_TTS_API_KEY=\r\n# next line\r\nOTHER=unrelated`);
 assert.deepEqual(await singingEnvironment(root),{DEEPSEEK_API_KEY:'',VOLC_TTS_API_KEY:''});
 await fs.writeFile(path.join(root,'.env.local'),`export DEEPSEEK_API_KEY='fixture-chat' # comment\nVOLC_TTS_API_KEY=fixture-voice # comment`);
 assert.deepEqual(await singingEnvironment(root),{DEEPSEEK_API_KEY:'fixture-chat',VOLC_TTS_API_KEY:'fixture-voice'});
 process.env.VOLC_TTS_API_KEY='process-fixture';assert.equal((await singingEnvironment(root)).VOLC_TTS_API_KEY,'process-fixture');
 console.log('PASS blank configuration templates, CRLF, comments, quotes and process precedence');
} finally {
 for(const [key,value] of Object.entries(old)){if(value===undefined)delete process.env[key];else process.env[key]=value;}
 await fs.rm(root,{recursive:true,force:true});
}
