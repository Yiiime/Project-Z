const $ = selector => document.querySelector(selector);
const state = {session: '', data: null, selected: [], startWarnings: [], startPrompt: '', confirmed: new Set(), shipConfirmed: new Set()};
const STATUS_TEXT = {free: '空闲', active: '开发中', paused: '可能暂停', stale: '已搁置·可接手'};
const LEVEL_TEXT = {yellow: '普通文件重叠', red: '难合并文件重叠'};

function el(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs || {})) {
    if (value === false || value === null || value === undefined) continue;
    if (key === 'class') node.className = value;
    else if (key === 'text') node.textContent = value;
    else if (key.startsWith('on')) node.addEventListener(key.slice(2), value);
    else if (value === true) node.setAttribute(key, '');
    else node.setAttribute(key, value);
  }
  for (const child of children.flat()) {
    if (child === null || child === undefined || child === false) continue;
    node.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
  return node;
}

function toast(message, error = false) {
  const item = el('div', {class: error ? 'error' : ''}, message);
  $('#toast').append(item);
  setTimeout(() => item.remove(), error ? 9000 : 4500);
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: {'Content-Type': 'application/json', 'X-Z-Session': state.session, ...(options.headers || {})},
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
  });
  const data = await response.json().catch(() => ({error: '面板返回了无法识别的内容。'}));
  if (!response.ok) {
    const error = new Error(data.error || '操作失败');
    error.details = data.details;
    throw error;
  }
  return data;
}

