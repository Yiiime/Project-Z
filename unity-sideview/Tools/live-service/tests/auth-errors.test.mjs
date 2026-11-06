import {test} from 'node:test';
import assert from 'node:assert/strict';
import {processJson} from '../server/song-library/process.mjs';
function child(error){return processJson(process.execPath,['-e',`process.stdin.resume();process.stdin.on('end',()=>process.stdout.write(JSON.stringify({ok:false,error:${JSON.stringify(error)}})))`],{input:{}});}
test('worker transport retains typed login expiry',async()=>{await assert.rejects(child('netease_login_expired'),/netease_login_expired/);});
test('worker transport drops arbitrary provider error text',async()=>{await assert.rejects(child('untrusted_secret_text'),e=>e.message==='provider_request_failed');});
