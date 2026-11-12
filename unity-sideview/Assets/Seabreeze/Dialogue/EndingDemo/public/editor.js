import { $, api, status, node, download } from './common.js';
let current, draft, revision, busy = false;
function showCurrent() {
  const count = current.memories.filter(m => m.zKnows).length;
  $('current-file').textContent = `${current.sourceName || '示例经历'} · ${current.title} · ${count} 段 Z 已知经历`;
}
function showDraft() {
  $('import-preview').hidden = false; $('preview-title').textContent = draft.title;
  $('preview-count').textContent = `${draft.memories.length} 段经历 · ${draft.memories.filter(m => m.zKnows).length} 段 Z 已知`;
  $('preview-context').textContent = draft.context;
  $('preview-memories').replaceChildren(...draft.memories.map(m => {
    const card = node('article', null, 'original-card'); card.append(node('span', `DAY ${m.day} · ${m.zKnows ? 'Z 已知' : 'Z 未知，作者侧保留'}`, 'small-label'), node('h3', m.title), node('p', m.text)); return card;
  }));
}
async function loadFile(file) {
  if (!file || busy) return;
  if (!/\.(txt|md|json)$/i.test(file.name)) { $('error').textContent = '请选择TXT、Markdown或JSON文件。'; return; }
  if (file.size > (/\.json$/i.test(file.name) ? 500000 : 200000)) { $('error').textContent = '文件过大：TXT/Markdown最多200KB，JSON最多500KB。'; return; }
  busy = true; draft = null; $('error').textContent = ''; $('import-preview').hidden = true;
  $('notice').textContent = '正在读取并整理文件，原文会完整保留……'; $('drop-zone').classList.add('loading');
  try {
    const text = await file.text();
    const result = await api('/api/import', { method: 'POST', body: JSON.stringify({ name: file.name, text }) });
    draft = result.scenario; revision = result.revision; showDraft();
    $('notice').textContent = '已整理。确认预览后，点“使用这份文件”开始本局。';
  } catch (error) { $('error').textContent = error.message; $('notice').textContent = ''; }
  finally { busy = false; $('drop-zone').classList.remove('loading'); $('story-file').value = ''; }
}
try {
  const s = await status(), data = await api('/api/scenario'); current = data.scenario; revision = data.revision; showCurrent();
  for (const key of ['provider', 'baseUrl', 'model', 'apiType']) $(key).value = s.model[key];
  $('connection-summary').textContent = s.model.configured ? `模型已连接 · ${s.model.providerName}` : '连接模型（整理文本文件及生成结局需要）';
  $('model-status').textContent = s.model.configured ? `${s.model.providerName}已连接` : '尚未连接';
} catch (error) { $('error').textContent = error.message; }
$('story-file').onchange = event => loadFile(event.target.files[0]);
$('drop-zone').ondragover = event => { event.preventDefault(); $('drop-zone').classList.add('drag-over'); };
$('drop-zone').ondragleave = () => $('drop-zone').classList.remove('drag-over');
$('drop-zone').ondrop = event => { event.preventDefault(); $('drop-zone').classList.remove('drag-over'); loadFile(event.dataTransfer.files[0]); };
$('use-file').onclick = async () => {
  if (!draft || busy) return; busy = true; $('use-file').disabled = true; $('error').textContent = '';
  try {
    const saved = await api('/api/scenario', { method: 'PUT', body: JSON.stringify({ revision, scenario: draft }) });
    revision = saved.revision; current = saved.scenario; draft = null; busy = false; location.href = '/';
  } catch (error) { $('error').textContent = error.message; }
  finally { busy = false; $('use-file').disabled = false; }
};
$('export').onclick = () => { if (current) download(current, 'Z-seven-days.json'); };
$('provider').onchange = () => { const ark = $('provider').value === 'ark'; $('baseUrl').value = ark ? 'https://ark.cn-beijing.volces.com/api/v3' : 'https://api.deepseek.com'; $('model').value = ark ? '' : 'deepseek-flash'; $('apiType').value = 'chat'; $('apiKey').value = ''; };
$('save-model').onclick = async () => {
  $('save-model').disabled = true; $('error').textContent = '';
  try { const input = {}; for (const key of ['provider', 'baseUrl', 'model', 'apiType', 'apiKey']) input[key] = $(key).value;
    const result = await api('/api/model', { method: 'PUT', body: JSON.stringify(input) }); $('apiKey').value = ''; $('model-status').textContent = `${result.providerName}已连接`; $('connection-summary').textContent = `模型已连接 · ${result.providerName}`;
  } catch (error) { $('error').textContent = error.message; }
  finally { $('save-model').disabled = false; }
};
addEventListener('beforeunload', e => { if (draft || busy) { e.preventDefault(); e.returnValue = ''; } });
