/** Lifecycle and actual Cubism-parameter integration, with offline SDK mocks. */
import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { Live2DStage, loadLive2DRuntime } from '../ui/live2d-stage.js';
import { resolvePreviewIntent, generatePreviewLine } from '../ui/performance-intent.js';
import { HARU_ACTION_LIBRARY } from '../ui/live2d-actions.js';
import { LIVE2D_CONFIG } from '../ui/live2d-config.js';

if (!globalThis.CustomEvent) globalThis.CustomEvent = class extends Event { constructor(type, { detail } = {}) { super(type); this.detail = detail; } };
const next = () => new Promise(resolve => setTimeout(resolve, 0));
const deferred = () => { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no; }); return { promise, resolve, reject }; };
let checks = 0;
async function check(name, fn) { await fn(); checks++; console.log('PASS ' + name); }

class Element extends EventTarget {
  constructor() {
    super(); this.dataset = {}; this.style = {}; this.hidden = false; this.children = []; this.attributes = {};
    this.bounds = { width: 1000, height: 625, left: 20, top: 30 };
    const values = new Set();
    this.classList = { toggle(name, on) { if (on) values.add(name); else values.delete(name); }, add(name) { values.add(name); }, contains(name) { return values.has(name); } };
  }
  getBoundingClientRect() { return this.bounds; }
  setAttribute(key, value) { this.attributes[key] = value; }
  replaceChildren(...children) { this.children = children; }
  remove() { this.removed = true; }
}
function mockModel() {
  const internal = new EventEmitter(), values = new Map();
  const model = {
    internalModel: internal, updates: 0, motions: [], expressions: [], destroyed: 0, mouthFrames: [],
    anchor: { set: (...args) => { model.anchorValue = args; } },
    scale: { set: value => { model.scaleValue = value; } },
    position: { set: (...args) => { model.positionValue = args; } },
    update(dt) { this.updates++; this.elapsed = (this.elapsed || 0) + dt; },
    expression(name) { this.expressions.push(name); return Promise.resolve(true); },
    motion(...args) { this.motions.push(args); return Promise.resolve(true); },
    destroy() { this.destroyed++; this._destroyed = true; internal.destroyed = true; },
    values,
  };
  internal.width = 2000; internal.height = 3500; internal.breath = { vendorBreath: true };
  internal.focusController = { focus: (...args) => { model.focusValue = args; } };
  internal.motionManager = {
    groups: { idle: 'Idle' }, definitions: { TapBody: [{}, {}, {}] },
    stopAllMotions() { model.motionStops = (model.motionStops || 0) + 1; },
    expressionManager: { resetExpression() { model.expressionResets = (model.expressionResets || 0) + 1; } },
  };
  internal.coreModel = {
    setParameterValueById(id, value) { values.set(id, value); },
    addParameterValueById(id, value) { values.set(id, (values.get(id) || 0) + value); },
    update() { model.mouthFrames.push(values.get('ParamMouthOpenY')); },
  };
  return model;
}
function mockApp() {
  const app = {
    view: new Element(), running: false, destroyed: false, lost: 0, renders: 0,
    stage: { children: [], addChild(model) { this.children.push(model); }, removeChild(model) { this.children = this.children.filter(value => value !== model); } },
    renderer: { gl: { getExtension: () => ({ loseContext() { app.lost++; } }) }, resize(width, height) { app.size = [width, height]; } },
    ticker: { callbacks: new Set(), deltaMS: 16.67, add(fn) { this.callbacks.add(fn); }, remove(fn) { this.callbacks.delete(fn); } },
    start() { this.running = true; }, stop() { this.running = false; },
    render() {
      this.renders++;
      for (const model of this.stage.children) {
        // A motion may have authored an open mouth. Our lip-sync hook must run
        // after that motion and before the SDK finalizes/render its geometry.
        model.values.set('ParamMouthOpenY', 0.95);
        model.values.set('ParamBrowLY', 0); model.values.set('ParamBrowRY', 0);
        model.internalModel.emit('beforeModelUpdate'); model.internalModel.coreModel.update();
      }
    },
    destroy(removeView) { this.destroyed = true; if (removeView) this.view.remove(); },
  };
  return app;
}
function setup(patch = {}) {
  const document = new EventTarget(); document.body = new Element(); document.hidden = false;
  const window = new EventTarget(); window.devicePixelRatio = 3;
  const media = new EventTarget(); media.matches = false; window.matchMedia = () => media;
  const stored = new Map(); const storage = { getItem: key => stored.get(key), setItem: (key, value) => stored.set(key, value) };
  const elements = { stage: new Element(), host: new Element(), room: new Element(), foreground: new Element(), notice: new Element(), noticeText: new Element(), retryButton: new Element() };
  const apps = [], models = []; let now = 0;
  const config = structuredClone(LIVE2D_CONFIG); config.runtime.enabled = true;
  let observer;
  class Observer { constructor(callback) { this.callback = callback; observer = this; } observe() {} disconnect() { this.disconnected = true; } }
  const PIXI = { utils: { isWebGLSupported: () => true } };
  const controller = new Live2DStage({ ...elements, document, window, storage, config,
    loadRuntime: async () => PIXI, loadRoom: async () => {}, ResizeObserver: Observer,
    createApplication: (_, options) => { const app = mockApp(); app.options = options; apps.push(app); return app; },
    loadModel: async () => { const model = mockModel(); models.push(model); return model; },
    now: () => now, ...patch,
  });
  return { controller, document, window, media, storage, stored, config, PIXI, elements, apps, models,
    observer: () => observer,
    step(count = 1, dt = 16.67) { for (let i = 0; i < count; i++) { now += dt; for (const app of apps) { app.ticker.deltaMS = dt; if (app.running) for (const fn of app.ticker.callbacks) fn(); } } },
  };
}

