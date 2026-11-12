import { $, api, status, node, download } from './common.js';
let run;
try {
  await status(); const id = new URLSearchParams(location.search).get('run');
  if (!id || !/^[0-9a-f-]{36}$/.test(id)) throw new Error('请先回到码头，开始这次结局。');
  run = await api(`/api/runs/${id}`); const d = run.decision;
  $('mode-note').textContent = run.mode === 'model' ? 'Z 根据本局经历作出的选择' : '示例演出 · 用于预览呈现，不代表模型决策';
  $('ending-title').textContent = d.title; document.title = `${d.title} · Project Z`;
  $('opening').textContent = d.opening; $('reason').textContent = d.reason; $('decision').textContent = d.decision;
  $('last-line').textContent = d.lastLine; $('outcome-label').textContent = run.outcome.label; $('outcome').textContent = run.outcome.text;
  for (const [i, r] of run.recollections.entries()) {
    const article = node('article', null, 'recall'); article.append(node('span', `${String(i + 1).padStart(2, '0')}`, 'recall-index'),
      node('span', `DAY ${String(r.day).padStart(2, '0')} · 仍然记得`, 'small-label'), node('h2', r.title), node('p', r.text));
    $('recollections').append(article);
    const original = node('article', null, 'original-card'); original.append(node('span', `DAY ${r.day} · ${r.memoryId}`, 'small-label'), node('h3', r.title), node('p', r.original)); $('originals').append(original);
  }
  $('coverage').textContent = `${run.memoryCount} / ${run.memoryCount} 段知情经历已回望 · 本局版本 ${run.revision}`;
  if (!run.recollections.length) { $('recollections').append(node('p', '这一局没有录入知情经历。她的答案，来自眼前的处境与自己的愿望。', 'no-memories')); }
} catch (error) { $('error').textContent = error.message; $('ending-title').textContent = '这一夜，尚未翻开'; }
$('begin-recall').onclick = () => ($('recollections').firstElementChild || $('decision-section')).scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth', block: 'start' });
$('export-ending').onclick = () => { if (run) download(run, `Z-ending-${run.decision.choice}-${run.id.slice(0, 8)}.json`); };
$('read-originals').onclick = () => $('original-dialog').showModal();
$('close-originals').onclick = () => $('original-dialog').close();
