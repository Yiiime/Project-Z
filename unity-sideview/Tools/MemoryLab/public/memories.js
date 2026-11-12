const $ = id => document.getElementById(id);
const periods = ['上午', '午后', '傍晚', '夜晚'];
let data = null, actorId = 'linxu', worldId = '', selectedId = '', requestNumber = 0, controller;
let listSignature = '', detailSignature = '', actorSignature = '', worldSignature = '';
function element(tag, text, className) {
  const node = document.createElement(tag);
  if (text != null) node.textContent = text;
  if (className) node.className = className;
  return node;
}
function time(hour) {
  if (hour == null) return '日期未记载';
  const day = Math.floor(hour / 24) + 1, minutes = Math.round((hour % 24) * 60);
  return `第 ${day} 天 · ${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
}
function filtered() {
  const query = $('search').value.trim().toLocaleLowerCase(), kind = $('kind').value;
  return (data?.memories || []).filter(m => (!kind || m.kind === kind) &&
    (!query || [m.content, m.gist, m.sourceLabel, m.subject].join(' ').toLocaleLowerCase().includes(query)));
}
function render() {
  const ws = JSON.stringify(data.worlds);
  if (ws !== worldSignature) {
    worldSignature = ws;
    const options = data.worlds.map((w, index) => {
      const date = new Date(w.loadedAt).toLocaleString('zh-CN', { hour12: false });
      const option = element('option', `${index === 0 ? '最近载入 · ' : ''}第 ${w.day} 天 ${periods[w.period] || ''} · ${date} · ${w.worldId.slice(0, 6)}`);
      option.value = w.worldId; return option;
    });
    if (!options.length) { const option = element('option', '等待游戏连接'); option.value = ''; options.push(option); }
    $('world').replaceChildren(...options);
  }
  $('world').value = data.world?.worldId || '';
  const as = JSON.stringify([data.actors, actorId]);
  if (as !== actorSignature) {
    actorSignature = as;
    $('actors').replaceChildren(...data.actors.map(a => {
      const button = element('button', null, 'actor'); button.type = 'button';
      button.setAttribute('aria-pressed', String(a.id === actorId));
      button.append(element('strong', a.name), element('small', a.role), element('span', `${a.count} 条有效记忆`, 'number'));
      button.onclick = () => { actorId = a.id; selectedId = ''; clearDetail(); load(); };
      return button;
    }));
  }
  const actor = data.actors.find(a => a.id === actorId);
  $('actor-name').textContent = `${actor?.name || ''}记得的事`;
  const memories = filtered();
  $('total').textContent = `${memories.length} / ${data.memories.length} 条`;
  $('pending').hidden = !data.pending;
  $('pending').textContent = `${data.pending} 次交流的原话已保存，记忆整理尚未完成。游戏后续会重试。`;
  if (!memories.some(m => m.id === selectedId)) selectedId = memories[0]?.id || '';
  const ls = JSON.stringify([memories, selectedId]);
  if (ls !== listSignature) {
    listSignature = ls;
    $('memory-list').replaceChildren(...memories.map(m => {
      const button = element('button', null, 'memory'); button.type = 'button';
      button.setAttribute('aria-pressed', String(m.id === selectedId));
      const head = element('div', null, 'memory-head');
      head.append(element('span', m.kindLabel, 'badge'), element('span', time(m.originalHour)));
      button.append(head, element('p', m.gist || m.content), element('small', `${m.state.label} · ${m.sourceLabel}`));
      button.onclick = () => { selectedId = m.id; render(); }; return button;
    }));
    if (!memories.length) $('memory-list').append(element('p', !data.world ? '进入七人试玩后，这里会出现游戏进度。' : data.memories.length ? '没有匹配的记忆，试试其他关键词。' : '这位角色目前没有记忆记录。', 'empty'));
  }
  const memory = memories.find(m => m.id === selectedId);
  const ds = JSON.stringify([data.world?.worldId, data.world?.epoch, actorId, memory]);
  if (ds === detailSignature) return;
  detailSignature = ds;
  const panel = $('detail'); panel.replaceChildren();
  if (!memory) { panel.append(element('div', '选择一条记忆，查看原文与来源。', 'empty')); return; }
  const top = element('div', null, 'detail-top');
  top.append(element('span', memory.kindLabel, 'badge'), element('span', time(memory.originalHour)));
  panel.append(top, element('h2', memory.subject || memory.gist || '记忆原文'), element('div', memory.certainty, 'certainty'), element('p', memory.content, 'original'));
  const meters = element('div', null, 'meters');
  for (const [title, value, note] of [['容易想起', memory.availability, `实际使用 ${memory.recallCount} 次`], ['细节清晰', memory.clarity, `细节被使用 ${memory.detailRecallCount} 次`]]) {
    const meter = element('div', null, 'meter'), label = element('div', null, 'meter-label');
    label.append(element('span', title), element('strong', `${value}%`));
    // A native meter avoids inline styles under the page's strict CSP.
    const bar = document.createElement('meter'); bar.min = 0; bar.max = 100; bar.value = value; bar.setAttribute('aria-label', title);
    meter.append(label, bar, element('small', note)); meters.append(meter);
  }
  panel.append(meters, element('p', `${memory.state.label}：${memory.state.reason}`, 'state-note'));
  if (memory.commitmentStatus) panel.append(element('p', `约定状态：${({ pending: '待兑现', fulfilled: '游戏已确认兑现', cancelled: '已取消' })[memory.commitmentStatus] || memory.commitmentStatus}`, 'state-note'));
  panel.append(element('h3', '当前可回忆的内容'), element('div', memory.recallView.content, 'recall'), element('p', memory.recallView.reason + '。这里只展示记忆的可用程度，不触发一次真实回忆。', 'muted'));
  panel.append(element('h3', '来源与原话'), element('p', memory.sourceLabel, 'source'));
  for (const evidence of memory.evidence) panel.append(element('p', `${evidence.speaker} · ${time(evidence.hour)}`, 'source'), element('blockquote', evidence.quote, 'evidence'));
  if (!memory.evidence.length) panel.append(element('p', memory.origin === 'authored_backstory' ? '来自角色档案中的既往经历，没有本局发言原文。' : '没有可关联的在场原话；可从下方依据或来源说明核对。', 'muted'));
  if (memory.dependencies.length) {
    panel.append(element('h3', '这条看法基于哪些记忆'));
    memory.dependencies.forEach(d => panel.append(element('div', `${d.counterexample ? '反例' : '依据'}：${d.gist}`, 'dependency')));
  }
  if (memory.supersededBy) panel.append(element('p', '这条记录已被后续记忆更新，保留在这里供核对。', 'muted'));
}
function clearDetail() {
  data = null; listSignature = detailSignature = '';
  $('memory-list').replaceChildren(element('p', '正在读取这位角色的记忆…', 'empty'));
  $('detail').replaceChildren(element('p', '正在读取…', 'empty'));
}
async function load() {
  const sequence = ++requestNumber; controller?.abort(); controller = new AbortController();
  const currentController = controller, timeout = setTimeout(() => currentController.abort(), 7000);
  $('refresh').disabled = true;
  try {
    const query = new URLSearchParams({ actorId }); if (worldId) query.set('worldId', worldId);
    const response = await fetch('/api/debug/memories?' + query, { headers: { 'X-Z-Memory': '1' }, signal: controller.signal, cache: 'no-store' });
    const result = await response.json(); if (sequence !== requestNumber) return;
    if (!response.ok) throw new Error(result.error || '读取失败');
    data = result; render();
    $('status').textContent = '已更新 · ' + new Date().toLocaleTimeString('zh-CN', { hour12: false }); $('status').className = '';
  } catch (error) {
    if (sequence !== requestNumber) return;
    $('status').textContent = error.name === 'AbortError' ? '读取超时，稍后重试' : '服务未连接或读取失败'; $('status').className = 'error';
  } finally { clearTimeout(timeout); if (sequence === requestNumber) $('refresh').disabled = false; }
}
$('world').onchange = () => { worldId = $('world').value; selectedId = ''; clearDetail(); load(); };
$('search').oninput = $('kind').onchange = () => { if (data) render(); };
$('refresh').onclick = load;
setInterval(() => { if ($('auto').checked && !document.hidden && !$('refresh').disabled) load(); }, 3000);
document.addEventListener('visibilitychange', () => { if (!document.hidden && $('auto').checked) load(); });
load();
