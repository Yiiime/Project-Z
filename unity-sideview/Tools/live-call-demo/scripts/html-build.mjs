import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const ui = path.join(root, 'ui');
const mime = {'.png':'image/png','.svg':'image/svg+xml',};
const moduleNames = {
  './game-return.js':'GameReturnModule',
  './live-engine.js':'EngineModule',
  './play-engine.js':'PlayEngineModule',
  './agent-engine.js':'AgentEngineModule',
  './session-continuity.js':'SessionContinuityModule',
  './conversation-engine.js':'ConversationModule',
  './call-contract.js':'CallContractModule',
  './call-engine.js':'CallEngineModule',
  './call-ui.js':'CallUIModule',
  './call-voice-policy.js':'CallVoicePolicyModule',
  './call-noise-monitor.js':'CallNoiseModule',
  './doubao-recognition.js':'DoubaoRecognitionModule',
  './voiceprint-recognition.js':'VoiceprintRecognitionModule',
  './agent-client.js':'AgentClientModule',
  './audience-director.js':'AudienceModule',
  './dialogue-review.js':'DialogueReviewModule',
  './play-ui.js':'PlayUIModule',
  './text-director.js':'TextModule',
  './speech-director.js':'SpeechModule',
  './talent-director.js':'TalentModule',
  './talent-config.js':'TalentConfigModule',
  './drawing-scene.js':'DrawingSceneModule',
  './drawing-director.js':'DrawingModule',
  './chat-client.js':'ChatModule',
  './card-art.js':'CardArtModule',
  './manga-face.js':'MangaFaceModule',
  './live2d-actions.js':'Live2DActionsModule',
  './live2d-config.js':'Live2DConfigModule',
  './live2d-stage.js':'Live2DStageModule',
};

function rewriteImports(source) {
  return source.replace(/^import\s*\{([\s\S]*?)\}\s*from\s*['"]([^'"]+)['"];?\s*$/gm, (_, bindings, specifier) => {
    const name = moduleNames[specifier.split('?')[0]];
    if (!name) throw new Error('Unsupported browser import: ' + specifier);
    return `const {${bindings.replace(/\s+as\s+/g, ':')}}=${name};`;
  }).replace(/^export\s+(?=(?:class|const|function|let)\b)/gm, '')
    .replace(/^export\s*\{[^}]+\};?\s*$/gm, '');
}

