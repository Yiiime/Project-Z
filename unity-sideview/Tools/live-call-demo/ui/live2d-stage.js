import { LIVE2D_CONFIG } from './live2d-config.js';

const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
const ease = value => { const t = clamp(value, 0, 1); return t * t * (3 - 2 * t); };
// Anticipation -> readable pose -> hold -> recovery. All curves start/end at 0.
const gestureCurve = (u, points) => {
  for (let i = 1; i < points.length; i++) {
    if (u <= points[i][0]) {
      const [a, x] = points[i - 1], [b, y] = points[i];
      return x + (y - x) * ease((u - a) / (b - a));
    }
  }
  return points.at(-1)[1];
};
const ARM_IDS = ['ParamArmLA','ParamArmRA','ParamArmLB','ParamArmRB','ParamHandAngleR','ParamHandAngleL','ParamHandChangeR','ParamHandDhangeL'];
const abortError = () => Object.assign(new Error('Live2D load canceled'), { name: 'AbortError' });
function issue(code) { return Object.assign(new Error(code), { code }); }
let textureGeneration = 0;

function loadScript(document, url, ready, signal, integrity = '') {
  if (ready()) return Promise.resolve();
  return new Promise((resolve, reject) => {
    const script = document.createElement('script'); let settled = false;
    const done = error => {
      if (settled) return; settled = true;
      signal.removeEventListener('abort', cancel); script.onload = script.onerror = null;
      if (error) { script.remove(); reject(error); } else resolve();
    };
    const cancel = () => done(abortError());
    script.async = false; script.src = url; script.dataset.live2dRuntime = 'true';
    if (integrity) { script.integrity = integrity; script.crossOrigin = 'anonymous'; }
    script.onload = () => done(ready() ? null : issue('runtime'));
    script.onerror = () => done(issue('runtime'));
    signal.addEventListener('abort', cancel, { once: true });
    if (signal.aborted) cancel(); else document.head.append(script);
  });
}

/** Dependency order is important: PIXI -> official Core -> Cubism display. */
export const loadLive2DRuntime = async (config, { document = globalThis.document, globalObject = globalThis, signal } = {}) => {
  await loadScript(document, config.runtime.pixiUrl, () => Boolean(globalObject.PIXI?.Application), signal);
  if (signal.aborted) throw abortError();
  await loadScript(document, config.runtime.coreUrl, () => Boolean(globalObject.Live2DCubismCore), signal, config.runtime.coreIntegrity);
  if (signal.aborted) throw abortError();
  await loadScript(document, config.runtime.displayUrl, () => Boolean(globalObject.PIXI?.live2d?.Live2DModel), signal);
  if (signal.aborted) throw abortError();
  // Sample motion sounds must never overlap with Vivi or play while muted.
  if (globalObject.PIXI.live2d.config) globalObject.PIXI.live2d.config.sound = false;
  return globalObject.PIXI;
};

function releaseModel(model) {
  if (!model) return;
  model.autoUpdate = false;
  if (model.internalModel && !model._destroyed && !model.internalModel.destroyed) {
    try { model.destroy({ children: true, texture: true, baseTexture: true }); return; } catch {
      // A partially loaded renderer may not own a GL context yet. Still release
      // the native Core allocation if its normal destructor stopped early.
      const core = model.internalModel.coreModel;
      try { if (core?.getModel?.()) core.release(); } catch {}
    }
  }
  // fromSync can still be waiting for a model or a texture. Destroy signals
  // cancel its owned XHRs; callbacks below also release any late resources.
  try { model.emit?.('destroy'); } catch {}
  for (const texture of model.textures || []) { try { texture.destroy(true); } catch {} }
}

function loadModel(PIXI, url, { signal } = {}) {
  return new Promise((resolve, reject) => {
    let model, settled = false;
    const textureOwner = `live2d-${++textureGeneration}`;
    const finish = (error, candidate) => {
      if (signal.aborted || settled) { releaseModel(candidate || model); return; }
      settled = true; signal.removeEventListener('abort', cancel);
      if (error) { releaseModel(model); reject(error); } else resolve(candidate);
    };
    const cancel = () => { if (settled) return; settled = true; releaseModel(model); reject(abortError()); };
    signal.addEventListener('abort', cancel, { once: true });
    try {
      model = PIXI.live2d.Live2DModel.fromSync(url, {
        autoUpdate: false, autoInteract: false, motionPreload: 'ALL',
        onLoad: () => finish(null, model), onError: error => finish(error),
      });
      // The factory emits this synchronously before it constructs settings and
      // calls Texture.fromURL. Unique texture URLs give each load ownership of
      // its own Pixi cache entries, so late canceled loads cannot destroy the
      // current model's shared BaseTexture. A model-JSON query alone would not
      // propagate to its relative textures.
      model.on('settingsJSONLoaded', json => {
        const refs = json?.FileReferences;
        if (Array.isArray(refs?.Textures)) refs.Textures = refs.Textures.map(file => {
          if (typeof file !== 'string') return file;
          const hashIndex = file.indexOf('#'), path = hashIndex < 0 ? file : file.slice(0, hashIndex), hash = hashIndex < 0 ? '' : file.slice(hashIndex);
          return `${path}${path.includes('?') ? '&' : '?'}z-live2d-owner=${textureOwner}${hash}`;
        });
      });
      model.on('modelLoaded', () => { if (signal.aborted) releaseModel(model); });
      model.on('textureLoaded', () => { if (signal.aborted) releaseModel(model); });
      if (signal.aborted) cancel();
    } catch (error) { finish(error); }
  });
}