function relative(iso) {
  if (!iso) return '—';
  const minutes = Math.max(0, Math.round((Date.now() - Date.parse(iso)) / 60000));
  if (minutes < 1) return '刚刚';
  if (minutes < 60) return `${minutes} 分钟前`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} 小时前`;
  return `${Math.round(hours / 24)} 天前`;
}

const dateText = iso => iso ? new Date(iso).toLocaleString('zh-CN', {month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false}) : '—';
const personName = task => task.ownerName && task.ownerName !== task.owner ? `${task.ownerName}（@${task.owner}）` : `@${task.owner}`;
const areaById = id => state.data.config.areas.find(area => area.id === id);

async function copy(text, label = '提示词') {
  try {
    await navigator.clipboard.writeText(text);
    toast(`已复制${label}，粘贴给 Codex 即可。`);
  } catch {
    showDialog(`复制${label}`, [el('p', {}, '浏览器不允许自动复制，请全选下面的文字后复制。'), el('textarea', {rows: 10, readonly: true}, text)], [el('button', {value: 'close', class: 'primary'}, '关闭')]);
  }
}

function showDialog(title, content, actions) {
  $('#dialog-title').textContent = title;
  $('#dialog-content').replaceChildren(...content);
  $('#dialog-actions').replaceChildren(...actions);
  if (!$('#dialog').open) $('#dialog').showModal();
}

async function withButton(button, work) {
  const text = button.textContent;
  button.disabled = true;
  button.textContent = '处理中…';
  try { return await work(); }
  catch (error) { toast(error.message, true); throw error; }
  finally { button.disabled = false; button.textContent = text; }
}

async function loadChecks() {
  $('#checks-view').hidden = false;
  $('#board-view').hidden = true;
  $('#checks').replaceChildren(el('p', {class: 'empty'}, '正在检查这台电脑…'));
  const result = await api('/api/checks');
  $('#checks').replaceChildren(...result.items.map(item => {
    let action = null;
    if (!item.ok && item.action?.type === 'link') action = el('button', {type: 'button', onclick: () => api('/api/open', {method: 'POST', body: {url: item.action.url}}).catch(error => toast(error.message, true))}, item.action.label);
    if (!item.ok && item.action?.type === 'login') action = el('button', {type: 'button', class: 'primary', onclick: startLogin}, item.action.label);
    if (!item.ok && item.action?.type === 'identity') action = el('button', {type: 'button', class: 'primary', onclick: event => withButton(event.currentTarget, async () => { await api('/api/identity', {method: 'POST', body: {}}); await loadChecks(); })}, item.action.label);
    return el('div', {class: `check ${item.ok ? 'ok' : 'bad'} ${item.optional ? 'optional' : ''}`},
      el('span', {class: 'mark', 'aria-hidden': 'true'}, item.ok ? '✓' : item.optional ? '!' : '×'),
      el('div', {}, el('strong', {}, `${item.label}${item.optional ? '（建议）' : ''}`), el('p', {}, item.detail)),
      action);
  }));
  $('#enter-board').hidden = !result.ready;
  return result.ready;
}

async function startLogin() {
  const box = $('#login-box');
  box.hidden = false;
  box.replaceChildren(el('p', {}, '正在准备 GitHub 登录…'));
  await api('/api/login', {method: 'POST', body: {}});
  const poll = async () => {
    const login = await api('/api/login');
    if (login.code) {
      box.replaceChildren(
        el('p', {}, '一次性代码：'), el('p', {class: 'code'}, login.code),
        el('p', {}, '在浏览器打开 GitHub，输入这个代码，并确认授权。'),
        el('div', {class: 'actions'},
          el('button', {type: 'button', class: 'primary', onclick: () => api('/api/open', {method: 'POST', body: {url: login.url}})}, '打开 GitHub 登录页'),
          el('button', {type: 'button', onclick: () => copy(login.code, '一次性代码')}, '复制代码')));
    }
    if (login.done) {
      toast(login.ok ? 'GitHub 登录完成。' : login.output, !login.ok);
      box.hidden = login.ok;
      await loadChecks();
      return;
    }
    setTimeout(poll, 1200);
  };
  poll();
}

function areaStatus(area) {
  const tasks = state.data.tasks.filter(task => task.areas.includes(area.id));
  const visible = tasks.filter(task => task.status === 'active' || task.status === 'paused').sort((a, b) => Date.parse(b.lastActivity) - Date.parse(a.lastActivity));
  const stale = tasks.filter(task => task.status === 'stale');
  return {status: visible[0]?.status || 'free', task: visible[0] || null, stale, all: tasks};
}

function renderAreas() {
  $('#areas').replaceChildren(...state.data.config.areas.map(area => {
    const info = areaStatus(area);
    const selected = state.selected.includes(area.id);
    const stats = state.data.areaStats[area.id] || {hard: 0, hardTypes: []};
    const task = info.task;
    const meta = task
      ? [['负责人', personName(task)], ['开始', dateText(task.startedAt)], ['最近更新', relative(task.lastActivity)], ['PR', el('a', {href: task.url, target: '_blank', rel: 'noreferrer'}, `#${task.number}`)]]
      : [['负责人', '—'], ['开始', '—'], ['最近更新', '—'], ['PR', '—']];
    const actions = [
      el('button', {type: 'button', class: selected ? 'primary' : '', 'aria-pressed': String(selected), onclick: () => toggleArea(area.id)}, selected ? '已选择' : '选择这个区域'),
    ];
    if (info.stale.length) actions.push(el('button', {type: 'button', onclick: () => openTakeover(info.stale[0])}, `接手 #${info.stale[0].number}`));
    return el('article', {class: `area ${selected ? 'selected' : ''}`},
      el('div', {class: 'area-head'}, el('h3', {}, area.name), el('span', {class: `pill ${info.status}`}, STATUS_TEXT[info.status])),
      el('p', {class: 'area-desc'}, area.description),
      info.stale.length ? el('p', {class: 'area-desc'}, `#${info.stale[0].number} 已搁置：可以接手或重新开始。`) : null,
      el('dl', {class: 'area-meta'}, meta.map(([key, value]) => el('div', {}, el('dt', {}, key), el('dd', {}, value)))),
      stats.hard ? el('p', {class: 'hard'}, `难合并文件 ${stats.hard} 个（${stats.hardTypes.join('、')}）`) : el('p', {class: 'area-desc'}, '目前没有难合并文件'),
      el('details', {}, el('summary', {}, '查看对应路径'), el('p', {class: 'area-desc'}, area.paths.join('\n'))),
      el('div', {class: 'area-actions'}, actions));
  }));
}

