import { $, api, status, node } from './common.js';
let revision, busy = false, audio;
function originalCard(m) {
  const article = node('article', null, 'original-card'); article.append(node('span', `DAY ${String(m.day).padStart(2, '0')} · ${m.kind === 'plot' ? '岛上剧情' : '共同记忆'}`, 'small-label'), node('h3', m.title), node('p', m.text)); return article;
}
try {
  const s = await status(), data = await api('/api/scenario'); revision = data.revision;
  const memories = data.scenario.memories.filter(m => m.zKnows).sort((a, b) => a.day - b.day);
  $('memory-count').textContent = memories.length; $('scenario-title').textContent = data.scenario.title;
  $('memory-list').replaceChildren(...memories.map(originalCard));
  if (!memories.length) $('memory-list').append(node('p', '这一局还没有录入 Z 已知的经历。你可以先写进来，也可以听她根据眼前处境作出选择。', 'no-memories'));
  $('model-note').textContent = s.model.configured ? `${s.model.providerName}已连接 · 她会根据本局经历自主判断` : '先到编辑入口连接模型；也可展开下方查看示例演出。';
  $('start').disabled = !s.model.configured;
} catch (error) { $('error').textContent = error.message; $('start').disabled = true; }
$('show-memories').onclick = () => $('memory-dialog').showModal();
$('close-memories').onclick = () => $('memory-dialog').close();
async function generate(mode) {
  if (busy) return; busy = true; $('error').textContent = ''; $('waiting').hidden = false;
  const started = Date.now(); const timer = setInterval(() => { $('wait-time').textContent = `已等待 ${Math.round((Date.now() - started) / 1000)} 秒 · 页面会在完成后自动翻开`; }, 1000);
  try { const run = await api('/api/generate', { method: 'POST', body: JSON.stringify({ mode, revision }) }); location.href = `/ending?run=${encodeURIComponent(run.id)}`; }
  catch (error) { $('error').textContent = error.message; $('waiting').hidden = true; }
  finally { busy = false; clearInterval(timer); }
}
$('start').onclick = () => generate('model');
$('preview-leave').onclick = () => generate('preview-leave');
$('preview-stay').onclick = () => generate('preview-stay');
// A quiet, optional sea ambience made locally. It never autoplays.
$('sound').onclick = async () => {
  if (audio) { await audio.close(); audio = null; $('sound').textContent = '海风 · 关闭'; $('sound').setAttribute('aria-pressed', 'false'); return; }
  try {
    audio = new AudioContext(); const buffer = audio.createBuffer(1, audio.sampleRate * 4, audio.sampleRate), data = buffer.getChannelData(0);
    for (let i = 0; i < data.length; i++) data[i] = (Math.random() * 2 - 1) * .2;
    const noise = audio.createBufferSource(); noise.buffer = buffer; noise.loop = true;
    const filter = audio.createBiquadFilter(); filter.type = 'lowpass'; filter.frequency.value = 700;
    const gain = audio.createGain(); gain.gain.value = .12; noise.connect(filter).connect(gain).connect(audio.destination); noise.start();
    await audio.resume(); $('sound').textContent = '海风 · 开启'; $('sound').setAttribute('aria-pressed', 'true');
  } catch { $('error').textContent = '当前浏览器不能播放海风声，仍可继续阅读。'; }
};