function loadRoom(url, { window, signal }) {
  return new Promise((resolve, reject) => {
    const image = new window.Image(); let settled = false;
    const done = error => {
      if (settled) return; settled = true;
      signal.removeEventListener('abort', cancel); image.onload = image.onerror = null;
      if (error) reject(error); else resolve();
    };
    const cancel = () => { image.src = ''; done(abortError()); };
    image.onload = () => done(); image.onerror = () => done(issue('background'));
    signal.addEventListener('abort', cancel, { once: true });
    if (signal.aborted) cancel(); else image.src = url;
  });
}

/**
 * One owned Pixi ticker and canvas; no shared ticker or model-supplied audio.
 * Cubism's own blink, breath, idle motions and physics update on that ticker.
 * Lip sync is applied at beforeModelUpdate, after motion/physics, before draw.
 * Primary API: guansss/pixi-live2d-display v0.4.0 Complete Guide & source.
 */
export class Live2DStage extends EventTarget {
  constructor(options = {}) {
    super();
    this.config = options.config || LIVE2D_CONFIG;
    this.document = options.document || globalThis.document;
    this.window = options.window || globalThis.window;
    this.stage = options.stage;
    this.host = options.host;
    this.room = options.room;
    this.foreground = options.foreground;
    this.notice = options.notice;
    this.noticeText = options.noticeText;
    this.retryButton = options.retryButton;
    this.storage = options.storage;
    if (!this.storage) { try { this.storage = this.window?.localStorage; } catch {} }
    this.now = options.now || (() => globalThis.performance?.now?.() || Date.now());
    // Browser platform methods must retain their Window receiver; assigning the
    // native function and later calling this.setTimer() causes Illegal invocation.
    this.setTimer = options.setTimeout || ((callback, delay) => globalThis.setTimeout(callback, delay));
    this.clearTimer = options.clearTimeout || (timer => globalThis.clearTimeout(timer));
    this.runtimeLoader = options.loadRuntime || loadLive2DRuntime;
    this.modelLoader = options.loadModel || loadModel;
    this.roomLoader = options.loadRoom || loadRoom;
    this.applicationFactory = options.createApplication || ((PIXI, settings) => new PIXI.Application(settings));
    this.ResizeObserver = options.ResizeObserver || globalThis.ResizeObserver;
    this.preferred = 'live2d';
    try { if (this.storage?.getItem(this.config.storageKey) === 'manga') this.preferred = 'manga'; } catch {}
    this.status = 'off'; this.message = ''; this.reason = '';
    this.motionQuery = this.window?.matchMedia?.('(prefers-reduced-motion: reduce)');
    this.manualReduced = Boolean(options.reducedMotion);
    this.paused = false; this.hidden = Boolean(this.document?.hidden); this.reduced = this.manualReduced || Boolean(this.motionQuery?.matches);
    this.session = null; this.destroyed = false; this.generation = 0; this.pending = null;
    this.speech = { id: null, mood: 'neutral', gesture: '' };
    this.emotion = { mood: 'neutral', endedAt: null, startedAt: 0 }; this.emotionStrength = 1; this.frameMs = 16.67;
    this.phase = 'idle'; this.phaseAt = 0; this.playbackStarted = false;
    this.gestureHistory = []; this.gestureLastAt = new Map(); this.activeGesture = null;
    this.pose = {}; this.face = {}; this.armMix = 0; this.carriedArms = null; this.pointerUntil = 0; this.noticeUntil = 0; this.waiting = false;
    this.metrics = { frames: 0, slowFrames: 0, gestures: 0, skipped: 0 };
    this.activity = { speaking: false, level: null, at: 0 };
    this.mouth = 0; this.time = 0;
    this.onPointerMove = event => this._gaze(event);
    this.onPointerLeave = () => { this.pointerUntil = 0; };
    this.onVisibility = () => { this.hidden = Boolean(this.document.hidden); this._syncPlayback(); };
    this.onPageHide = () => { this.hidden = true; this._syncPlayback(); };
    this.onPageShow = () => { this.hidden = Boolean(this.document.hidden); this._syncPlayback(); };
    this.onResize = () => this.resize();
    this.onMotionPreference = () => { this.reduced = this.manualReduced || Boolean(this.motionQuery?.matches); this._configureMotion(); this._syncPlayback(); };
    this.onRetry = () => { void this.retry(); };
    this.stage?.addEventListener('pointermove', this.onPointerMove);
    this.stage?.addEventListener('pointerleave', this.onPointerLeave);
    this.document?.addEventListener('visibilitychange', this.onVisibility);
    this.window?.addEventListener('pagehide', this.onPageHide);
    this.window?.addEventListener('pageshow', this.onPageShow);
    this.window?.addEventListener('resize', this.onResize);
    this.retryButton?.addEventListener('click', this.onRetry);
    this.motionQuery?.addEventListener?.('change', this.onMotionPreference);
    if (this.ResizeObserver && this.stage) { this.observer = new this.ResizeObserver(this.onResize); this.observer.observe(this.stage); }
  }

