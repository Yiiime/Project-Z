import {HARU_ACTIONS} from './live2d-actions.js';

// Product capability boundary. Legacy modules remain for reference, not execution.
export const LIVE_PROFILE = Object.freeze({id:'singing-only', skills:Object.freeze(['singing','audience-reaction']), tools:Object.freeze(['start_singing','prepare_song','clarify_song','play_prepared_song','replay_talent','finish_activity','offer_call_story'])});
export const liveSkillEnabled = id => LIVE_PROFILE.skills.includes(id);
export const liveToolEnabled = name => LIVE_PROFILE.tools.includes(name);

export const Z_VOICE_PROFILE = Object.freeze({identity:'z-vivi',revision:1,speaker:'zh_female_vv_uranus_bigtts',speakingModel:'seed-tts-2.0',singingModel:'1.2.6.1',mode:'single_voice_a_cappella'});

// Only reviewed recordings belong here. See docs/TALENT-AUDIO.md before adding one.
export const TALENT_CATALOG = {
  version: 1,
  voiceIdentity: Z_VOICE_PROFILE.identity,
  voiceVersion: Z_VOICE_PROFILE.revision,
  songs: [{
    id: 'wanan-yanrenzhong-vivi2-chorus',
    title: '晚安',
    version: '颜人中歌曲版本 · Vivi 2.0 清唱 · 用户试听验收版',
    section: '第一段完整副歌',
    src: '/audio/songs/wanan-yanrenzhong-vivi2-chorus.wav',
    voiceIdentity: 'z-vivi',
    voiceVersion: 1,
    aCappella: true,
    approved: true,
    reviewRef: 'docs/reviews/wanan-vivi2.json',
    sha256: '24b598acd511e50c0dc434ee1405d1fd4abacb45bfe5bddaf1b67e1e065b03df',
  }],
};
export const TALENT_MOODS = ['neutral','happy','warm','serious','thinking','shy','sad','surprised'];
export const TALENT_GESTURES = Object.keys(HARU_ACTIONS).filter(id => HARU_ACTIONS[id].automatic);
export const EXPRESSION_GESTURES = TALENT_GESTURES.map(id => ({id,label:HARU_ACTIONS[id].label}));
export function playableSongs(catalog = TALENT_CATALOG) {
  return catalog.songs.filter(s => s.approved === true && s.aCappella === true && s.voiceIdentity === catalog.voiceIdentity && s.voiceVersion === catalog.voiceVersion && s.reviewRef && (/^(?:\/audio\/songs\/[a-zA-Z0-9_-]+\.(?:mp3|wav)|\/api\/singing\/assets\/[a-zA-Z0-9-]+)$/.test(s.src)));
}
export function validatePerformance(args, inbox = []) {
  const fail = () => { throw Error('invalid_performance'); };
  const text = (v, max) => typeof v === 'string' && !!v.trim() && v.length <= max && !/[\u0000-\u001f\u007f]/.test(v);
  if (!args || Object.keys(args).some(k => !['title','scriptType','sourceMessageId','lines'].includes(k)) || !text(args.title,60) || !['original','user_provided'].includes(args.scriptType) || !Array.isArray(args.lines) || !args.lines.length || args.lines.length > 6) fail();
  if (args.sourceMessageId !== undefined && !text(args.sourceMessageId,100)) fail();
  let size = 0;
  for (const line of args.lines) {
    if (!line || Object.keys(line).some(k => !['text','mood','gesture','pauseAfterMs'].includes(k)) || !text(line.text,160) || !TALENT_MOODS.includes(line.mood) || !TALENT_GESTURES.includes(line.gesture) || !Number.isInteger(line.pauseAfterMs) || line.pauseAfterMs < 0 || line.pauseAfterMs > 2000) fail();
    size += line.text.length;
  }
  if (size > 600) fail();
  if (args.scriptType === 'user_provided') {
    const source = inbox.find(m => m.mine && m.id === args.sourceMessageId);
    let cursor = 0;
    for (const line of args.lines) {
      const at = source?.text.indexOf(line.text, cursor) ?? -1;
      if (at < 0) fail();
      cursor = at + line.text.length;
    }
  }
  return args;
}