await check('licence gate and saved original-art choice make zero runtime/model calls', async () => {
  let calls = 0;
  const config = structuredClone(LIVE2D_CONFIG); config.runtime.enabled = false;
  const fixture = setup({ config, loadRuntime: async () => { calls++; throw Error(); } });
  assert.equal(await fixture.controller.setMode('live2d'), false); assert.equal(calls, 0); assert.equal(fixture.controller.getState().status, 'blocked');
  assert.equal(fixture.elements.stage.dataset.character, 'manga');
  await fixture.controller.setMode('manga'); assert.equal(calls, 0); assert.equal(fixture.elements.notice.hidden, true);
  fixture.controller.destroy();
  const saved = setup({ storage: { getItem: () => 'manga', setItem() {} }, loadRuntime: async () => { calls++; } });
  assert.equal(saved.controller.preferred, 'manga'); await saved.controller.setMode(saved.controller.preferred); assert.equal(calls, 0); saved.controller.destroy();
});

await check('file preview immediately uses original art with a local-service explanation and makes no remote calls', async () => {
  let calls = 0; const fixture = setup({ loadRuntime: async () => { calls++; } });
  fixture.window.location = { protocol: 'file:' };
  assert.equal(await fixture.controller.setMode('live2d'), false); assert.equal(fixture.controller.status, 'offline'); assert.equal(calls, 0);
  assert(fixture.controller.message.includes('启动本地服务')); assert.equal(fixture.elements.notice.hidden, false); assert.equal(fixture.elements.retryButton.hidden, true); fixture.controller.destroy();
});

await check('native platform timers keep their global owner instead of receiving the Live2D controller', async () => {
  const originalSet = globalThis.setTimeout, originalClear = globalThis.clearTimeout;
  let sets = 0, clears = 0;
  try {
    globalThis.setTimeout = function(callback, delay) { assert.equal(this, globalThis); sets++; return originalSet(callback, delay); };
    globalThis.clearTimeout = function(timer) { assert.equal(this, globalThis); clears++; return originalClear(timer); };
    const fixture = setup(); assert.equal(await fixture.controller.setMode('live2d'), true); fixture.controller.destroy();
    assert.equal(sets, 1); assert.equal(clears, 1);
  } finally { globalThis.setTimeout = originalSet; globalThis.clearTimeout = originalClear; }
});

await check('runtime scripts load in order, verify globals, disable sample audio and abort before the next dependency', async () => {
  const config = structuredClone(LIVE2D_CONFIG), globals = {}, urls = [], document = { createElement: () => new Element() };
  document.head = { append(script) {
    urls.push(script.src);
    if (script.src === config.runtime.coreUrl) { assert.equal(script.integrity, config.runtime.coreIntegrity); assert.equal(script.crossOrigin, 'anonymous'); }
    queueMicrotask(() => {
      if (script.src === config.runtime.pixiUrl) globals.PIXI = { Application() {} };
      if (script.src === config.runtime.coreUrl) globals.Live2DCubismCore = {};
      if (script.src === config.runtime.displayUrl) globals.PIXI.live2d = { Live2DModel() {}, config: { sound: true } };
      script.onload?.();
    });
  } };
  await loadLive2DRuntime(config, { document, globalObject: globals, signal: new AbortController().signal });
  assert.deepEqual(urls, [config.runtime.pixiUrl, config.runtime.coreUrl, config.runtime.displayUrl]); assert.equal(globals.PIXI.live2d.config.sound, false);
  const cancel = new AbortController(); let pending;
  const never = { createElement: () => new Element(), head: { append(script) { pending = script; } } };
  const task = loadLive2DRuntime(config, { document: never, globalObject: {}, signal: cancel.signal }); cancel.abort();
  await assert.rejects(task, { name: 'AbortError' }); assert.equal(pending.removed, true);
});

await check('successful model swaps after first render, uses the bundled empty room and preserves portrait framing on resize', async () => {
  const fixture = setup(), { controller, elements, apps, models } = fixture; let roomUrl;
  elements.room.style.backgroundImage = 'url("data:image/png;base64,fixture")'; controller.roomLoader = async value => { roomUrl = value; };
  assert.equal(await controller.setMode('live2d'), true);
  assert.equal(controller.getState().status, 'ready'); assert.equal(elements.stage.dataset.character, 'live2d');
  assert.equal(fixture.document.body.classList.contains('live2d-active'), true); assert.equal(elements.host.children[0], apps[0].view);
  assert.equal(roomUrl, 'data:image/png;base64,fixture'); assert.equal(elements.foreground.style.backgroundImage, elements.room.style.backgroundImage);
  assert.equal(apps[0].options.autoStart, false); assert.equal(apps[0].options.sharedTicker, false); assert.equal(apps[0].options.resolution, 2);
  assert.equal(models[0].autoUpdate, false); assert.equal(models[0].autoInteract, false); assert.deepEqual(models[0].anchorValue, [0.5, 0]);
  // Portrait framing may deliberately place transparent model padding above
  // the canvas; actual head visibility is verified with the rendered model.
  const oldPosition=[...models[0].positionValue],oldBounds={...elements.stage.bounds};
  assert(oldPosition.every(Number.isFinite)); assert(models[0].scaleValue > 0);
  elements.stage.bounds = { ...elements.stage.bounds, width: 390, height: 245 }; controller.resize();
  assert.deepEqual(apps[0].size, [390, 245]); assert(Number.isFinite(models[0].scaleValue));
  assert(Math.abs(models[0].positionValue[0]/390-oldPosition[0]/oldBounds.width)<1e-8);
  assert(Math.abs(models[0].positionValue[1]/245-oldPosition[1]/oldBounds.height)<1e-8); controller.destroy();
});