  getState() { return { preferred: this.preferred, status: this.status, active: this.status === 'ready', message: this.message, reason: this.reason, label: this.config.label }; }
  _notify(status, reason = '') {
    this.status = status; this.reason = reason;
    this.message = status === 'ready' ? 'Live2D 示例已就绪' : status === 'loading' ? '动态形象准备中，先看看原画。' : status === 'error'
      ? reason === 'timeout' ? '动态形象准备超时，已显示原画。' : reason === 'webgl' || reason === 'context' ? '动态形象暂时无法显示，已切回原画。' : '动态形象暂时加载不上，已显示原画。'
      : status === 'blocked' ? '动态形象尚在准备，当前显示原画。'
      : status === 'offline' ? '离线预览使用原画，启动本地服务可体验 Live2D。' : '正在使用 Z 的原画';
    const active = status === 'ready';
    this.stage?.classList.toggle('live2d-active', active);
    this.document?.body?.classList.toggle('live2d-active', active);
    if (this.stage) { this.stage.dataset.character = active ? 'live2d' : 'manga'; this.stage.dataset.live2dState = status; }
    if (this.notice) this.notice.hidden = !['loading', 'error', 'blocked', 'offline'].includes(status);
    if (this.noticeText) this.noticeText.textContent = this.message;
    if (this.retryButton) this.retryButton.hidden = status !== 'error';
    this.dispatchEvent(new CustomEvent('change', { detail: this.getState() }));
  }