export async function buildHtml({assetNotes} = {}) {
  let html = await fs.readFile(path.join(ui, 'index.html'), 'utf8');
  let css = await fs.readFile(path.join(ui, 'styles.css'), 'utf8');
  css += '\n' + await fs.readFile(path.join(ui, 'manga-motion.css'), 'utf8');
  css += '\n' + await fs.readFile(path.join(ui, 'cards.css'), 'utf8');
  css += '\n' + await fs.readFile(path.join(ui, 'live2d.css'), 'utf8');
  css += '\n' + await fs.readFile(path.join(ui, 'call.css'), 'utf8');
  const rasterTargets = {'manga-live-night.png': '.room-art,.head-piece,.left-arm,.right-arm','live2d-empty-room.png':'#live2d-room,#live2d-foreground'};
  const assets = {};
  for (const filename of await fs.readdir(path.join(ui, 'assets'))) {
    const type = mime[path.extname(filename)];
    if (filename.endsWith('.png') && !rasterTargets[filename]) continue;
    if (type) assets[filename] = `data:${type};base64,${(await fs.readFile(path.join(ui,'assets',filename))).toString('base64')}`;
  }
  // Multi-megabyte CSS custom-property values are discarded by Chromium.
  // Keep each raster once in JavaScript and assign the normal CSSOM property
  // after the page elements exist, for both Worker and offline builds.
  const rasterImages = {};
  for (const [filename, url] of Object.entries(assets)) {
    if (!filename.endsWith('.png')) continue;
    if (!rasterTargets[filename]) throw new Error('Missing raster target selectors: ' + filename);
    rasterImages[filename] = url;
    for (const reference of [`url('assets/${filename}')`, `url("assets/${filename}")`, `url(assets/${filename})`]) {
      css = css.replaceAll(reference, 'none');
    }
  }
  html = html.replace(/<link rel="preload"[^>]*>/g, '')
    .replace(/<link rel="stylesheet"[^>]*>/, () => `<style>${css}</style>`);
  const avatars = Object.fromEntries(Object.entries(assets).filter(([name])=>name.endsWith('.svg')));
  const notes = assetNotes ? JSON.stringify(assetNotes) : await fs.readFile(path.join(ui,'assets','asset-notes.json'),'utf8');
  const modules = [];
  for (const [filename,name,exports] of [
    ['game-return.js','GameReturnModule','gameReturnUrl,mountGameReturn'],
    ['live2d-actions.js','Live2DActionsModule','HARU_ACTIONS,HARU_ACTION_LIBRARY'],
    ['talent-config.js','TalentConfigModule','LIVE_PROFILE,liveSkillEnabled,liveToolEnabled,TALENT_CATALOG,TALENT_MOODS,TALENT_GESTURES,EXPRESSION_GESTURES,playableSongs,validatePerformance'],
    ['drawing-scene.js','DrawingSceneModule','DRAWING_LIMITS,drawingText,validateDrawingScene,applyDrawingPatch,renderDrawingScene'],
    ['drawing-director.js','DrawingModule','DrawingDirector'],
    ['live-engine.js','EngineModule','LiveEngine,CARDS'],
    ['play-engine.js','PlayEngineModule','PlayEngine,drawingSvg'],
    ['dialogue-review.js','DialogueReviewModule','DialogueReview'],
    ['audience-director.js','AudienceModule','AudienceDirector'],
    ['agent-client.js','AgentClientModule','AgentClient'],
    ['agent-engine.js','AgentEngineModule','AgentEngine'],
    ['session-continuity.js','SessionContinuityModule','SessionContinuity,validateAgendaUpdates,validateTopicProgress'],
    ['conversation-engine.js','ConversationModule','ConversationEngine'],
    ['call-contract.js','CallContractModule','CALL_STORY'],
    ['call-engine.js','CallEngineModule','CallConversationEngine'],
    ['call-voice-policy.js','CallVoicePolicyModule','CALL_VOICE_DEFAULTS,readRecognitionResults,filterCallRecognitionResults,turnDisposition,CallTranscriptWindow'],
    ['call-noise-monitor.js','CallNoiseModule','BrowserNoiseMonitor'],
    ['doubao-recognition.js','DoubaoRecognitionModule','DoubaoRecognition'],
    ['voiceprint-recognition.js','VoiceprintRecognitionModule','CallRecognition,VoiceprintRecognition,enrollmentWav'],
    ['text-director.js','TextModule','TextDirector'],
    ['speech-director.js','SpeechModule','SpeechDirector,CALL_THINKING_TEXT'],
    ['call-ui.js','CallUIModule','mountCallUI'],
    ['talent-director.js','TalentModule','TalentDirector'],
    ['chat-client.js','ChatModule','ChatClient'],
    ['card-art.js','CardArtModule','cardBackArt,tarotArt,rewardArt'],
    ['play-ui.js','PlayUIModule','mountPlayUI'],
    ['manga-face.js','MangaFaceModule','setupMangaFace'],
    ['live2d-config.js','Live2DConfigModule','LIVE2D_CONFIG'],
    ['live2d-stage.js','Live2DStageModule','Live2DStage'],
  ]) {
    const source = rewriteImports(await fs.readFile(path.join(ui,filename),'utf8'));
    modules.push(`const ${name}=(()=>{\n${source}\nreturn {${exports}};})();`);
  }
  let app = rewriteImports(await fs.readFile(path.join(ui,'app.js'),'utf8'));
  app = app.replace('src="assets/${person.asset}"', 'src="${BUNDLED_AVATARS[person.asset]}"')
    .replace("const notes=await (await fetch('assets/asset-notes.json')).json();", 'const notes=BUNDLED_NOTES;');
  for (const [filename,url] of Object.entries(assets)) {
    if (filename.endsWith('.png')) continue;
    html = html.replaceAll('assets/' + filename, url);
    app = app.replaceAll('assets/' + filename, url);
  }
  const imageSetup = `const BUNDLED_IMAGES=${JSON.stringify(rasterImages)};\nfor(const [filename,selector] of Object.entries(${JSON.stringify(rasterTargets)})){for(const element of document.querySelectorAll(selector)){element.style.backgroundImage='url("'+BUNDLED_IMAGES[filename]+'")';}}`;
  const script = (`const BUNDLED_AVATARS=${JSON.stringify(avatars)};\n${imageSetup}\nconst BUNDLED_NOTES=${notes};\n${modules.join('\n')}\n${app}`).replace(/<\/script/gi, '<\\/script');
  const scripts = html.match(/<script type="module" src="app\.js[^"\n]*"><\/script>/g) || [];
  if (scripts.length !== 1) throw new Error('Expected one application entry script');
  html = html.replace(scripts[0], () => `<script>\n${script}\n</script>`);
  return {html,script};
}

// Standalone, explicit preview route. No AI requests or secrets are included.
export async function buildPerformanceHtml() {
  let html = await fs.readFile(path.join(ui, 'live2d-preview.html'), 'utf8');
  const actions = (await fs.readFile(path.join(ui, 'live2d-actions.js'), 'utf8')).replace(/^export /gm, '');
  const config = (await fs.readFile(path.join(ui, 'live2d-config.js'), 'utf8')).replace(/^import .*;\r?\n/gm, '').replace(/^export /gm, '');
  const stage = (await fs.readFile(path.join(ui, 'live2d-stage.js'), 'utf8')).replace(/^import .*;\r?\n/gm, '').replace(/^export /gm, '');
  html = html.replace("import {Live2DStage} from './live2d-stage.js';import {LIVE2D_CONFIG} from './live2d-config.js';", () => actions + '\n' + config + '\n' + stage);
  html = html.replace("import {HARU_ACTION_LIBRARY} from './live2d-actions.js';", '');
  const intent = (await fs.readFile(path.join(ui, 'performance-intent.js'), 'utf8')).replace(/^export /gm, '');
  html = html.replace("import {resolvePreviewIntent,generatePreviewLine} from './performance-intent.js';", () => intent);
  return html;
}