await check('real audio level drives the formal mouth parameter after motions; stale speech callbacks cannot reopen it', async () => {
  const fixture = setup(); await fixture.controller.setMode('live2d');
  const model = fixture.models[0], controller = fixture.controller;
  controller.setSpeech({ id: 1, mood: 'happy', gesture: 'sampleM26' }); controller.setPerformance({id:1,phase:'speaking'}); controller.setActivity({ id: 1, speaking: true, level: 0.06 }); fixture.step(5);
  assert(model.values.get('ParamMouthOpenY') > 0.3); assert(model.values.get('ParamMouthOpenY') < 0.9, 'Motion mouth value is replaced by audio amplitude');
  assert(model.values.get('ParamMouthForm') > 0); assert.deepEqual(model.motions.at(-1), ['TapBody', 0, 2]);
  controller.setSpeech({ id: 2, mood: 'serious' }); controller.setActivity({ id: 1, speaking: true, level: 1 }); fixture.step(200);
  assert.equal(model.values.get('ParamMouthOpenY'), 0); assert(model.values.get('ParamMouthForm') < .001); assert(model.expressionResets > 0);
  controller.setActivity({ id: 2, speaking: true, level: 0.07 }); fixture.step(3); assert(model.values.get('ParamMouthOpenY') > 0);
  fixture.step(45); assert(model.values.get('ParamMouthOpenY') < 0.01, 'Stale audio meters decay to silence'); controller.destroy();
});

await check('faces blend smoothly, preserve blinks and never force an open speaking mouth', async () => {
  const f=setup(); await f.controller.setMode('live2d');const c=f.controller,m=f.models[0];
  for(const mood of Object.keys(LIVE2D_CONFIG.moods)) {
    c.setSpeech({id:mood,mood});f.step(150);
    assert.equal(m.values.get('ParamMouthOpenY'),0);
    assert(m.values.get('ParamEyeLOpen')>=0&&m.values.get('ParamEyeLOpen')<=2);
    assert([...m.values.values()].every(Number.isFinite));
  }
  c.setSpeech({id:'a',mood:'happy'});f.step(120);const smile=m.values.get('ParamMouthForm');
  c.setSpeech({id:'b',mood:'sad'});f.step();assert(m.values.get('ParamMouthForm')>0,'No instant mood snap');
  f.step(150);assert(m.values.get('ParamMouthForm')<0&&smile>0);c.destroy();
});

await check('no gesture during synthesis; once at playback start; no replay on resume or stale callback', async () => {
  const f=setup();await f.controller.setMode('live2d');const c=f.controller,m=f.models[0];
  c.setSpeech({id:1,mood:'warm',gesture:'sampleM26'});f.step(120);assert.equal(m.motions.length,0);
  c.setPerformance({id:1,phase:'speaking'});assert.equal(m.motions.length,1);
  c.setPerformance({id:1,phase:'speaking',resumed:true});assert.equal(m.motions.length,1);
  c.setSpeech({id:2,mood:'warm',gesture:'sampleM26'});c.setPerformance({id:1,phase:'speaking'});assert.equal(m.motions.length,1);
  c.setPerformance({id:2,phase:'speaking'});assert.equal(m.motions.length,1,'Same-action cooldown');
  c.setPerformance({id:2,phase:'listening'});assert.equal(c.activeGesture,null);c.destroy();
});

await check('late async clip start cannot replace a newer clip or revive after cancellation', async () => {
 const f=setup();await f.controller.setMode('live2d');const c=f.controller,m=f.models[0],old=deferred();let calls=0;
 m.motion=(...args)=>{calls++;m.motions.push(args);return calls===1?old.promise:Promise.resolve();};
 c.setSpeech({id:1,gesture:'sampleM26'});c.setPerformance({id:1,phase:'speaking'});
 c.setSpeech({id:2,gesture:'sampleM06'});c.setPerformance({id:2,phase:'speaking'});assert.equal(calls,1);
 old.resolve();await next();assert.equal(calls,2);assert.deepEqual(m.motions.at(-1),['TapBody',1,2]);
 assert(m.motionStops>=2);c.setSpeech({id:null});await next();assert.equal(c.activeGesture,null);c.destroy();
});

await check('nod is a real head movement, missing intents fall back and pointer cannot steal active gaze', async () => {
 const f=setup();await f.controller.setMode('live2d');const c=f.controller,m=f.models[0];
 c.setSpeech({id:1,gesture:'nod'});c.setPerformance({id:1,phase:'speaking'});f.step(50);assert(m.values.get('ParamAngleY') < -6);
 const before=m.focusValue;c._gaze({clientX:999,clientY:100});assert.equal(m.focusValue,before);
 f.step(150);assert.equal(c.activeGesture,null);assert(Math.abs(m.values.get('ParamAngleY'))<1);
 c.setSpeech({id:2,gesture:'not-an-action'});c.setPerformance({id:2,phase:'speaking'});assert.equal(c.activeGesture,null);c.destroy();
});

await check('silent call yield reuses nod while keeping mouth closed and no speech identity',async()=>{
 const f=setup();await f.controller.setMode('live2d');const c=f.controller,m=f.models[0];
 c.setSpeech({id:null});c.acknowledgeListening();f.step(50);
 assert.equal(c.speech.id,null);assert.equal(c.phase,'listening');assert.equal(c.activity.speaking,false);assert(m.values.get('ParamAngleY') < -6);
 c.destroy();
});

await check('emotion survives speech completion, then settles; a new mood blends from the visible face and attitude', async () => {
 const f=setup();await f.controller.setMode('live2d');const c=f.controller,m=f.models[0];
 c.setSpeech({id:1,mood:'happy'});c.setPerformance({id:1,phase:'speaking'});f.step(90);
 const smile=m.values.get('ParamMouthForm');assert(smile>.9);assert(m.values.get('ParamAngleY')>3);
 c.setPerformance({id:1,phase:'listening'});c.setSpeech({id:null});f.step(120);
 assert.equal(c.getPerformanceState().mood,'happy');assert(m.values.get('ParamMouthForm')>.9,'Completion retains the feeling');
 c.setSpeech({id:2,mood:'sad'});c.setPerformance({id:2,phase:'speaking'});
 const oldAngle=m.values.get('ParamAngleY');f.step();assert(m.values.get('ParamMouthForm')>0);assert(Math.abs(m.values.get('ParamAngleY')-oldAngle)<2);
 f.step(100);assert(m.values.get('ParamMouthForm')<-.9);assert(m.values.get('ParamAngleY')<-8);
 c.setPerformance({id:2,phase:'listening'});c.setSpeech({id:null});f.step(210,50);
 assert.equal(c.getPerformanceState().mood,'neutral');assert(Math.abs(m.values.get('ParamMouthForm')-.12)<.02);c.destroy();
});

