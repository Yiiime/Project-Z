// Stable machine-readable causes for host UI, agent speech and retry routing.
export function retrievalFailure(error){
 if(error==='netease_account_mismatch')return {status:'needs_login',outcome:'login_required',error,requiresLogin:true,nextAction:'login_bound_account',authStatus:'account_mismatch',label:'请登录已绑定的网易云账号',reason:'后台固定使用的网易云账号与本次登录账号不一致，请使用已绑定账号重新登录；不会自动切换到其他账号。'};
 if(['netease_login_required','netease_login_expired','netease_session_invalid'].includes(error)){
  const authStatus=error==='netease_login_expired'?'expired':error==='netease_session_invalid'?'invalid':'anonymous';
  return {status:'needs_login',outcome:'login_required',error,requiresLogin:true,nextAction:'login_then_retry',authStatus,label:authStatus==='anonymous'?'需要登录网易云':'需要重新登录网易云',reason:authStatus==='expired'?'网易云后台登录态已过期，需要重新扫码登录后重试；不是没有这首歌的音源。':authStatus==='invalid'?'本机网易云登录态无法读取，需要重新登录后重试；不能据此判断歌曲没有音源。':'当前未登录网易云，完整音频获取受限，请先扫码登录再试；登录后仍需确认该账号能访问完整音频。'};
 }
 if(error==='netease_login_check_failed')return {status:'failed',outcome:'login_check_failed',error,requiresLogin:false,nextAction:'retry',authStatus:'unknown',label:'暂时无法确认网易云登录状态',reason:'登录状态查询暂时失败，尚不能判断登录是否过期，也不能判断歌曲是否无音源；稍后重试。'};
 if(['audio_trial_only_authenticated','audio_access_unavailable_authenticated'].includes(error))return {status:'unavailable',outcome:'audio_permission_unavailable',error,requiresLogin:false,nextAction:'check_account_access',authStatus:'authenticated',label:'已登录 · 完整音频仍不可用',reason:error==='audio_trial_only_authenticated'?'网易云登录有效，但当前账号获取这首歌仍只有试听，暂时无法获取完整音频；不是登录过期。':'网易云登录有效，但当前账号仍拿不到这首歌的完整音频；原因可能是账号或资源访问限制，不能保证重新登录就能解决。'};
 return null;
}
