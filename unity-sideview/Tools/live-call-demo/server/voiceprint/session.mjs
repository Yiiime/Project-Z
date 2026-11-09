import { randomUUID } from 'node:crypto';

// MMPG session.create has different nesting from StartVoiceChat.
// Only the listening path is configured here. Agent, TTS and Live2D remain local.
export function buildVoiceprintSession(config, { roomId, userId, agentId, eventId = randomUUID() }) {
  if (!config.validMode || config.missing.length) throw new Error('voiceprint_not_configured');
  for (const id of [roomId, userId, agentId]) {
    if (typeof id !== 'string' || !/^[a-zA-Z0-9_@.\-]{1,128}$/.test(id)) throw new Error('invalid_rtc_identity');
  }
  if (userId === agentId) throw new Error('invalid_rtc_identity');
  return {
    event_id: eventId, type: 'session.create',
    session: {
      AgentConfig: {
        TransmitConfig: { Type: 'rtc', AppId: config.rtcAppId, RoomId: roomId, UserId: agentId, TargetUserId: [userId] },
        IdleTimeout: 30, TurnDetectionMode: 0,
        SubtitleConfig: { DisableRTSSubtitle: true, UserTranscriptMode: 1 },
      },
      AudioConfig: {
        AudioFormat: { UserAudioFormat: 'pcm16', AssistantAudioFormat: 'pcm24', AnsMode: 0 },
        UserAudioCallbackMode: 0,
        // Do not let raw VAD cancel playback before identity verification.
        VADConfig: { SilenceTime: 600, ForceBeginThreshold: 0, ForceEnd: false, ExpireTime: 1800, EnablePrefetch: true, AIVAD: true },
        VoicePrint: { Mode: 1, EnableSV: true, IdList: [config.voiceprintId], ProcessMode: 2, SVMode: 2 },
        VolumeGain: 1,
        FarfieldConfig: { Enable: false, Level: 'Low', Threshold: 0, FixedSource: false },
      },
      VisionConfig: { Enable: false },
      ASRConfig: {
        Provider: 'volcano',
        ProviderParams: {
          Mode: 'bigmodel', StreamMode: 2, ContextHistoryLength: 0,
          // ApiKey spelling confirmed against the live MMPG ASR service.
          Credential: { ...(config.asrSource === 'legacy' ? { AppId: config.asrAppId, AccessToken: config.asrAccessToken } : { ApiKey: config.asrApiKey }), ApiResourceId: 'volc.seedasr.sauc.duration' },
          VolcanoASRParameters: JSON.stringify({ request: { enable_nonstream: true, enable_itn: true, enable_punc: true } }),
        },
      },
    },
  };
}