await check('sample arm takeover and interruption blend from the displayed pose without a one-frame jump', async () => {
 const f=setup();await f.controller.setMode('live2d');const c=f.controller,m=f.models[0],core=m.internalModel.coreModel;
 const ids=Object.keys(LIVE2D_CONFIG.restPose);
 Object.assign(core,{getParameterIndex:id=>ids.indexOf(id),getParameterCount:()=>ids.length,
 getParameterValueById:id=>m.values.get(id)??LIVE2D_CONFIG.restPose[id]??0,
 getParameterDefaultValue:i=>LIVE2D_CONFIG.restPose[ids[i]],getParameterMinimumValue:()=>-10,getParameterMaximumValue:()=>10});
 f.step(10);const rest=m.values.get('ParamArmLA');assert.equal(rest,1);
 const render=f.apps[0].render;f.apps[0].render=function(){if(c.activeGesture?.kind==='clip')m.values.set('ParamArmLA',-1);render.call(this);};
 c.setSpeech({id:1,gesture:'sampleM26'});c.setPerformance({id:1,phase:'speaking'});f.step();
 assert(m.values.get('ParamArmLA')>.7,'First clip frame blends from the old arms');f.step(60);assert(m.values.get('ParamArmLA')<-.95);
 const before=m.values.get('ParamArmLA');c.setSpeech({id:2,gesture:'nod'});c.setPerformance({id:2,phase:'speaking'});f.step();
 assert(Math.abs(m.values.get('ParamArmLA')-before)<.15,'Interruption retains the displayed arm pose');f.step(120);assert(m.values.get('ParamArmLA')>.99);c.destroy();
});

await check('gesture has a readable held pose, recovers to mood posture, and pause preserves emotional time', async () => {
 const f=setup();await f.controller.setMode('live2d');const c=f.controller,m=f.models[0];
 c.setSpeech({id:1,mood:'warm',gesture:'lean'});c.setPerformance({id:1,phase:'speaking'});f.step(70);
 assert.equal(c.getPerformanceState().gesturePhase,'hold');const held=m.values.get('ParamBodyAngleY');assert(held>5);
 c.setPaused(true);const strength=c.emotionStrength,time=c.time;f.step(100);assert.equal(c.time,time);assert.equal(c.emotionStrength,strength);c.setPaused(false);
 f.step(160);assert.equal(c.getPerformanceState().gesturePhase,'rest');assert(Math.abs(m.values.get('ParamBodyAngleY')-2)<.1);assert(m.values.get('ParamMouthForm')>.6);c.destroy();
});

await check('a serious explanation and warm reassurance select mood-appropriate accents, not the sample folded-arms clip', async () => {
 const f=setup();await f.controller.setMode('live2d');const c=f.controller,m=f.models[0];
 c.setSpeech({id:1,mood:'serious',gesture:'explain'});c.setPerformance({id:1,phase:'speaking'});assert.equal(c.activeGesture.name,'openArms');
 c.setSpeech({id:2,mood:'warm',gesture:'explain'});c.setPerformance({id:2,phase:'speaking'});assert.equal(c.activeGesture.name,'present');assert.equal(m.motions.length,0);c.destroy();
});

await check('large gestures activate alternate arm art, reach distinct poses, and recover after interruption', async () => {
 const f=setup();await f.controller.setMode('live2d');const c=f.controller,m=f.models[0],core=m.internalModel.coreModel;
 const ids=Object.keys(LIVE2D_CONFIG.restPose),parts=new Map();
 Object.assign(core,{getParameterIndex:id=>ids.indexOf(id),getParameterCount:()=>ids.length,
 getParameterValueById:id=>m.values.get(id)??LIVE2D_CONFIG.restPose[id]??0,
 getParameterDefaultValue:i=>LIVE2D_CONFIG.restPose[ids[i]],getParameterMinimumValue:()=>-1,getParameterMaximumValue:i=>/Arm.B/.test(ids[i])?5:1,
 setPartOpacityById:(id,value)=>parts.set(id,value)});
 c.setSpeech({id:1,mood:'serious',gesture:'openArms'});c.setPerformance({id:1,phase:'speaking'});f.step(150);
 assert(m.values.get('ParamArmLB')>4.8);assert(m.values.get('ParamArmRB')>4.8);assert(parts.get('Part01ArmLB001')>.99);
 const previous=m.values.get('ParamArmLB');c.setSpeech({id:2,mood:'shy',gesture:'coverMouth'});c.setPerformance({id:2,phase:'speaking'});f.step();
 assert(Math.abs(m.values.get('ParamArmLB')-previous)<.3,'Do not jump on interruption');f.step(150);
 assert(m.values.get('ParamArmLB')>2.8&&m.values.get('ParamArmLB')<3.1);assert(m.values.get('ParamArmRB')<.05);assert.equal(m.values.get('ParamHandChangeR'),0,'Cover-mouth uses the inward-facing curved hand');
 for(const side of ['L','R'])assert(Math.abs(parts.get(`Part01Arm${side}A001`)+parts.get(`Part01Arm${side}B001`)-1)<1e-6);
 c.setPerformance({id:2,phase:'listening'});f.step(150);assert(parts.get('Part01ArmLB001')<.001);assert(m.values.get('ParamArmLA')>.99);c.destroy();
});

