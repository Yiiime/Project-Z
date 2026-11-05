import {providerCall} from './netease.mjs';
// Safe status/QR receipts only; credentials stay in the worker's private store.
export const neteaseSession = Object.freeze({
 status:()=>providerCall('auth_status',{}),
 begin:()=>providerCall('auth_begin',{}),
 poll:()=>providerCall('auth_poll',{}),
 refresh:()=>providerCall('auth_refresh',{}),
 maintain:()=>providerCall('auth_maintain',{})
});
export function startNeteaseSessionMaintenance({intervalMs=60*60*1000,onStatus=()=>{},maintain=neteaseSession.maintain}={}){
 if(!Number.isFinite(intervalMs)||intervalMs<1000)throw Error('invalid_maintenance_interval');
 let stopped=false,timer,active=Promise.resolve(),last;
 const tick=()=>{
  active=Promise.resolve().then(maintain).catch(()=>({status:'check_failed',requiresLogin:false,error:'netease_login_check_failed'})).then(status=>{
   const signature=JSON.stringify([status.status,status.renewal,status.requiresLogin]);
   if(signature!==last){last=signature;try{onStatus(status);}catch{}}
  }).finally(()=>{if(!stopped){timer=setTimeout(tick,intervalMs);timer.unref?.();}});
 };
 tick();return {async stop(){stopped=true;clearTimeout(timer);await active;}};
}
