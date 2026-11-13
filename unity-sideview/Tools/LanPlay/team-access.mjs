import {loadTeamClient,secureRequest} from '../team-model/client.mjs';
export {loadTeamClient};
export function memberVerifier(team){
  if(!team)return null;
  return async token=>{
    if(typeof token!=='string'||!/^[A-Za-z0-9_-]{43}$/.test(token))return null;
    const response=await secureRequest(team.origin+'/auth/status',{token,body:{},certificate:team.certificate,signal:AbortSignal.timeout(10000)});
    if([401,403].includes(response.status))return null;
    if(!response.ok)throw Object.assign(Error('team_authorization_unavailable'),{status:503});
    const value=await response.json();
    if(value.repository!==team.config.repository||!Number.isSafeInteger(value.user?.id)||!Number.isSafeInteger(value.expires)||value.expires<=Date.now())return null;
    return {token,user:value.user,expires:value.expires};
  };
}