await check('continuous dialogue holds the old arms past clip duration and hands directly to the next pose', async () => {
 const f=setup();await f.controller.setMode('live2d');const c=f.controller,m=f.models[0],core=m.internalModel.coreModel,ids=Object.keys(LIVE2D_CONFIG.restPose);
 Object.assign(core,{getParameterIndex:id=>ids.indexOf(id),getParameterCount:()=>ids.length,getParameterValueById:id=>m.values.get(id)??LIVE2D_CONFIG.restPose[id]??0,getParameterDefaultValue:i=>LIVE2D_CONFIG.restPose[ids[i]],getParameterMinimumValue:()=>-1,getParameterMaximumValue:i=>/Arm.B/.test(ids[i])?5:1});
 c.config.performance.cooldownMs=0;
 c.setSpeech({id:1,mood:'warm',gesture:'openArms',gestureHoldMs:6500});c.setPerformance({id:1,phase:'speaking'});f.step(330);
 assert.equal(c.getPerformanceState().gesturePhase,'hold');assert.equal(c.armMix,1);assert(m.values.get('ParamArmLB')>4.99);assert(m.values.get('ParamArmRB')>4.99);
 c.setPaused(true);const age=c.getPerformanceState().gestureAgeMs;f.step(300);assert.equal(c.getPerformanceState().gestureAgeMs,age);c.setPaused(false);
 c.setSpeech({id:2,mood:'shy',gesture:'coverMouth',gestureHoldMs:6000});c.setPerformance({id:2,phase:'speaking'});
 for(let i=0;i<36;i++){f.step();assert.equal(c.armMix,1);assert(m.values.get('ParamArmLB')>=3,'Cover-mouth must not travel through folded B=0');assert(m.values.get('ParamArmRB')>=0);}
 assert(Math.abs(m.values.get('ParamArmLB')-3)<.01);
 c.setSpeech({id:3,mood:'warm',gesture:'nod',carryArms:true});c.setPerformance({id:3,phase:'speaking'});f.step(200);
 assert.equal(c.armMix,1);assert.equal(m.values.get('ParamArmLB'),3,'Head cue keeps the previous arms after its own accent ends');
 c.setSpeech({id:4,mood:'happy',gesture:'discover'});f.step(13);assert(m.values.get('ParamArmRB')>4.8,'Sudden cues still interrupt held arms immediately');c.destroy();
});

await check('lowering aligns alternate arm drawings before switching and stays down when the sequence ends', async () => {
 const f=setup();await f.controller.setMode('live2d');const c=f.controller,m=f.models[0],core=m.internalModel.coreModel,ids=Object.keys(LIVE2D_CONFIG.restPose);
 Object.assign(core,{getParameterIndex:id=>ids.indexOf(id),getParameterCount:()=>ids.length,getParameterValueById:id=>m.values.get(id)??LIVE2D_CONFIG.restPose[id]??0,getParameterDefaultValue:i=>LIVE2D_CONFIG.restPose[ids[i]],getParameterMinimumValue:()=>-1,getParameterMaximumValue:i=>/Arm.B/.test(ids[i])?5:1});
 c.setSpeech({id:1,mood:'shy',gesture:'coverMouth',gestureHoldMs:5000});c.setPerformance({id:1,phase:'speaking'});f.step(90);
 c.setSpeech({id:2,mood:'warm',gesture:'relaxArms',gestureHoldMs:5000});c.setPerformance({id:2,phase:'speaking'});
 f.step(24);assert.equal(c.armMix,1,'Keep raised drawing visible while withdrawing the hand');assert(m.values.get('ParamArmLB')<.1);assert.equal(m.values.get('ParamArmLA'),1);
 f.step(12);assert(c.armMix<.001);assert(m.values.get('ParamArmLA')<1);
 f.step(24);assert.equal(m.values.get('ParamArmLA'),-1);assert.equal(m.values.get('ParamArmRA'),-1);
 c.setSpeech({id:null,carryArms:true});f.step(300);assert.equal(c.armMix,0);assert.equal(m.values.get('ParamArmLA'),-1,'No bounce back to folded arms after the last line');
 c.setSpeech({id:3,mood:'neutral',gesture:'none'});f.step(180);assert(m.values.get('ParamArmLA')>.99,'Independent playback releases the sequence posture');c.destroy();
});

await check('custom sentence rules select supported gestures, allow overrides and do not interpret markup', async () => {
 assert.deepEqual(resolvePreviewIntent('分享一个我的经验：先拆小，再慢慢试。').gesture,'openArms');
 assert.equal(resolvePreviewIntent('你夸我，我有点不好意思了。').gesture,'coverMouth');
 assert.equal(resolvePreviewIntent('我并不难过。').mood,'neutral');
 const manual=resolvePreviewIntent('你好',{mood:'shy',gesture:'present'});assert.equal(manual.mood,'shy');assert.equal(manual.gesture,'present');
 assert.equal(resolvePreviewIntent('<script>not code</script>').mood,'neutral');
 for(const mood of Object.keys(LIVE2D_CONFIG.moods))assert.equal(typeof generatePreviewLine(mood,()=>0),'string');
});

