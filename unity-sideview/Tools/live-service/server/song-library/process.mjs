import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
export const execFileAsync = promisify(execFile);
export function processJson(file,args,{input,env,cwd,timeout=40000}={}) {
  return new Promise((resolve,reject) => {
    const child=execFile(file,args,{env,cwd,timeout,maxBuffer:8*1024*1024},(error,stdout) => {
      try { const result=JSON.parse(stdout); if (!result.ok) reject(Object.assign(Error(['netease_account_mismatch','netease_login_required','netease_login_expired','netease_session_invalid','netease_login_check_failed','audio_trial_only_authenticated','audio_access_unavailable_authenticated'].includes(result.error)?result.error:result.code===301 ? 'netease_login_required' : 'provider_request_failed'),{providerCode:result.code})); else resolve(result.body); }
      catch(e) { reject(error ? Error('provider_process_failed') : e); }
    });
    child.stdin.on('error',()=>{}); child.stdin.end(JSON.stringify(input));
  });
}