function toggleArea(id) {
  if (state.selected.includes(id)) state.selected = state.selected.filter(value => value !== id);
  else if (state.selected.length >= 2) return toast('一个任务最多选择 2 个区域。', true);
  else state.selected.push(id);
  state.confirmed.clear();
  renderAreas();
  previewStart();
}

function warningNode(item, confirmedSet, onChange) {
  const name = item.ownerName && item.ownerName !== item.owner ? `${item.ownerName}（@${item.owner}）` : `@${item.owner}`;
  const statusText = item.status === 'paused' ? '可能暂停' : '正在改';
  const files = item.hardFiles?.length ? `难合并文件：${item.hardFiles.join('、')}` : item.files?.length ? `重叠文件：${item.files.join('、')}` : '';
  return el('div', {class: `warning ${item.level}`},
    el('p', {}, el('strong', {}, `${item.level === 'red' ? '🔴' : '🟡'} ${name} ${item.status ? statusText : LEVEL_TEXT[item.level]}，${item.lastActivity ? `${relative(item.lastActivity)}更新` : `PR #${item.number}`}`)),
    files ? el('p', {}, files) : null,
    item.level === 'red' ? el('label', {class: 'confirm'},
      el('input', {type: 'checkbox', checked: confirmedSet.has(item.owner), onchange: event => { event.target.checked ? confirmedSet.add(item.owner) : confirmedSet.delete(item.owner); onChange(); }}),
      `我已和 ${name} 确认`) : el('p', {}, '仍然可以继续，建议先打个招呼。'));
}

async function previewStart() {
  const chips = state.selected.map(id => el('span', {class: 'chip'}, areaById(id).name));
  $('#selected-areas').replaceChildren(chips.length ? el('span', {}, '已选择：') : el('span', {}, '先在上面选择 1～2 个区域。'), ...chips);
  state.startWarnings = [];
  state.startPrompt = '';
  if (state.selected.length) {
    const result = await api('/api/preview-start', {method: 'POST', body: {goal: $('#goal').value, areaIds: state.selected}}).catch(error => (toast(error.message, true), null));
    if (result) Object.assign(state, {startWarnings: result.warnings, startPrompt: result.prompt});
  }
  renderStartWarnings();
}

function renderStartWarnings() {
  $('#start-warnings').replaceChildren(...state.startWarnings.map(item => warningNode(item, state.confirmed, renderStartWarnings)));
  const missing = state.startWarnings.filter(item => item.level === 'red' && !state.confirmed.has(item.owner));
  const busyCurrent = state.data?.local.dirty.length;
  $('#start-button').disabled = !state.selected.length || !$('#goal').value.trim() || missing.length || busyCurrent;
  $('#copy-start-preview').disabled = !state.selected.length;
  $('#start-button').title = busyCurrent ? '当前有未保存修改，先保存进度。' : '';
}

function promptButtons(task, kinds) {
  const labels = {start: '复制开工提示词', save: '复制保存提示词', ship: '复制交付提示词', conflict: '复制修复冲突提示词', checks: '复制修复检查提示词'};
  return kinds.map(kind => el('button', {type: 'button', class: 'ghost', onclick: () => copy(state.data.prompts[task.number][kind])}, labels[kind]));
}

function renderCurrent() {
  const data = state.data;
  const task = data.current;
  const node = $('#current');
  if (!task) {
    node.replaceChildren(...[el('div', {class: 'section-head'}, el('div', {}, el('h2', {}, '我的当前任务'), el('p', {}, data.local.branch.startsWith('task/') ? `当前分支 ${data.local.branch} 没有打开的 PR。` : `当前在 ${data.local.branch}。选择区域并开工后，这里会显示任务按钮。`))),
      data.local.dirty.length ? el('div', {class: 'warning yellow'}, el('p', {}, `本机有 ${data.local.dirty.length} 个未保存修改。开新任务前先处理。`)) : null].filter(Boolean));
    return;
  }
  const areas = task.areas.map(id => areaById(id)?.name || id).join('、');
  const overlaps = data.overlaps.filter(item => item.pulls.includes(task.number));
  const failed = task.check?.state === 'FAILURE' || task.check?.state === 'ERROR';
  node.replaceChildren(el('div', {class: 'current-grid'},
    el('div', {},
      el('div', {class: 'section-head'}, el('div', {}, el('h2', {}, `我的当前任务 · #${task.number}`), el('p', {}, task.draft ? '草稿：可以分多次保存，不会自动合并。' : '已交付：检查通过后自动合并。')), el('span', {class: `pill ${task.status}`}, STATUS_TEXT[task.status])),
      el('p', {class: 'current-goal'}, task.goal),
      el('dl', {class: 'kv'},
        el('dt', {}, '区域'), el('dd', {}, areas),
        el('dt', {}, '分支'), el('dd', {}, task.branch),
        el('dt', {}, '本机修改'), el('dd', {}, data.local.dirty.length ? `${data.local.dirty.length} 个文件未保存` : '没有未保存修改'),
        el('dt', {}, '最近更新'), el('dd', {}, relative(task.lastActivity)),
        el('dt', {}, 'GitHub'), el('dd', {}, el('a', {href: task.url, target: '_blank', rel: 'noreferrer'}, task.url))),
      overlaps.map(item => warningNode({...item, owner: data.tasks.find(t => t.number === item.pulls.find(n => n !== task.number))?.owner, ownerName: data.tasks.find(t => t.number === item.pulls.find(n => n !== task.number))?.ownerName, number: item.pulls.find(n => n !== task.number)}, state.shipConfirmed, renderCurrent))),
    el('div', {},
      actionCard('交给 Codex 开发', '复制后粘贴到 Codex；它只会改所选区域。', [...promptButtons(task, ['start'])]),
      actionCard('保存进度', '提交并推送到任务分支，PR 保持草稿。暂停前一定要点。', [el('button', {type: 'button', class: 'primary', onclick: openSave}, '保存进度'), ...promptButtons(task, ['save'])]),
      actionCard('还在做', '只刷新最近活动时间，不需要提交代码。', [el('button', {type: 'button', onclick: event => withButton(event.currentTarget, async () => { await api('/api/still', {method: 'POST', body: {number: task.number}}); toast('已告诉团队：你还在做。'); await loadBoard(); })}, '还在做')]),
      actionCard('交付', '同步 main、运行检查、推送，并转为可合并。', [el('button', {type: 'button', class: 'primary', onclick: event => ship(event.currentTarget, task)}, '交付'), ...promptButtons(task, ['ship'])]),
      actionCard('遇到问题', '冲突或检查失败时，复制对应提示词。', promptButtons(task, failed ? ['checks', 'conflict'] : ['conflict', 'checks'])))));
}

function actionCard(title, text, buttons) {
  return el('div', {class: 'action-card'}, el('h3', {}, title), el('p', {}, text), el('div', {class: 'actions'}, buttons));
}

function renderTasks() {
  const data = state.data;
  const rows = data.tasks.map(task => {
    const areas = task.areas.map(id => areaById(id)?.name || id).join('、') || '未识别区域';
    const mine = task.owner === data.me.login;
    const actions = [el('a', {href: task.url, target: '_blank', rel: 'noreferrer'}, `打开 #${task.number}`)];
    if (mine && data.current?.number !== task.number) actions.push(el('button', {type: 'button', onclick: event => withButton(event.currentTarget, async () => { await api('/api/switch', {method: 'POST', body: {number: task.number}}); toast(`已切换到 #${task.number}`); await loadBoard(); })}, '继续这个任务'));
    if (!mine && task.status === 'stale') actions.push(el('button', {type: 'button', onclick: () => openTakeover(task)}, '接手'));
    return el('article', {class: 'task'},
      el('div', {}, el('div', {class: 'task-title'}, el('span', {class: `pill ${task.status}`}, STATUS_TEXT[task.status]), el('strong', {}, task.goal)),
        el('p', {}, `${personName(task)} · ${areas} · ${task.draft ? '草稿' : '已交付'} · ${relative(task.lastActivity)}更新`),
        task.handoff ? el('p', {}, `交接：${task.handoff}`) : null),
      el('div', {class: 'actions'}, actions));
  });
  $('#tasks').replaceChildren(el('div', {class: 'section-head'}, el('div', {}, el('h2', {}, '所有未合并任务'), el('p', {}, '合并或关闭后会自动从这里消失。'))),
    rows.length ? el('div', {class: 'task-list'}, rows) : el('p', {class: 'empty'}, '现在没有未合并任务。'));
}

function openSave() {
  const message = el('input', {placeholder: '这次做了什么，例如：加入潮水叶子的次日台词'});
  const handoff = el('textarea', {rows: 4, placeholder: '已完成：…\n未完成：…\n下一步：…'});
  const save = el('button', {type: 'button', class: 'primary', onclick: event => withButton(event.currentTarget, async () => {
    const result = await api('/api/save', {method: 'POST', body: {message: message.value, handoff: handoff.value}});
    $('#dialog').close();
    toast(result.committed ? '已保存并推送，PR 仍是草稿。' : '没有新修改；已确认远端是最新的。');
    await loadBoard();
  })}, '保存并推送');
  showDialog('保存进度', [el('label', {class: 'field'}, el('span', {}, '提交说明（可选）'), message), el('label', {class: 'field'}, el('span', {}, '交接说明（暂停时建议填写）'), handoff)], [el('button', {value: 'cancel'}, '取消'), save]);
}

async function ship(button, task) {
  if (state.data.local.dirty.length) {
    toast('还有未保存的修改。先保存进度，再交付。', true);
    openSave();
    return;
  }
  await withButton(button, async () => {
    const check = await api('/api/ship-check', {method: 'POST', body: {confirmed: [...state.shipConfirmed]}});
    if (!check.ok) {
      const warnings = (check.warnings || []).filter(item => item.level === 'red');
      showDialog('交付前先确认', [el('p', {}, check.message), ...warnings.map(item => warningNode(item, state.shipConfirmed, () => {}))],
        [el('button', {value: 'cancel'}, '取消'), el('button', {type: 'button', class: 'primary', onclick: () => { $('#dialog').close(); ship(button, task); }}, '已确认，继续交付')]);
      return;
    }
    const yellow = (check.warnings || []).filter(item => item.level === 'yellow');
    if (yellow.length) toast(`提醒：和 ${yellow.map(item => `#${item.number}`).join('、')} 改了同一普通文件。`);
    const job = await api('/api/ship', {method: 'POST', body: {confirmed: [...state.shipConfirmed]}});
    const output = el('div', {class: 'output'}, '开始交付…');
    showDialog('正在交付', [el('p', {}, '会同步最新 main、运行检查、推送，并把草稿转为可合并。'), output], [el('button', {value: 'close'}, '在后台继续')]);
    const poll = async () => {
      const latest = await api(`/api/job?id=${job.id}`);
      output.textContent = latest.output || '处理中…';
      output.scrollTop = output.scrollHeight;
      if (latest.running) return setTimeout(poll, 1500);
      toast(latest.ok ? '已交付。GitHub 检查通过后会自动合并。' : '交付没有完成，请看窗口里的原因，或复制修复提示词。', !latest.ok);
      await loadBoard();
    };
    poll();
  });
}

function openTakeover(task) {
  const note = el('textarea', {rows: 4, placeholder: '例如：我会先补完 Z 的次日回应，再处理动作。'});
  const continueRadio = el('input', {type: 'radio', name: 'mode', value: 'continue', checked: true});
  const freshRadio = el('input', {type: 'radio', name: 'mode', value: 'fresh'});
  showDialog(`接手 #${task.number}`, [
    el('dl', {class: 'kv'}, el('dt', {}, '原负责人'), el('dd', {}, personName(task)), el('dt', {}, '原分支'), el('dd', {}, task.branch), el('dt', {}, '最近更新'), el('dd', {}, relative(task.lastActivity))),
    el('div', {}, el('strong', {}, '交接说明'), el('p', {}, task.handoff || '原负责人没有写交接说明。')),
    el('div', {}, el('strong', {}, '最近修改'), task.recentCommits.length ? el('ul', {}, task.recentCommits.map(commit => el('li', {}, `${commit.message} · ${relative(commit.date)}`))) : el('p', {}, '没有提交记录。')),
    el('label', {class: 'option'}, continueRadio, el('span', {}, el('strong', {}, '在原分支继续'), el('br'), '保留原来的修改，并在 PR 中补充接手说明。')),
    el('label', {class: 'option'}, freshRadio, el('span', {}, el('strong', {}, '从最新 main 重新开始'), el('br'), '新开草稿 PR；原 PR 和分支都保留。')),
    el('label', {class: 'field'}, el('span', {}, '接手说明'), note),
  ], [el('button', {value: 'cancel'}, '取消'), el('button', {type: 'button', class: 'primary', onclick: event => withButton(event.currentTarget, async () => {
    const result = await api('/api/takeover', {method: 'POST', body: {number: task.number, mode: freshRadio.checked ? 'fresh' : 'continue', note: note.value}});
    $('#dialog').close();
    toast(`已接手，当前分支：${result.branch}`);
    await loadBoard();
  })}, '确认接手')]);
}

async function startTask(button) {
  await withButton(button, async () => {
    const result = await api('/api/start', {method: 'POST', body: {goal: $('#goal').value, areaIds: state.selected, slug: $('#slug').value, confirmed: [...state.confirmed]}});
    $('#goal').value = '';
    $('#slug').value = '';
    state.selected = [];
    state.confirmed.clear();
    toast(`已开工：草稿 PR #${result.number}`);
    await loadBoard();
    const prompts = state.data.prompts[result.number];
    if (prompts) showDialog('开工完成', [el('p', {}, '下一步：复制提示词，粘贴给 Codex。'), el('textarea', {rows: 10, readonly: true}, prompts.start)], [el('button', {type: 'button', class: 'primary', onclick: () => copy(prompts.start)}, '复制开工提示词'), el('button', {value: 'close'}, '关闭')]);
  }).catch(error => {
    if (error.details?.warnings) {
      state.startWarnings = error.details.warnings;
      renderStartWarnings();
    }
  });
}

async function loadBoard() {
  $('#checks-view').hidden = true;
  $('#board-view').hidden = false;
  if (!state.data) $('#areas').replaceChildren(el('p', {class: 'empty'}, '正在读取 GitHub 上的任务…'));
  try {
    state.data = await api('/api/board');
  } catch (error) {
    $('#areas').replaceChildren(el('div', {class: 'warning red'}, el('p', {}, `读取 GitHub 失败：${error.message}`), el('button', {type: 'button', onclick: event => withButton(event.currentTarget, loadBoard)}, '重试')));
    throw error;
  }
  $('#whoami').textContent = `${state.data.me.name} · ${state.data.repo}`;
  renderCurrent();
  renderAreas();
  renderTasks();
  await previewStart();
}

async function boot() {
  try {
    state.session = (await api('/api/session')).session;
    if (await loadChecks()) await loadBoard();
  } catch (error) {
    toast(error.message, true);
  }
}

$('#refresh').addEventListener('click', event => withButton(event.currentTarget, () => $('#board-view').hidden ? loadChecks() : loadBoard()));
$('#recheck').addEventListener('click', event => withButton(event.currentTarget, loadChecks));
$('#enter-board').addEventListener('click', event => withButton(event.currentTarget, loadBoard));
$('#goal').addEventListener('input', renderStartWarnings);
$('#goal').addEventListener('change', previewStart);
$('#start-button').addEventListener('click', event => startTask(event.currentTarget));
$('#copy-start-preview').addEventListener('click', () => copy(state.startPrompt));
boot();