await check('sudden discovery interrupts immediately, reaches the pose within 220 ms and never replays at speech onset', async () => {
 const f=setup();await f.controller.setMode('live2d');const c=f.controller,m=f.models[0],core=m.internalModel.coreModel,ids=Object.keys(LIVE2D_CONFIG.restPose);
 Object.assign(core,{getParameterIndex:id=>ids.indexOf(id),getParameterCount:()=>ids.length,getParameterValueById:id=>m.values.get(id)??LIVE2D_CONFIG.restPose[id]??0,getParameterDefaultValue:i=>LIVE2D_CONFIG.restPose[ids[i]],getParameterMinimumValue:()=>-1,getParameterMaximumValue:i=>/Arm.B/.test(ids[i])?5:1});
 c.setSpeech({id:1,mood:'warm',gesture:'openArms'});c.setPerformance({id:1,phase:'speaking'});f.step(12);assert(m.values.get('ParamArmRB')<2,'Normal cue enters gradually');
 c.setSpeech({id:2,mood:'happy',gesture:'discover'});assert.equal(c.activeGesture.name,'discover');assert.equal(c.getPerformanceState().transition,'sudden');f.step(13);
 assert(m.values.get('ParamArmRB')>4.8);assert.equal(c.getPerformanceState().gesturePhase,'hold');assert(m.values.get('ParamEyeLOpen')>1.4);
 f.step(12);assert(m.values.get('ParamEyeLOpen')>1.4,'Initial surprise must not be hidden by the happy grin');assert(m.values.get('ParamEyeLSmile')<.1);
 const count=c.metrics.gestures;c.setPerformance({id:2,phase:'speaking'});c.setPerformance({id:2,phase:'speaking',resumed:true});assert.equal(c.metrics.gestures,count);
 c.setSpeech({id:3,mood:'happy',gesture:'discover',transition:'smooth'});assert.equal(c.activeGesture,null,'Explicit smooth cue waits for speech onset');
 // Make the old action cooldown irrelevant for this independent manual preview.
 c.config.performance.cooldownMs=0;c.setPerformance({id:3,phase:'speaking'});assert.equal(c.getPerformanceState().enterMs,550);c.destroy();
});

await check('wave uses a stable greeting palm and small bounded forearm movement, not a wrist oscillation', async () => {
 const f=setup();await f.controller.setMode('live2d');const c=f.controller,m=f.models[0],core=m.internalModel.coreModel,ids=Object.keys(LIVE2D_CONFIG.restPose);
 Object.assign(core,{getParameterIndex:id=>ids.indexOf(id),getParameterCount:()=>ids.length,getParameterValueById:id=>m.values.get(id)??LIVE2D_CONFIG.restPose[id]??0,getParameterDefaultValue:i=>LIVE2D_CONFIG.restPose[ids[i]],getParameterMinimumValue:()=>-1,getParameterMaximumValue:i=>/Arm.B/.test(ids[i])?5:1});
 c.setSpeech({id:1,mood:'happy',gesture:'wave',gestureHoldMs:6500});c.setPerformance({id:1,phase:'speaking'});assert.equal(c.activeGesture.name,'handWave');
 let min=5,max=0;
 for(let i=0;i<140;i++){f.step();if(c.getPerformanceState().gesturePhase==='hold'){const arm=m.values.get('ParamArmRB');min=Math.min(min,arm);max=Math.max(max,arm);assert.equal(m.values.get('ParamHandAngleL'),.1);assert.equal(m.values.get('ParamHandDhangeL'),1);}}
 assert(min>=4.45&&max<=4.85);assert(max-min>.1);f.step(80);assert.equal(m.values.get('ParamArmRB'),4.65,'Long dialogue keeps the greeting pose after two waves instead of slowing the wave');c.destroy();
});

await check('saved action library separates automatic actions, candidates, source motions and raw calibration samples', async () => {
 const actions=LIVE2D_CONFIG.gestures;assert.equal(Object.keys(actions).length,28);assert.equal(HARU_ACTION_LIBRARY.calibrationSamples.length,42);
 for(const action of Object.values(actions)){
   assert(action.label);assert(action.review);if(action.review!=='calibrated')assert.equal(action.automatic,false);
   for(const [id,value]of Object.entries(action.arms||{})){const bounds=HARU_ACTION_LIBRARY.parameterLimits[id];assert(bounds&&Number.isFinite(value));assert(value>=Math.min(...bounds)&&value<=Math.max(...bounds));}
   if(action.timing)assert.equal(action.durationMs,action.timing.enterMs+action.timing.holdMs+action.timing.exitMs);
 }
 const cue=resolvePreviewIntent('惊喜地发现那边还有一条路！');assert.equal(cue.gesture,'discover');assert.equal(cue.mood,'happy');
 assert.equal(actions.coverMouth.arms.ParamHandChangeR,0);assert.equal(actions.wavePose.arms.ParamHandDhangeL,1);
});

await check('30 minutes of mixed moods, gestures, interruptions and pause keep bounded state', async () => {
 const f=setup();await f.controller.setMode('live2d');const c=f.controller,m=f.models[0];
 const moods=Object.keys(LIVE2D_CONFIG.moods),gestures=Object.keys(LIVE2D_CONFIG.gestures);
 for(let i=0;i<180;i++){
   c.setSpeech({id:i,mood:moods[i%moods.length],gesture:gestures[i%gestures.length]});
   c.setPerformance({id:i,phase:'speaking'});c.setActivity({id:i,speaking:true,level:.04});f.step(150,50);
   c.setPerformance({id:i,phase:'listening'});c.setPaused(true);const t=c.time;f.step(5,50);assert.equal(c.time,t);c.setPaused(false);f.step(50,50);
   assert(c.gestureHistory.length<=2);assert(c.gestureLastAt.size<=gestures.length);
   assert([...m.values.values()].every(Number.isFinite));assert.equal(f.apps[0].ticker.callbacks.size,1);
 }
 assert(c.time>=1800);assert.equal(f.models.length,1);c.destroy();assert.equal(f.apps[0].ticker.callbacks.size,0);
});

