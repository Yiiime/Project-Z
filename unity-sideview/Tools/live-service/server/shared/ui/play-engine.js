import {LiveEngine, CARDS} from './live-engine.js';

const SAVE_KEY = 'z-play-session-v1';
const POSITIONS = ['当前处境', '值得留意的因素', '可尝试的行动'];
const id = () => globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random()}`;
const copy = value => JSON.parse(JSON.stringify(value));
const text = value => String(value || '').trim().slice(0, 120);

// Local demo adapter. Hidden cards never enter getState(), the DOM or AI input.
// A real multi-user service must own this deck, identity and persistence.
export class PlayEngine extends LiveEngine {
  #deck = [];
  #picks = [];
  constructor({storage} = {}) {
    super();
    try { if (storage === undefined) storage = globalThis.sessionStorage; } catch { storage = null; }
    this.storage = storage;
    this._state.play = {active: null, queue: [], tarot: null, drawing: null, history: []};
    this._restored = false;
    try {
      const saved = JSON.parse(storage?.getItem(SAVE_KEY) || 'null');
      if (saved?.version === 1 && saved.play && Array.isArray(saved.deck) && Array.isArray(saved.picks)) {
        this._state.play = saved.play;
        this.#deck = saved.deck;
        this.#picks = saved.picks;
        this._restored = true;
      }
    } catch { /* A missing or unavailable session store leaves a fresh demo. */ }
  }
  exportDeckState(){return {deck:[...this.#deck],picks:[...this.#picks]};}
  restoreDeckState(value){this.#deck=[...value.deck];this.#picks=[...value.picks];}
  _emitState() {
    if (this._state.play && this.storage) {
      try { this.storage.setItem(SAVE_KEY, JSON.stringify({version: 1, play: this._state.play, deck: this.#deck, picks: this.#picks})); } catch {}
    }
    super._emitState();
  }
  start() {
    if (this._started || this._destroyed) return this.getState();
    this._started = true;
    this._lastTick = performance.now();
    this._timer = setInterval(() => this._tick(), 160);
    this._message('互动演示：塔罗由提问者选牌；绘画使用可修改的预制笔画回放。');
    if (this._restored) {
      this._setStage('free');
      this._say('回来了，刚才的牌和画都还在，我们接着来。', 'warm', 'nod');
      this._resumePlay();
    } else {
      this._say('来啦，晚上好！我是 Z。今晚可以陪我翻牌，也可以提一个自己的问题，或点一幅简笔画。', 'happy', 'wave');
      this._say('先让我试一次。我想看看，今晚怎样把陪伴你们这件事做好。塔罗就当作一个思考角度。', 'warm', 'explain', {
        onComplete: () => this.requestTarot({origin: 'host', spread: 'single'}),
      });
    }
    this._emitState();
    return this.getState();
  }
  _seedChat() {
    if (!this._state.play?.active) return super._seedChat();
    // No invented reaction to a future card or unfinished illustration.
    const p = this._state.play, task = p[p.active];
    const key = `${task.id}:${task.phase}:${task.round ?? task.stage}`;
    if (this._lastPlayReaction === key) return;
    this._lastPlayReaction = key;
    const lines = ['在这里陪着，慢慢来。', '我也在旁边看看。', '今晚的小屋好热闹。'];
    this._message(lines[this._seedCount++ % lines.length], {name: '晚风来信', type: 'chat'});
  }
  _enterFortune() { return this.requestTarot({origin: 'host', spread: 'single'}); }
  _valid(kind, taskId, revision) {
    const p = this._state.play, task = p[kind];
    return !this._destroyed && p.active === kind && task?.id === taskId
      && (revision === undefined || task.revision === revision);
  }
  _later(kind, taskId, delay, run, revision) {
    this._schedule(delay, () => { if (this._valid(kind, taskId, revision)) run(); }, 'play');
  }
  _line(kind, taskId, line, done, revision) {
    if (!this._valid(kind, taskId, revision)) return;
    this._say(line, 'warm', 'explain', {source: 'local-play', onComplete: () => {
      if (this._valid(kind, taskId, revision)) done?.();
    }});
  }
  _request(request) {
    if (this._destroyed || this._state.paused) return false;
    const p = this._state.play;
    if (this._pendingPlay && JSON.stringify(this._pendingPlay) === JSON.stringify(request)) return true;
    if (p.active || this._state.lottery?.open || this._pendingPlay) {
      if (p.queue.some(item => JSON.stringify(item) === JSON.stringify(request))) return true;
      if (p.queue.length >= 3) { this._message('已经排了三个玩法，先等前面的完成吧。'); return false; }
      p.queue.push(request);
      this._message('已排队，当前互动结束后开始。');
      return true;
    }
    if (this._active) {
      this._pendingPlay = request;
      this._deferAction('说完这段后开始新玩法', () => {
        this._pendingPlay = null; this._request(request);
      });
      return true;
    }
    this._setStage('free');
    if (request.kind === 'lottery') return super.openLottery() || super.triggerStory();
    for (const kind of ['tarot', 'drawing']) if (p[kind]) p[kind].open = false;
    this._state.currentCard = null;
    this._state.currentViewer = null;
    p.active = request.kind;
    if (request.kind === 'tarot') this._startTarot(request);
    else this._startDrawing(request);
    return true;
  }
  requestTarot({origin = 'viewer', question = '', spread = 'single'} = {}) {
    if (!['host', 'viewer'].includes(origin) || !['single', 'three'].includes(spread)) return false;
    const q = origin === 'host' ? '今晚怎样把陪伴你们这件事做好？' : text(question);
    if (!q) return false;
    return this._request({kind: 'tarot', origin, question: q, spread});
  }
  _startTarot(request) {
    const p = this._state.play;
    if (p.tarot) p.history = [...p.history, copy(p.tarot)].slice(-8);
    const t = p.tarot = {...request, id: id(), owner: request.origin === 'host' ? 'Z' : '小岛来客',
      phase: 'preparing', round: 0, slots: [], candidates: [], cards: [], activeIndex: -1,
      readCount: 0, open: true, paused: false, touched: this._clock};
    this.#deck = CARDS.map((_, i) => i);
    for (let i = this.#deck.length - 1; i > 0; i--) {
      const n = Math.floor(Math.random() * (i + 1));
      [this.#deck[i], this.#deck[n]] = [this.#deck[n], this.#deck[i]];
    }
    this.#picks = [];
    this._state.stageLabel = `塔罗 · ${t.owner}的问题`;
    const rule = t.spread === 'three' ? '分三次，每次三选一，选齐再一起翻开。' : '三个牌背选一张，选定后就翻开。';
    this._line('tarot', t.id, `${t.owner === 'Z' ? '这次是我的问题' : '这轮看你的问题'}：「${t.question}」。${rule}我们只把牌当作一个思考角度。`, () => this._prepareCandidates());
    this._emitState();
  }
  _prepareCandidates() {
    const t = this._state.play.tarot;
    t.phase = 'candidates_ready'; t.round++; t.selected = null; t.reminded = false;
    t.candidates = [0, 1, 2].map((_, index) => ({id: id(), label: ['左', '中', '右'][index]}));
    this._emitState();
  }
  candidatesDisplayed(taskId, round) {
    const t = this._state.play.tarot;
    if (!this._valid('tarot', taskId) || t.round !== round || t.phase !== 'candidates_ready' || !t.open || t.paused || this._state.paused) return false;
    t.phase = 'awaiting_selection';
    this._emitState();
    if (t.origin === 'host') {
      const index = Math.floor(Math.random() * 3), candidate = t.candidates[index];
      this._line('tarot', t.id, `这一张，我选${index === 1 ? '中间' : candidate.label + '边'}。`, () => this._chooseTarot(taskId, round, candidate.id, 'host'));
    } else {
      this._line('tarot', t.id, '轮到你啦，从这三个牌背里点一张。');
      this._later('tarot', t.id, 20000, () => {
        if (t.phase === 'awaiting_selection' && t.round === round && !t.paused) {
          t.reminded = true;
          this._line('tarot', t.id, '不着急，选好再点。想歇一会儿，也可以先暂停这一轮。');
          this._emitState();
        }
      });
    }
    return true;
  }
  chooseTarot(taskId, round, candidateId) { return this._chooseTarot(taskId, round, candidateId, 'viewer'); }
  _chooseTarot(taskId, round, candidateId, actor) {
    const t = this._state.play.tarot;
    if (!this._valid('tarot', taskId) || this._state.paused || t.paused || t.phase !== 'awaiting_selection'
      || t.origin !== actor || t.round !== round) return false;
    const index = t.candidates.findIndex(c => c.id === candidateId);
    if (index < 0) return false;
    const candidates = this.#deck.splice(0, 3), chosen = candidates.splice(index, 1)[0];
    if (chosen === undefined) return false;
    this.#deck.push(...candidates); this.#picks.push(chosen);
    t.selected = candidateId;
    t.slots.push({label: t.spread === 'single' ? '本次关注的角度' : POSITIONS[t.slots.length], choice: t.candidates[index].label});
    t.phase = 'selection_locked';
    this._emitState();
    this._later('tarot', t.id, 650, () => this._afterSelection());
    return true;
  }
  _afterSelection() {
    const t = this._state.play.tarot;
    if (t.paused) return;
    if (t.slots.length < (t.spread === 'three' ? 3 : 1)) this._prepareCandidates();
    else {
      t.phase = 'revealing';
      t.cards = this.#picks.map((index, i) => ({...CARDS[index], position: t.slots[i].label, orientation: '正位'}));
      t.candidates = [];
      this._emitState();
    }
  }
  tarotDisplayed(taskId) {
    const t = this._state.play.tarot;
    if (!this._valid('tarot', taskId) || t.phase !== 'revealing' || !t.open || this._state.paused || t.paused) return false;
    t.phase = 'interpreting';
    this._state.currentCard = t.cards[0];
    this._state.currentViewer = {question: t.question};
    this._readTarot();
    return true;
  }
  _readTarot() {
    const t = this._state.play.tarot, i = t.readCount;
    if (i >= t.cards.length) {
      t.activeIndex = -1;
      this._line('tarot', t.id, t.origin === 'host'
        ? '我先把这些角度记下来，一次认真做好一件事。你们想先一起做点什么？'
        : '这些牌提供的是几个思考角度。哪一点和你的处境有关？也可以告诉我哪里不贴合。', () => {
          t.phase = 'completed'; this._autoClose('tarot', t); this._emitState();
        });
    } else {
      const card = t.cards[i]; t.activeIndex = i;
      this._state.currentCard = card;
      this._line('tarot', t.id, `${card.position}，是${card.title}正位。${card.meaning}${t.origin === 'host' ? '放到今晚的陪伴里，我可以试着' : '结合你刚才的问题，可以考虑这个小行动：'}${card.advice}`, () => {
        t.readCount = i + 1; this._readTarot();
      });
    }
    this._emitState();
  }
  pauseTarot() {
    const t = this._state.play.tarot;
    if (this._state.play.active !== 'tarot' || t.origin !== 'viewer' || !['awaiting_selection', 'candidates_ready'].includes(t.phase)) return false;
    t.paused = !t.paused; this._emitState(); return true;
  }
  requestDrawing({subject = 'cat', accessory = 'scarf'} = {}) {
    if (!['cat', 'rabbit'].includes(subject) || !['scarf', 'bow'].includes(accessory)) return false;
    return this._request({kind: 'drawing', subject, accessory});
  }
  _startDrawing(request) {
    const p = this._state.play;
    if (p.drawing) p.drawingHistory = [...(p.drawingHistory || []), copy(p.drawing)].slice(-8);
    const d = this._state.play.drawing = {...request, id: id(), revision: 0, stage: -1,
      phase: 'preparing', title: `${request.accessory === 'scarf' ? '戴围巾' : '戴蝴蝶结'}的${request.subject === 'cat' ? '小猫' : '小兔'}`,
      open: false, manuallyHidden: false, displayed: null, svg: '', owner: '小岛来客', touched: this._clock};
    this._state.stageLabel = '绘画 · Z 的小画室';
    this._line('drawing', d.id, `给你画一只${d.title}。先画轮廓，再补五官和装饰。这里先用准备好的笔画演示绘制过程。`, () => this._nextDrawing(d.id, 0), 0);
    this._emitState();
  }
  _nextDrawing(taskId, revision) {
    if (!this._valid('drawing', taskId, revision)) return;
    const d = this._state.play.drawing;
    d.stage++;
    d.svg = drawingSvg(d.subject, d.accessory, d.stage);
    d.phase = d.stage === 2 ? 'ready' : 'drawing';
    if (!d.manuallyHidden) d.open = true;
    this._emitState();
    if (d.stage < 2) this._later('drawing', d.id, 6500, () => this._nextDrawing(taskId, revision), revision);
  }
  drawingDisplayed(taskId, revision, stage) {
    const d = this._state.play.drawing;
    if (!this._valid('drawing', taskId, revision) || this._state.paused || !d.open || d.stage !== stage || stage < 0 || d.displayed === `${revision}:${stage}`) return false;
    d.displayed = `${revision}:${stage}`;
    if (stage === 2) {
      d.phase = 'completed';
      this._line('drawing', d.id, `${d.accessory === 'scarf' ? '围巾' : '蝴蝶结'}已经画上去了。${d.subject === 'cat' ? '小猫' : '小兔'}画好了，你想给它起个什么名字？`, () => {
        this._autoClose('drawing', d); this._emitState();
      }, revision);
    } else if (!this._active && !this._queue.length) {
      this._line('drawing', d.id, stage === 0 ? '轮廓先放出来了，接着补五官。' : '五官和装饰都有了，再整理一点细节。', null, revision);
    }
    this._emitState(); return true;
  }
  editDrawing(accessory) {
    const p = this._state.play, d = p.drawing;
    if (!d || this._destroyed || this._state.paused || !['scarf', 'bow'].includes(accessory) || d.accessory === accessory) return false;
    if (p.active && p.active !== 'drawing' || this._state.lottery?.open) return false;
    if (this._deferAction('说完这段后修改画作', () => this.editDrawing(accessory))) return true;
    this._setStage('free'); p.active = 'drawing';
    this._state.stageLabel = '绘画 · Z 的小画室';
    if (p.tarot) p.tarot.open = false;
    d.versions = [...(d.versions || []), {revision: d.revision, title: d.title, svg: d.svg}].slice(-8);
    d.revision++; d.accessory = accessory; d.phase = 'editing'; d.displayed = null;
    d.title = `${accessory === 'scarf' ? '戴围巾' : '戴蝴蝶结'}的${d.subject === 'cat' ? '小猫' : '小兔'}`;
    d.open = true; d.manuallyHidden = false; d.touched = this._clock;
    const revision = d.revision;
    this._line('drawing', d.id, `好，把装饰改成${accessory === 'scarf' ? '围巾' : '蝴蝶结'}，主体和构图保留。`, () => {
      d.stage = 1; this._nextDrawing(d.id, revision);
    }, revision);
    this._emitState(); return true;
  }
  setPanel(kind, open) {
    const p = this._state.play, task = p[kind];
    if (!task || this._state.lottery?.open || p.active && p.active !== kind) return false;
    if (open) for (const other of ['tarot', 'drawing']) if (p[other]) p[other].open = false;
    task.open = Boolean(open); task.touched = this._clock;
    if (kind === 'drawing') task.manuallyHidden = !open;
    this._emitState(); return true;
  }
  _autoClose(kind, task) {
    const touched = task.touched = this._clock;
    this._later(kind, task.id, 15000, () => {
      if (task.phase !== 'completed') return;
      if (task.touched !== touched || this._active || this._hasPendingPlayerChat()) return this._autoClose(kind, task);
      this.finishActivity();
    });
  }
  finishActivity(cancel = false) {
    const p = this._state.play, kind = p.active, task = p[kind];
    if (!task || this._state.paused) return false;
    if (!cancel && task.phase !== 'completed') return false;
    if (this._deferAction(cancel ? '说完这段后结束本轮' : '说完这段后收好作品', () => this.finishActivity(cancel))) return true;
    if (cancel) task.phase = 'cancelled';
    task.open = false; p.active = null;
    this._jobs = this._jobs.filter(job => job.kind !== 'play');
    this._setStage('free');
    this._emitState();
    this._drainPlayQueue(); return true;
  }
  _drainPlayQueue() {
    const p = this._state.play;
    if (!p.active && !this._state.lottery?.open && p.queue.length) this._request(p.queue.shift());
  }
  removeQueued(index) {
    if (!Number.isInteger(index) || index < 0 || index >= this._state.play.queue.length) return false;
    this._state.play.queue.splice(index, 1); this._emitState(); return true;
  }
  triggerStory() { return this._request({kind: 'lottery'}); }
  openLottery() { return this._request({kind: 'lottery'}); }
  closeLottery() { const result = super.closeLottery(); this._drainPlayQueue(); return result; }
  advance() {
    if (this._state.play.active) {
      const task = this._state.play[this._state.play.active];
      return task.phase === 'completed' ? this.finishActivity() : false;
    }
    return this._state.stage === 'intro' ? this.requestTarot({origin: 'host'}) : false;
  }
  _resumePlay() {
    const p = this._state.play, t = p.tarot, d = p.drawing;
    if (p.active === 'tarot') {
      this._state.stageLabel = `塔罗 · ${t.owner}的问题`;
      if (t.phase === 'preparing') this._prepareCandidates();
      else if (['awaiting_selection', 'candidates_ready'].includes(t.phase)) { t.phase = 'candidates_ready'; this._emitState(); }
      else if (t.phase === 'selection_locked') this._afterSelection();
      else if (t.phase === 'interpreting') { t.phase = 'revealing'; this._emitState(); }
      else if (t.phase === 'completed') this._autoClose('tarot', t);
    } else if (p.active === 'drawing') {
      this._state.stageLabel = '绘画 · Z 的小画室';
      if (d.phase === 'editing') { d.stage = 1; this._nextDrawing(d.id, d.revision); }
      else if (d.stage < 2) this._later('drawing', d.id, 1500, () => this._nextDrawing(d.id, d.revision), d.revision);
      else if (d.phase === 'completed') this._autoClose('drawing', d);
    } else this._drainPlayQueue();
    this._emitState();
  }
  sendMessage(value) {
    const input = text(value), p = this._state.play;
    const tarotMatch = input.match(/^(?:塔罗|占卜)[：:]\s*(.+)$/);
    const choice = input.match(/^我选\s*([ABCabc左右中])$/);
    const drawMatch = input.match(/^(?:画|画一只)(?:戴(围巾|蝴蝶结)的)?小?(猫|兔)(?:子)?[。！!]?$/);
    const editMatch = input.match(/^(?:把)?(?:围巾|蝴蝶结|装饰)?(?:换|改)成(围巾|蝴蝶结)[。！!]?$/);
    const task = p[p.active];
    const feedback = task && (p.active === 'tarot' && ['interpreting', 'completed'].includes(task.phase)
      || p.active === 'drawing' && task.phase === 'completed');
    const unsupportedDrawing = /^(?:请|帮我|能不能|Z[，,]?\s*)*(?:画|绘画)/i.test(input);
    if (!tarotMatch && !choice && !drawMatch && !editMatch && !feedback && !unsupportedDrawing) return super.sendMessage(value);
    if (!input || this._destroyed || this._state.paused) return false;
    const message = this._message(input, {name: '小岛来客', type: 'chat', mine: true, replyStatus: 'seen'});
    if (tarotMatch) return this.requestTarot({question: tarotMatch[1]});
    if (choice) {
      const t = p.tarot, index = {A: 0, B: 1, C: 2, 左: 0, 中: 1, 右: 2}[choice[1].toUpperCase()];
      if (!t || !this.chooseTarot(t.id, t.round, t.candidates[index]?.id)) this._message('当前没有轮到你选牌，或这次选择已经锁定。');
      return true;
    }
    if (drawMatch) return this.requestDrawing({subject: drawMatch[2] === '猫' ? 'cat' : 'rabbit', accessory: drawMatch[1] === '蝴蝶结' ? 'bow' : 'scarf'});
    if (editMatch) {
      if (!this.editDrawing(editMatch[1] === '围巾' ? 'scarf' : 'bow')) this._message('当前暂不能应用这个修改，请先完成正在进行的互动。');
      return true;
    }
    if (unsupportedDrawing) {
      this._scheduleReply({text: '这一版先支持小猫、小兔，搭配围巾或蝴蝶结。点「点一幅画」选一个演示主题吧，其他主题等绘画能力接好再画。', mood: 'warm', gesture: 'nod', replyTo: message.id, replyMessage: message});
      return true;
    }
    task.touched = this._clock;
    let response;
    if (p.active === 'tarot') {
      task.feedback = [...(task.feedback || []), input].slice(-6);
      response = task.origin === 'host' ? '你的想法我听到了，会把它当作你给我的建议。牌面先保留，我再结合今晚实际发生的事想一想。' : /不像|不准|不贴|不对/.test(input) ? '收到，不贴合就不用硬套。牌先保留，你觉得哪一点和实际情况不同？'
        : `补充我收到了，牌面不变。我们先把「${input}」当作你提供的背景，结合实际情况继续聊。`;
    } else response = /叫|名字|取名/.test(input) ? `好，这幅画的名字建议我记下了：「${input}」。` : '收到你的反馈啦，画先留着。想改装饰的话，可以在画板里换围巾或蝴蝶结。';
    this._scheduleReply({text: response, mood: 'warm', gesture: 'nod', replyTo: message.id, replyMessage: message});
    this._emitState(); return true;
  }
  replay() {
    this._restored = false; this._pendingPlay = null; this.#deck = []; this.#picks = [];
    return super.replay();
  }
  _initialState(saved) {
    return {...super._initialState(saved), play: {active: null, queue: [], tarot: null, drawing: null, history: []}};
  }
}

// Stable SVG object IDs keep all unedited geometry identical across revisions.
export function drawingSvg(subject, accessory, stage) {
  const ears = subject === 'rabbit'
    ? '<path id="ears" d="M131 113C88 34 119 17 149 104M170 104C184 16 218 36 191 116"/>'
    : '<path id="ears" d="M109 132L103 79L146 109M176 109L217 79L211 135"/>';
  const outline = `${ears}<path id="body" d="M124 190C100 216 99 248 113 263H207C220 246 217 216 194 190"/><path id="head" d="M111 126C127 95 194 97 210 129C237 178 206 199 161 200C114 200 86 175 111 126Z"/>`;
  const features = '<g id="face"><path d="M131 150v9M190 150v9M155 168l6 5 6-5M161 173v7M161 180q-9 9-17 0M161 180q9 9 17 0M103 164l19 3M199 167l20-3"/></g>';
  const decoration = accessory === 'bow'
    ? '<g id="accessory"><path d="M157 207Q119 176 124 216Q129 230 157 214M165 207Q201 176 198 216Q191 230 165 214"/><circle cx="161" cy="210" r="7"/></g>'
    : '<g id="accessory"><path d="M123 192Q161 207 198 192L200 211Q160 225 121 211ZM175 217l-6 35 20 3 6-42M171 243l18 3"/></g>';
  const finish = '<g id="finishing"><path d="M135 239v23M186 239v23M115 251h15M193 251h14M124 126l8-6M190 120l8 6"/><path d="M124 176h9M189 176h9" stroke="#bf8791"/></g>';
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 320 300" role="img" aria-label="${subject === 'cat' ? '小猫' : '小兔'}简笔画${['轮廓', '特征', '完成'][stage] || ''}"><g fill="#fffdf7" stroke="#665d75" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round">${outline}${stage >= 1 ? features + decoration : ''}${stage >= 2 ? finish : ''}</g></svg>`;
}
