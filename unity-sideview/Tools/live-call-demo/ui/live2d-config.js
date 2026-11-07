import { HARU_ACTIONS } from './live2d-actions.js';
// Haru is an official Live2D sample, temporarily standing in for Z.
// Keep URLs and model-specific parameter/motion mappings in this one place.
export const LIVE2D_CONFIG = {
  label: 'Haru · Live2D 官方示例',
  modelUrl: '/live2d/models/Haru/Haru.model3.json',
  roomUrl: 'assets/live2d-empty-room.png',
  storageKey: 'z-character-mode-v1',
  runtime: {
    // The user has accepted the official Core/sample licence requirements.
    enabled: true,
    pixiUrl: '/live2d/vendor/pixi.min.js',
    coreUrl: 'https://cubism.live2d.com/sdk-web/core/05/live2dcubismcore.min.js',
    coreIntegrity: 'sha384-MeKqhuhBpq1ZqqshjOzqDOQJ/00BuDVdnNeYgPKul9hmgROzmT17WkmUeFJ9Jlrb',
    displayUrl: '/live2d/vendor/cubism4.min.js',
  },
  loadTimeoutMs: 20000,
  layout: { x: 0.49, top: -0.08, height: 2.3, maxWidth: 0.92 },
  // Semantic capabilities are model-specific. These presets use Haru's existing rig;
  // they do not add new artwork or claim that a sample clip performs card drawing.
  parameters: { mouthOpen: 'ParamMouthOpenY', mouthForm: 'ParamMouthForm', browLeftY: 'ParamBrowLY', browRightY: 'ParamBrowRY' },
  restPose: { ParamArmLA: 1, ParamArmRA: 1, ParamArmLB: 0, ParamArmRB: 0, ParamHandAngleR: -0.1, ParamHandAngleL: 0.1, ParamHandChangeR: 0, ParamHandDhangeL: 0 },
  lipSync: { noiseFloor: 0.006, gain: 13, attackMs: 40, releaseMs: 90 },
  performance: { cooldownMs: 8000, transitionMs: 180, returnMs: 240, armBlendMs: 140, emotionHoldMs: 3500, emotionReleaseMs: 6000, gazeChatMs: 1100, gazeCameraMs: 2600 },
  // Expressions are paired with a sustained attitude. Gestures are accents on
  // that attitude, not a replacement for it. Values are calibrated for Haru.
  moods: {
    neutral: { label: '自然', mouthForm: 0.12, brow: 0, eyeOpen: 1, pose: {} },
    happy: { label: '开心', mouthForm: 1, brow: 0.5, eyeSmile: 1, eyeOpen: 0.75,
      pose: { ParamAngleY: 8, ParamAngleZ: -9, ParamBodyAngleZ: -2, ParamEyeBallY: 0.1 } },
    warm: { label: '温柔', explainGesture: 'present', mouthForm: 0.65, brow: 0.15, browAngle: 0.4, eyeSmile: 0.55, eyeOpen: 0.78,
      pose: { ParamAngleY: -5, ParamAngleZ: 10, ParamBodyAngleY: 2 } },
    serious: { label: '认真', explainGesture: 'openArms', mouthForm: -0.35, brow: -0.65, browForm: 0.85, browAngle: -0.65, eyeOpen: 0.82,
      pose: { ParamAngleY: -2, ParamBodyAngleY: 1.5 } },
    thinking: { label: '思考', explainGesture: 'chin', mouthForm: -0.3, brow: 0, browAsymmetry: 0.55, browAngle: 0.25, eyeOpen: 0.85,
      pose: { ParamAngleX: -12, ParamAngleY: 7, ParamAngleZ: -16, ParamEyeBallX: -0.35, ParamEyeBallY: 0.25 } },
    shy: { label: '害羞', explainGesture: 'coverMouth', mouthForm: 0.65, brow: -0.25, browAngle: 0.65, blush: 1, eyeSmile: 0.3, eyeOpen: 0.68,
      pose: { ParamAngleX: -10, ParamAngleY: -13, ParamAngleZ: 12, ParamBodyAngleZ: 2, ParamEyeBallX: -0.4, ParamEyeBallY: -0.2 } },
    sad: { label: '低落', explainGesture: 'nod', mouthForm: -1, brow: -0.85, browForm: -1, browAngle: -0.8, eyeForm: -0.75, eyeOpen: 0.62,
      pose: { ParamAngleY: -16, ParamAngleZ: -7, ParamBodyAngleY: -2, ParamEyeBallY: -0.3 } },
    surprised: { label: '惊讶', mouthForm: -0.8, brow: 1, pupil: -0.85, eyeOpen: 1.85,
      pose: { ParamAngleY: 7, ParamAngleZ: 2, ParamBodyAngleY: -2 } },
  },
  gestures: HARU_ACTIONS,
};