await check('text-only speech keeps a natural mouth rhythm while pause/hidden freeze animation and close the mouth', async () => {
  const fixture = setup(); await fixture.controller.setMode('live2d'); const model = fixture.models[0], app = fixture.apps[0], controller = fixture.controller;
  controller.setSpeech({ id: 'text' }); controller.setActivity({ id: 'text', speaking: true }); fixture.step(8);
  assert(model.values.get('ParamMouthOpenY') > 0); const updates = model.updates;
  controller.setPaused(true); assert.equal(app.running, false); assert.equal(model.values.get('ParamMouthOpenY'), 0); fixture.step(10); assert.equal(model.updates, updates);
  fixture.document.hidden = true; fixture.document.dispatchEvent(new Event('visibilitychange'));
  controller.setPaused(false); assert.equal(app.running, false);
  fixture.document.hidden = false; fixture.document.dispatchEvent(new Event('visibilitychange')); assert.equal(app.running, true);
  fixture.step(4); assert(model.updates > updates);
  fixture.window.dispatchEvent(new Event('pagehide')); assert.equal(app.running, false); assert.equal(model.values.get('ParamMouthOpenY'), 0);
  fixture.window.dispatchEvent(new Event('pageshow')); assert.equal(app.running, true); controller.destroy();
});

await check('gaze and reduced-motion preference affect SDK focus, idle, breathing and gestures, while speech remains usable', async () => {
  const fixture = setup(); await fixture.controller.setMode('live2d'); const model = fixture.models[0], breath = model.internalModel.breath, controller = fixture.controller;
  fixture.elements.stage.dispatchEvent(Object.assign(new Event('pointermove'), { clientX: 900, clientY: 100 }));
  assert(model.focusValue[0] > 0); assert(model.focusValue[1] > 0);
  fixture.media.matches = true; fixture.media.dispatchEvent(new Event('change'));
  assert.equal(model.internalModel.breath, undefined); assert.equal(model.internalModel.motionManager.groups.idle, ''); assert(model.motionStops > 0);
  const motions = model.motions.length;
  controller.setSpeech({ id: 4, gesture: 'sampleM26' }); controller.setActivity({ id: 4, speaking: true }); fixture.step(5);
  assert.equal(model.motions.length, motions); assert(model.values.get('ParamMouthOpenY') > 0);
  fixture.media.matches = false; fixture.media.dispatchEvent(new Event('change')); assert.equal(model.internalModel.breath, breath); assert.equal(model.internalModel.motionManager.groups.idle, '');
  controller.destroy();
});

await check('switching to original art aborts a pending load and disposes a late model without reviving its canvas', async () => {
  const pending = deferred(); let signal;
  const fixture = setup({ loadModel: (_, __, options) => { signal = options.signal; return pending.promise; } });
  const loading = fixture.controller.setMode('live2d'); await next(); assert.equal(fixture.controller.getState().status, 'loading');
  await fixture.controller.setMode('manga'); assert.equal(signal.aborted, true); assert.equal(await loading, false);
  const late = mockModel(); pending.resolve(late); await next();
  assert.equal(late.destroyed, 1); assert.equal(fixture.apps[0].destroyed, true); assert.equal(fixture.apps[0].lost, 1);
  assert.equal(fixture.elements.host.children.length, 0); assert.equal(fixture.elements.stage.dataset.character, 'manga'); fixture.controller.destroy();
});

await check('native fromSync wrapper disables shared updates/interactions and cancels its owned model before load', async () => {
  let callbacks, loadedUrl;
  const model = mockModel(), events = new EventEmitter(); model.on = events.on.bind(events); model.emit = events.emit.bind(events);
  const PIXI = { utils: { isWebGLSupported: () => true }, live2d: { Live2DModel: {
    fromSync(url, options) { loadedUrl = url; callbacks = options; return model; },
  } } };
  const fixture = setup({ loadRuntime: async () => PIXI, loadModel: undefined });
  const loading = fixture.controller.setMode('live2d'); await next();
  assert.equal(loadedUrl, LIVE2D_CONFIG.modelUrl); assert.equal(callbacks.autoUpdate, false); assert.equal(callbacks.autoInteract, false); assert.equal(callbacks.motionPreload, 'ALL');
  await fixture.controller.setMode('manga'); assert.equal(await loading, false); assert.equal(model.destroyed, 1);
  callbacks.onLoad(); await next(); assert.equal(model.destroyed, 1); assert.equal(fixture.elements.host.children.length, 0); fixture.controller.destroy();
});

await check('native loads isolate real URL-keyed texture cache ownership across cancel/retry and late completion', async () => {
  const cache = new Map(), requests = [];
  const PIXI = { utils: { isWebGLSupported: () => true }, live2d: { Live2DModel: {
    fromSync(url, options) {
      const model = mockModel(), internal = model.internalModel, events = new EventEmitter();
      model.internalModel = undefined; model.on = events.on.bind(events); model.emit = events.emit.bind(events); model.textures = [];
      model.destroy = () => { model.destroyed++; model._destroyed = true; internal.destroyed = true; for (const texture of model.textures) texture.destroy(true); };
      const request = { model, url, options,
        settings() {
          const json = { url, FileReferences: { Textures: ['Haru.2048/texture_00.png?revision=1#texture'], Motions: { Idle: [{ File: 'motions/idle.motion3.json' }] } } };
          events.emit('settingsJSONLoaded', json);
          request.json = json; request.textureUrl = new URL(json.FileReferences.Textures[0], 'https://z.example.test' + url).href;
          // Match Pixi's actual Texture.from(string) cache: the complete URL is
          // the key, so without our owner query both model instances share this.
          if (!cache.has(request.textureUrl)) cache.set(request.textureUrl, { destroyed: false, destroy() { this.destroyed = true; } });
          request.texture = cache.get(request.textureUrl);
        },
        complete() {
          model.internalModel = internal; events.emit('modelLoaded');
          model.textures = [request.texture]; events.emit('textureLoaded'); options.onLoad();
        },
      };
      requests.push(request); return model;
    },
  } } };
  const fixture = setup({ loadRuntime: async () => PIXI, loadModel: undefined });
  const old = fixture.controller.setMode('live2d'); await next(); requests[0].settings();
  const current = fixture.controller.retry(); await next(); requests[1].settings(); requests[1].complete();
  assert.equal(await current, true); assert.equal(await old, false);
  assert.notEqual(requests[0].textureUrl, requests[1].textureUrl); assert.notEqual(requests[0].texture, requests[1].texture);
  assert.equal(new URL(requests[1].textureUrl).searchParams.get('revision'), '1'); assert.equal(new URL(requests[1].textureUrl).hash, '#texture');
  assert.equal(requests[1].json.url, LIVE2D_CONFIG.modelUrl); assert.equal(requests[1].json.FileReferences.Motions.Idle[0].File, 'motions/idle.motion3.json');
  requests[0].complete(); await next();
  assert.equal(requests[0].texture.destroyed, true); assert.equal(requests[1].texture.destroyed, false);
  assert.equal(fixture.controller.session.model, requests[1].model); assert.equal(fixture.elements.host.children[0], fixture.apps[1].view);
  fixture.controller.destroy(); assert.equal(requests[1].texture.destroyed, true);
});