  async setMode(mode, { persist = true, force = false } = {}) {
    if (this.destroyed || !['live2d', 'manga'].includes(mode)) return false;
    if (!force && mode === this.preferred && (this.status === 'ready' || this.status === 'loading')) return this.status === 'ready';
    this.preferred = mode;
    if (persist) { try { this.storage?.setItem(this.config.storageKey, mode); } catch {} }
    const generation = ++this.generation;
    this.pending?.abort(); this.pending = null; this._dispose();
    if (mode === 'manga') { this._notify('off'); return true; }
    if (this.window?.location?.protocol === 'file:') { this._notify('offline'); return false; }
    if (!this.config.runtime.enabled) { this._notify('blocked'); return false; }
    this._notify('loading');
    const controller = new AbortController(); this.pending = controller;
    // The portable HTML builder sets a data-URL background before app startup.
    // Reuse it instead of requesting a nonexistent /assets route from a Worker.
    let roomUrl = this.config.roomUrl;
    const background = this.room?.style?.backgroundImage;
    if (background?.startsWith('url(') && background.endsWith(')')) {
      const value = background.slice(4, -1).trim();
      roomUrl = /^['"]/.test(value) ? value.slice(1, -1) : value;
    }
    let timer, timedOut = false;
    const aborted = new Promise((_, reject) => controller.signal.addEventListener('abort', () => reject(abortError()), { once: true }));
    timer = this.setTimer(() => { timedOut = true; controller.abort(); }, this.config.loadTimeoutMs);
    try {
      const [PIXI] = await Promise.race([Promise.all([
        this.runtimeLoader(this.config, { document: this.document, globalObject: this.window, signal: controller.signal }),
        this.roomLoader(roomUrl, { window: this.window, signal: controller.signal }),
      ]), aborted]);
      if (!this._current(generation)) return false;
      if (PIXI.utils?.isWebGLSupported && !PIXI.utils.isWebGLSupported()) throw issue('webgl');
      const bounds = this.stage.getBoundingClientRect();
      let app;
      try {
        app = this.applicationFactory(PIXI, { width: Math.max(1, bounds.width), height: Math.max(1, bounds.height), backgroundAlpha: 0, antialias: true, autoStart: false, sharedTicker: false, autoDensity: true, resolution: Math.min(2, this.window.devicePixelRatio || 1), powerPreference: 'low-power' });
      } catch { throw issue('webgl'); }
      this.session = { app, model: null, generation, PIXI };
      app.stop(); app.ticker.remove(app.render, app);
      if (!app.renderer?.gl) throw issue('webgl');
      const modelTask = Promise.resolve(this.modelLoader(PIXI, this.config.modelUrl, { signal: controller.signal })).then(model => {
        if (!this._current(generation) || controller.signal.aborted) { releaseModel(model); throw abortError(); }
        return model;
      });
      const model = await Promise.race([modelTask, aborted]);
      if (!this._current(generation)) { releaseModel(model); return false; }
      const session = this.session; session.model = model;
      model.autoUpdate = false; model.autoInteract = false;
      session.breath = model.internalModel.breath;
      session.idle = model.internalModel.motionManager.groups.idle;
      app.stage.addChild(model);
      app.view.setAttribute('aria-hidden', 'true'); app.view.classList.add('live2d-canvas');
      session.onContextLost = event => { event.preventDefault?.(); if (this._current(generation)) this._fallback('context'); };
      app.view.addEventListener('webglcontextlost', session.onContextLost);
      session.beforeUpdate = () => this._applyParameters();
      model.internalModel.on('beforeModelUpdate', session.beforeUpdate);
      session.tick = () => {
        if (this.paused || this.hidden || this.destroyed || this.status !== 'ready') return;
        try { this._renderFrame(clamp(app.ticker.deltaMS || 16.67, 1, 50)); } catch { this._fallback('render'); }
      };
      app.ticker.add(session.tick);
      this._configureMotion(); this.resize();
      this._renderFrame(1);
      this.host.replaceChildren(app.view);
      if (this.room) this.room.style.backgroundImage = `url(${JSON.stringify(roomUrl)})`;
      if (this.foreground) this.foreground.style.backgroundImage = `url(${JSON.stringify(roomUrl)})`;
      this._notify('ready'); this._applyExpression(); this._syncPlayback();
      return true;
    } catch (error) {
      if (!this._current(generation)) return false;
      controller.abort(); this._dispose(); this._notify('error', timedOut ? 'timeout' : error.code || 'load');
      return false;
    } finally {
      this.clearTimer(timer);
      if (this.pending === controller) this.pending = null;
    }
  }
  _current(generation) { return !this.destroyed && this.preferred === 'live2d' && this.generation === generation; }
  retry() { return this.setMode('live2d', { force: true }); }
  _fallback(reason) { ++this.generation; this.pending?.abort(); this.pending = null; this._dispose(); this._notify('error', reason); }

  setSpeech({ id = null, mood = 'neutral', gesture = 'none', resume = false, transition = 'auto', gestureHoldMs = 0, carryArms = false } = {}) {
    gestureHoldMs = Number.isFinite(gestureHoldMs) ? clamp(gestureHoldMs,0,30000) : 0;
    // Engine emits state repeatedly while waiting: never restart a pose every tick.
    if (id === this.speech.id && mood === this.speech.mood && gesture === this.speech.gesture && transition === this.speech.transition && gestureHoldMs === this.speech.gestureHoldMs && Boolean(carryArms) === this.speech.carryArms) return;
    this.carriedArms = carryArms ? {pose:Object.fromEntries(ARM_IDS.map(key=>[key,this.pose[key]??this.config.restPose[key]])),mix:this.armMix} : null;
    this._releaseGesture();
    this.speech = { id, mood: this.config.moods[mood] ? mood : 'neutral', gesture, gestureHoldMs, carryArms:Boolean(carryArms), transition: ['auto','smooth','sudden'].includes(transition) ? transition : 'auto' };
    if (id !== null) this.emotion = { mood: this.speech.mood, endedAt: null, startedAt: this.emotion.mood !== this.speech.mood || this.emotionStrength < .1 ? this.time * 1000 : this.emotion.startedAt };
    else if (this.emotion.endedAt === null) this.emotion.endedAt = this.time * 1000;
    this.reactionStarted = false; this.playbackStarted = Boolean(resume); this.activity = { speaking: false, level: null, at: this.now() }; this.mouth = 0;
    this._setPhase(id === null ? (this.waiting ? 'thinking' : 'listening') : 'thinking');
    const cue=this.config.gestures[gesture];
    if(id!==null&&!resume&&(this.speech.transition==='sudden'||(this.speech.transition==='auto'&&cue?.priority===2))){
      this._gesture();this.reactionStarted=Boolean(this.activeGesture);
    }
  }
  _setPhase(phase) { if (this.phase !== phase) { this.phase = phase; this.phaseAt = this.time * 1000; } }
  setPerformance({ id, phase, resumed = false } = {}) {
    if (id !== this.speech.id || id === null || !['thinking', 'speaking', 'listening'].includes(phase)) return;
    this._setPhase(phase);
    if (phase === 'speaking') {
      this.emotion.endedAt = null;
      if (!this.playbackStarted && !this.reactionStarted && !resumed && !this.paused && !this.hidden) this._gesture();
      this.playbackStarted = true;
    } else if (phase === 'listening') { this._releaseGesture(); this.activity.speaking = false; if (this.emotion.endedAt === null) this.emotion.endedAt = this.time * 1000; }
  }
  setWaiting(value) {
    this.waiting = Boolean(value);
    if (this.speech.id === null && this.waiting) this._setPhase('thinking');
    else if (this.speech.id === null && this.phase === 'thinking') this._setPhase('listening');
  }
  acknowledgeListening(){
    if(this.paused||this.hidden||this.speech.id!==null)return;
    // Reuse the existing nod resource without creating a speech or mouth cue.
    const speech=this.speech;this.speech={...speech,gesture:'nod',transition:'smooth'};
    try{this._gesture();}finally{this.speech=speech;}
  }
  noticeMessage() { this.noticeUntil = this.time * 1000 + 1400; }
  setActivity(detail = {}) {
    if (detail.id !== this.speech.id) return;
    this.activity = { speaking: Boolean(detail.speaking), level: Number.isFinite(detail.level) ? clamp(detail.level, 0, 1) : null, at: this.now() };
  }
  setPaused(value) { this.paused = Boolean(value); this._syncPlayback(); }
  setReducedMotion(value) { this.manualReduced = Boolean(value); this.onMotionPreference(); }
  _configureMotion() {
    const session = this.session; if (!session?.model) return;
    session.app.ticker.maxFPS = this.reduced ? 30 : 60;
    const internal = session.model.internalModel;
    internal.breath = this.reduced ? undefined : session.breath;
    // Sample idle clips contain facial/arm curves. Use restrained procedural idle
    // instead, so unprompted sample gestures cannot compete with dialogue.
    internal.motionManager.groups.idle = '';
    internal.motionManager.stopAllMotions();
    if (this.reduced) { this.carriedArms=null; session.clipDesired = null; this.activeGesture = null; this.pose = {}; internal.focusController?.focus(0, 0, true); }
  }
  _applyExpression() {
    // All faces below are composed in one channel from existing Haru parameters.
    // Avoid independent exp3 files overriding lip-sync or freezing blinking.
    this.session?.model?.internalModel.motionManager.expressionManager?.resetExpression();
  }
  _releaseGesture() {
    if (this.activeGesture) {
      this.activeGesture = null;
      if(this.session) this.session.clipDesired = null;
      this.session?.model?.internalModel.motionManager.stopAllMotions();
    }
  }
  _gesture() {
    const session = this.session;
    if (!session?.model || this.reduced || this.paused || this.hidden) return;
    let name = this.speech.gesture;
    if (name === 'wave') name = 'handWave';
    if (name === 'explain') name = this.config.moods[this.emotion.mood].explainGesture || 'openArms';
    let motion = this.config.gestures[name];
    if (!motion || motion.kind === 'none') { this.metrics.skipped++; return; }
    const at = this.time * 1000;
    // Vary a repeated explanation with a subtle nod, then allow silence.
    if (this.speech.gesture === 'explain' && this.gestureHistory.slice(-2).includes(name)) name = 'nod';
    motion = this.config.gestures[name];
    const sudden=this.speech.transition==='sudden'||(this.speech.transition==='auto'&&motion.priority===2);
    const cooldown=this.config.performance.cooldownMs===0?0:sudden?1200:this.config.performance.cooldownMs;
    if (at - (this.gestureLastAt.get(name) ?? -Infinity) < cooldown) { this.metrics.skipped++; return; }
    if (motion.kind === 'clip' && !session.model.internalModel.motionManager.definitions?.[motion.group]?.[motion.index]) { this.metrics.skipped++; return; }
    let timing=motion.timing;
    if(motion.kind==='arms')timing=sudden?{enterMs:180,holdMs:motion.timing?.holdMs||1200,exitMs:650}:this.speech.transition==='smooth'?{enterMs:550,holdMs:motion.timing.holdMs,exitMs:650}:motion.timing;
    // The A/B arm drawings meet near the folded pose. Align them before changing
    // artwork, then lower A arms once; do not dissolve a raised hand into a low one.
    const lowerBridge = name==='relaxArms' && this.armMix>.01 && !sudden;
    if(timing)timing={...timing,enterMs:lowerBridge?900:timing.enterMs,holdMs:Math.max(timing.holdMs,this.speech.gestureHoldMs||0)};
    const active = { ...motion, name, at, timing, sudden, lowerBridge, waveHoldMs:motion.timing?.holdMs, fromPose:{...this.pose}, fromArmMix:this.armMix,
      durationMs:timing?timing.enterMs+timing.holdMs+timing.exitMs:motion.durationMs };
    this.activeGesture = active; this.gestureLastAt.set(name, at);
    this.gestureHistory.push(name); this.gestureHistory = this.gestureHistory.slice(-2); this.metrics.gestures++;
    if (motion.kind === 'clip') { session.clipDesired = active; void this._drainClips(session); }
  }
  async _drainClips(session) {
    if (session.clipBusy) return;
    session.clipBusy = true;
    try {
      while (this.session === session && session.clipDesired) {
        const desired = session.clipDesired;
        try { await session.model.motion(desired.group, desired.index, 2); } catch {}
        if (this.session !== session) return;
        if (session.clipDesired === desired && this.activeGesture === desired) return;
        // A cancelled async load cannot resurrect an old clip over a newer cue.
        session.model.internalModel.motionManager.stopAllMotions();
      }
    } finally { session.clipBusy = false; }
  }
  _set(core, id, value) {
    const index = core.getParameterIndex?.(id);
    if (index !== undefined && index >= 0 && index < core.getParameterCount()) {
      value = clamp(value, core.getParameterMinimumValue(index), core.getParameterMaximumValue(index));
    }
    core.setParameterValueById(id, value);
  }
  _applyParameters() {
    const core = this.session?.model?.internalModel.coreModel; if (!core) return;
    const set = (id, value) => this._set(core, id, value);
    const params = this.config.parameters, f = this.face;
    set(params.mouthOpen, this.paused || this.hidden ? 0 : this.mouth);
    set(params.mouthForm, f.mouthForm || 0);
    for (const side of ['L', 'R']) {
      set(`ParamBrow${side}Y`, (f.brow || 0) + (side === 'L' ? 1 : -1) * (f.browAsymmetry || 0)); set(`ParamBrow${side}Form`, f.browForm || 0);
      set(`ParamBrow${side}Angle`, f.browAngle || 0); set(`ParamBrow${side}X`, 0);
      set(`ParamEye${side}Smile`, f.eyeSmile || 0);
      // A deterministic blink owns eyelids, including during full-body clips.
      const reactionFace = this.activeGesture?.sudden && this.time * 1000 - this.activeGesture.at < (this.activeGesture.faceMs || 500);
      const period = 4.7, t = this.time % period;
      const blink = !reactionFace && t < 0.16 ? Math.abs(t - 0.08) / 0.08 : 1;
      // A brief closed-eye grin reads as delight; permanently half-closed eyes
      // read as sleepy. Reopen between smiles, without suppressing normal blinks.
      const joyTime = (this.time - this.emotion.startedAt / 1000) % 5;
      const grin = !reactionFace && !this.reduced && this.emotion.mood === 'happy' ? ease((joyTime - .3) / .25) * (1 - ease((joyTime - 1.4) / .35)) * this.emotionStrength : 0;
      set(`ParamEye${side}Open`, blink * (f.eyeOpen ?? 1) * (1 - grin));
    }
    set('ParamTere', f.blush || 0); set('ParamEyeBallForm', f.pupil || 0);
    set('ParamEyeForm', f.eyeForm || 0); set('ParamTear', 0);
    // Sample the clip after SDK animation, blend from the last displayed arms,
    // then retain that exact pose for interruption/recovery. No ownership snap.
    if (this.activeGesture?.kind === 'clip' && !this.paused && !this.hidden) {
      const blend = 1 - Math.exp(-this.frameMs / this.config.performance.armBlendMs);
      for (const id of ARM_IDS) {
        const sampled = core.getParameterValueById?.(id);
        if (Number.isFinite(sampled)) this.pose[id] = (this.pose[id] ?? this.config.restPose[id] ?? sampled) + (sampled - (this.pose[id] ?? this.config.restPose[id] ?? sampled)) * blend;
      }
    }
    for (const [id, value] of Object.entries(this.pose)) set(id, value);
    // The SDK pose controller runs before this hook. Own the four alternate arm
    // opacities here, so a finished sample cannot leave the wrong drawing active.
    if (core.setPartOpacityById) for (const side of ['L','R']) {
      core.setPartOpacityById(`Part01Arm${side}A001`, 1 - this.armMix);
      core.setPartOpacityById(`Part01Arm${side}B001`, this.armMix);
    }
  }
  _updatePerformance(dt) {
    const at = this.time * 1000, mood = this.config.moods[this.emotion.mood];
    const ageSinceEnd = this.emotion.endedAt === null ? 0 : at - this.emotion.endedAt;
    this.emotionStrength = 1 - ease((ageSinceEnd - this.config.performance.emotionHoldMs) / this.config.performance.emotionReleaseMs);
    const cue=this.activeGesture, reacting=cue?.sudden&&at-cue.at<500;
    const desired = cue?.face && at-cue.at<(cue.faceMs||500) ? {...mood,...cue.face} : mood, baseline = this.config.moods.neutral;
    const smooth = 1 - Math.exp(-dt / (reacting?45:this.config.performance.transitionMs));
    for (const key of ['mouthForm','brow','browForm','browAngle','browAsymmetry','eyeSmile','eyeOpen','eyeForm','blush','pupil']) {
      const base = baseline[key] ?? (key === 'eyeOpen' ? 1 : 0);
      const value = base + ((desired[key] ?? (key === 'eyeOpen' ? 1 : 0)) - base) * this.emotionStrength;
      this.face[key] = (this.face[key] ?? base) + (value - (this.face[key] ?? base)) * smooth;
    }
    let active = this.activeGesture;
    if (active && at - active.at >= active.durationMs) { this._releaseGesture(); active = null; }
    const target = { ParamAngleX: 0, ParamAngleY: 0, ParamAngleZ: 0, ParamBodyAngleX: 0, ParamBodyAngleY: 0, ParamBodyAngleZ: 0, ParamEyeBallX: 0, ParamEyeBallY: 0 };
    if (!this.reduced) {
      const age = at - this.phaseAt;
      const gazeCycle = this.config.performance.gazeCameraMs + this.config.performance.gazeChatMs;
      if (this.phase === 'listening' && age > 2400 && this.speech.id === null) this._setPhase('idle');
      let gazeX = 0, gazeY = 0;
      if (this.phase === 'thinking' && this.emotion.mood === 'neutral') { gazeX = -0.2; gazeY = 0.15; target.ParamAngleZ = -3; }
      else if (this.phase === 'listening') { target.ParamAngleY = -2; target.ParamBodyAngleY = 1; }
      if (this.phase === 'idle' && age % 7500 > 5700) gazeX = 0.12;
      if (at < this.noticeUntil || (this.phase === 'speaking' && ['neutral','happy','warm'].includes(this.emotion.mood) && age % gazeCycle > this.config.performance.gazeCameraMs)) { gazeX = 0.3; gazeY = -0.05; }
      // Pointer only controls idle; it cannot steal a speaking/listening cue.
      if (this.phase === 'idle' && at < this.pointerUntil) { gazeX = this.pointerX; gazeY = this.pointerY; }
      target.ParamEyeBallX = gazeX; target.ParamEyeBallY = gazeY;
      target.ParamAngleX = gazeX * 12; target.ParamAngleY += gazeY * 8;
      target.ParamBodyAngleZ = Math.sin(this.time * 0.65) * (this.emotion.mood === 'serious' ? 0.12 : 0.5);
      for (const [id, value] of Object.entries(mood.pose || {})) target[id] = (target[id] || 0) + value * this.emotionStrength;
      if (active) {
        const u = clamp((at - active.at) / active.durationMs, 0, 1);
        const envelope = gestureCurve(u, [[0,0],[.25,1],[.62,1],[1,0]]);
        const energy = ['serious','sad','warm'].includes(this.emotion.mood) ? 0.75 : 1;
        const accent = envelope * energy;
        if (active.kind === 'nod') target.ParamAngleY += gestureCurve(u, [[0,0],[.14,2],[.38,-13],[.52,-13],[1,0]]) * energy;
        if (active.kind === 'shake') target.ParamAngleX += gestureCurve(u, [[0,0],[.22,-12],[.48,12],[.72,-7],[1,0]]) * energy;
        if (active.kind === 'tilt') target.ParamAngleZ -= accent * 10;
        if (active.kind === 'lean') { target.ParamAngleY -= accent * 5; target.ParamBodyAngleY += accent * 5; }
        if (active.kind === 'emphasis') { target.ParamAngleY -= accent * 7; target.ParamBodyAngleZ += accent * 3; }
        if (active.kind === 'lookDown') { target.ParamAngleY -= accent * 14; target.ParamEyeBallY -= accent * 0.35; }

      }
    }
    const age=active?at-active.at:0, timing=active?.timing;
    const enter=timing?ease(age/timing.enterMs):0;
    const recover=timing?ease((age-timing.enterMs-timing.holdMs)/timing.exitMs):0;
    const armWeight=timing?enter*(1-recover):0;
    const useB = !this.reduced && active?.armSet === 'B' ? 1 : 0;
    if(active?.lowerBridge)this.armMix=active.fromArmMix*(1-ease((age-420)/120));
    else if(active?.kind==='arms')this.armMix=active.fromArmMix+(useB-active.fromArmMix)*ease(age/Math.min(timing.enterMs,180));
    else if(this.carriedArms&&active?.kind!=='clip')this.armMix=this.carriedArms.mix;
    else this.armMix += (useB-this.armMix)*(1-Math.exp(-dt/180));
    if (active?.head && !this.reduced) for (const [id,value] of Object.entries(active.head)) target[id] += (value-target[id])*armWeight;
    // Arm clips alone own arms; facial/gaze/head channels remain deterministic.
    // Return arms to the rig's defaults smoothly even after an interrupted clip.
    const core = this.session?.model?.internalModel.coreModel;
    for (const id of ARM_IDS) {
      if (active?.kind === 'clip') continue;
      const index = core?.getParameterIndex?.(id);
      if (index !== undefined && index >= 0 && index < core.getParameterCount()) {
        target[id] = this.carriedArms&&active?.kind!=='arms' ? this.carriedArms.pose[id] : this.config.restPose?.[id] ?? core.getParameterDefaultValue(index);
        if (active?.kind === 'arms') {
          const rest=target[id],goal=active.arms[id]??rest,start=active.fromPose[id]??rest;
          // Hand shapes are separate drawings: choose one, never linger halfway.
          target[id]=/ParamHand(?:Change|Dhange)/.test(id)?goal:age<timing.enterMs?start+(goal-start)*enter:goal+(rest-goal)*recover;
          if(active.lowerBridge&&age<timing.enterMs){
            if(/ParamArm[LR]B/.test(id))target[id]=start+(rest-start)*ease(age/420);
            else if(/ParamArm[LR]A/.test(id))target[id]=1+(goal-1)*ease((age-540)/360);
            else target[id]=age<540?start:goal;
          }
          if(active.waveCycles&&id==='ParamArmRB'&&age>=timing.enterMs&&age<timing.enterMs+active.waveHoldMs){
            const u=(age-timing.enterMs)/active.waveHoldMs;
            target[id]+=.18*Math.sin(u*Math.PI*2*active.waveCycles)*Math.sin(u*Math.PI)**2;
          }
        }
        if (this.pose[id] === undefined) this.pose[id] = core.getParameterValueById(id);
      }
    }
    for (const [id, value] of Object.entries(target)) this.pose[id] = active?.kind==='arms'&&ARM_IDS.includes(id)?value: (this.pose[id] ?? value) + (value - (this.pose[id] ?? value)) * (1 - Math.exp(-dt / (/ParamHand(?:Change|Dhange)/.test(id) ? 65 : ARM_IDS.includes(id) ? 340 : active?.sudden ? 50 : this.config.performance.returnMs)));
  }
  getPerformanceState() {
    const active=this.activeGesture,age=active?this.time*1000-active.at:0;
    const timing=active?.timing;
    const phase=!active?'rest':timing?(age<timing.enterMs?'enter':age<timing.enterMs+timing.holdMs?'hold':'recover'):(age/active.durationMs<.25?'enter':age/active.durationMs<.62?'hold':'recover');
    return { gestureProgress: active?clamp(age/active.durationMs,0,1):0, gesturePhase:phase,
      gestureAgeMs:Math.round(age), transition:active?.sudden?'sudden':'smooth', enterMs:timing?.enterMs??null,
      phase:this.phase, mood:this.emotionStrength<.01?'neutral':this.emotion.mood, emotionStrength:Math.round(this.emotionStrength*100)/100,
      gesture:active?.name||'none', paused:this.paused||this.hidden,
      capabilities:{moods:Object.keys(this.config.moods),gestures:Object.keys(this.config.gestures)},...this.metrics };
  }

  _renderFrame(dt) {
    const session = this.session; if (!session?.model) return;
    this.frameMs = dt; this.time += dt / 1000; this.metrics.frames++; if (dt > 34) this.metrics.slowFrames++;
    this._updatePerformance(dt);
    let target = 0;
    if (!this.paused && !this.hidden && this.activity.speaking) {
      if (this.activity.level !== null) target = this.now() - this.activity.at < 240 ? clamp((this.activity.level - this.config.lipSync.noiseFloor) * this.config.lipSync.gain, 0, 1) : 0;
      else target = (0.12 + Math.abs(Math.sin(this.time * 19)) * 0.62) * (0.65 + Math.sin(this.time * 4.1) * 0.25);
    }
    const smoothing = 1 - Math.exp(-dt / (target > this.mouth ? this.config.lipSync.attackMs : this.config.lipSync.releaseMs));
    this.mouth += (target - this.mouth) * smoothing;
    session.model.update(dt); session.app.render();
  }
  _syncPlayback() {
    const session = this.session; if (!session?.model) return;
    if (this.paused || this.hidden || this.status !== 'ready') {
      session.app.stop(); this.mouth = 0;
      try { this._applyParameters(); session.model.internalModel.coreModel.update(); session.app.render(); } catch {}
    } else session.app.start();
  }
  _gaze(event) {
    if (this.status !== 'ready' || this.paused || this.hidden || this.reduced || this.phase !== 'idle') return;
    const rect = this.stage.getBoundingClientRect(); if (!rect.width || !rect.height) return;
    const x = clamp((event.clientX - rect.left - rect.width / 2) / rect.width, -0.55, 0.55);
    const y = clamp((rect.height * 0.35 - (event.clientY - rect.top)) / rect.height, -0.45, 0.45);
    this.pointerX = x; this.pointerY = y; this.pointerUntil = this.time * 1000 + 1500;
    this.session.model.internalModel.focusController?.focus(x, y);
  }
  resize() {
    const session = this.session; if (!session?.model) return;
    const bounds = this.stage.getBoundingClientRect(), width = Math.max(1, bounds.width), height = Math.max(1, bounds.height);
    session.app.renderer.resize(width, height);
    const model = session.model, layout = this.config.layout;
    const scale = Math.min(height * layout.height / model.internalModel.height, width * layout.maxWidth / model.internalModel.width);
    model.anchor.set(0.5, 0); model.scale.set(scale); model.position.set(width * layout.x, height * layout.top);
    if (this.status === 'ready' && (this.paused || this.hidden)) this._syncPlayback();
  }
  _dispose() {
    const session = this.session; this.session = null; this.mouth = 0; this.activeGesture = null; this.pose = {}; this.face = {}; this.armMix = 0; this.carriedArms = null;
    if (!session) return;
    session.app.stop(); session.app.ticker.remove(session.tick);
    session.app.view?.removeEventListener('webglcontextlost', session.onContextLost);
    session.model?.internalModel?.off?.('beforeModelUpdate', session.beforeUpdate);
    if (session.model) { session.app.stage.removeChild?.(session.model); releaseModel(session.model); }
    const extension = session.app.renderer?.gl?.getExtension?.('WEBGL_lose_context');
    try { session.app.destroy(true, { children: true, texture: true, baseTexture: true }); } catch {}
    try { extension?.loseContext(); } catch {}
    this.host?.replaceChildren();
  }
  destroy() {
    if (this.destroyed) return;
    this.destroyed = true; ++this.generation; this.pending?.abort(); this.pending = null; this._dispose();
    this.stage?.removeEventListener('pointermove', this.onPointerMove); this.stage?.removeEventListener('pointerleave', this.onPointerLeave);
    this.document?.removeEventListener('visibilitychange', this.onVisibility);
    this.window?.removeEventListener('pagehide', this.onPageHide); this.window?.removeEventListener('pageshow', this.onPageShow); this.window?.removeEventListener('resize', this.onResize);
    this.retryButton?.removeEventListener('click', this.onRetry); this.observer?.disconnect(); this._notify('off');
    this.motionQuery?.removeEventListener?.('change', this.onMotionPreference);
  }
}