await check('retry can finish before an obsolete load; the obsolete model cannot replace the current one', async () => {
  const old = deferred(), current = deferred(); let calls = 0;
  const fixture = setup({ loadModel: () => (++calls === 1 ? old.promise : current.promise) });
  const first = fixture.controller.setMode('live2d'); await next(); const second = fixture.controller.retry(); await next();
  const winner = mockModel(); current.resolve(winner); assert.equal(await second, true); assert.equal(await first, false);
  const late = mockModel(); old.resolve(late); await next();
  assert.equal(fixture.controller.session.model, winner); assert.equal(winner.destroyed, 0); assert.equal(late.destroyed, 1); assert.equal(fixture.elements.host.children[0], fixture.apps[1].view);
  fixture.controller.destroy(); assert.equal(winner.destroyed, 1);
});

await check('failure, timeout and WebGL/context loss keep or restore the original art with retry status', async () => {
  const failure = setup({ loadModel: async () => { throw Error('offline model'); } });
  assert.equal(await failure.controller.setMode('live2d'), false); assert.equal(failure.controller.status, 'error'); assert.equal(failure.elements.stage.dataset.character, 'manga'); assert.equal(failure.elements.retryButton.hidden, false); assert.equal(failure.apps[0].destroyed, true); failure.controller.destroy();
  const config = structuredClone(LIVE2D_CONFIG); config.runtime.enabled = true; config.loadTimeoutMs = 5;
  const late = deferred(), timeout = setup({ config, loadModel: () => late.promise });
  assert.equal(await timeout.controller.setMode('live2d'), false); assert.equal(timeout.controller.reason, 'timeout'); assert.equal(timeout.apps[0].destroyed, true);
  const lateModel = mockModel(); late.resolve(lateModel); await next(); assert.equal(lateModel.destroyed, 1); timeout.controller.destroy();
  const webgl = setup({ loadRuntime: async () => ({ utils: { isWebGLSupported: () => false } }) });
  assert.equal(await webgl.controller.setMode('live2d'), false); assert.equal(webgl.controller.reason, 'webgl'); assert.equal(webgl.apps.length, 0); webgl.controller.destroy();
  const context = setup(); await context.controller.setMode('live2d'); const lost = new Event('webglcontextlost', { cancelable: true }); context.apps[0].view.dispatchEvent(lost);
  assert.equal(lost.defaultPrevented, true); assert.equal(context.controller.status, 'error'); assert.equal(context.controller.reason, 'context'); assert.equal(context.elements.stage.dataset.character, 'manga'); assert.equal(context.models[0].destroyed, 1); context.controller.destroy();
});

await check('destroy releases ticker/model/WebGL/canvas/observer and ignores future retries or document events', async () => {
  const fixture = setup(); await fixture.controller.setMode('live2d'); const app = fixture.apps[0], model = fixture.models[0];
  fixture.controller.destroy(); fixture.controller.destroy();
  assert.equal(app.destroyed, true); assert.equal(app.running, false); assert.equal(app.ticker.callbacks.size, 0); assert.equal(app.lost, 1); assert.equal(model.destroyed, 1); assert.equal(fixture.observer().disconnected, true);
  assert.equal(fixture.document.body.classList.contains('live2d-active'), false); assert.equal(fixture.elements.host.children.length, 0);
  fixture.document.dispatchEvent(new Event('visibilitychange')); fixture.window.dispatchEvent(new Event('pageshow'));
  assert.equal(await fixture.controller.retry(), false); assert.equal(fixture.apps.length, 1);
});

await check('chat-selected cues start smoothly with speech, suppress repeats and never cycle while waiting', async () => {
  const fixture=setup(),c=fixture.controller;await c.setMode('live2d');
  c.setSpeech({id:'chat-1',mood:'happy',gesture:'discover',transition:'smooth'});
  assert.equal(c.activeGesture,null,'Smooth chat cue does not jump before playback');
  c.setPerformance({id:'chat-1',phase:'speaking'});assert.equal(c.activeGesture.name,'discover');assert.equal(c.activeGesture.sudden,false);
  c.setSpeech({id:'chat-2',mood:'happy',gesture:'discover',transition:'smooth'});
  c.setPerformance({id:'chat-2',phase:'speaking'});assert.equal(c.activeGesture,null,'Repeated gesture is omitted rather than replaced');
  const count=c.metrics.gestures;fixture.step(700);assert.equal(c.metrics.gestures,count,'No scheduled rotation or retry');
  c.setSpeech({id:'chat-3',mood:'sad',gesture:'none',transition:'smooth'});c.setPerformance({id:'chat-3',phase:'speaking'});
  assert.equal(c.emotion.mood,'sad');assert.equal(c.activeGesture,null);assert.equal(c.metrics.gestures,count);
  c.setSpeech({id:null});fixture.step(700);assert.equal(c.metrics.gestures,count);c.destroy();
});

console.log(`\n${checks} Live2D lifecycle regression scenarios passed (offline SDK mocks, no Core downloads).`);
