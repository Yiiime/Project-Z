import { createServer as httpServer } from 'node:http';
import { readFile, writeFile, mkdir, rename } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { homedir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { randomBytes, randomUUID } from 'node:crypto';
import { ModelProvider } from '../../../../Tools/MemoryLab/lib/provider.mjs';
import { EndingError, validateScenario, revisionOf, decisionMessages, validateDecision, parseModel,
  recollectionMessages, validateRecollections, previewDecision, previewRecollections, resolveOutcome,
  validateDailyPages, previewDailyPages, approvedDailyPagesFor, requestDailyPage, sourceBlocks, importMessages, annotatedScenario } from './ending.mjs';

export const ROOT = dirname(fileURLToPath(import.meta.url));
const PROJECT = resolve(ROOT, '../../../..');
const DEFAULT_DATA = join(homedir(), '.project-z', 'ending-demo');
const atomic = async (file, value) => {
  await mkdir(dirname(file), { recursive: true });
  const temp = `${file}.${randomUUID()}.tmp`;
  await writeFile(temp, JSON.stringify(value, null, 2), { mode: 0o600 });
  await rename(temp, file);
};
export async function createEndingServer({ dataDir = process.env.Z_ENDING_DATA || DEFAULT_DATA, provider: suppliedProvider,
  selectionPath = join(DEFAULT_DATA, 'source-selection.json') } = {}) {
  await mkdir(dataDir, { recursive: true });
  const scenarioFile = join(dataDir, 'scenario.json'), configFile = join(dataDir, 'model.json');
  let scenario = validateScenario(JSON.parse(await readFile(existsSync(scenarioFile) ? scenarioFile : join(ROOT, 'scenario.example.json'), 'utf8')));
  let config = existsSync(configFile) ? JSON.parse(await readFile(configFile, 'utf8')) : {};
  const provider = suppliedProvider || new ModelProvider({ settings: () => config, saveSettings: value => { config = value; } });
  if (!suppliedProvider) {
    const source = process.env.Z_MEMORY_CONFIG || configFile;
    if (existsSync(source)) {
      const saved = JSON.parse(await readFile(source, 'utf8'));
      if (saved.apiKey) provider.set(saved);
    }
    if (process.env.GAME_MODEL_API_KEY) provider.set({ provider: 'deepseek', baseUrl: 'https://api.deepseek.com', model: process.env.GAME_MODEL_NAME || 'deepseek-chat', apiKey: process.env.GAME_MODEL_API_KEY });
  }
  const persona = await readFile(join(PROJECT, 'Tools/MemoryLab/content/current/moonlife-actors/references/characters/z.md'), 'utf8');
  const token = randomBytes(24).toString('hex'), runs = new Map();
  let busy = false;
  const json = (res, status, value) => { res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' }); res.end(JSON.stringify(value)); };
  const publicFiles = new Map([['/', 'index.html'], ['/editor', 'editor.html'], ['/ending', 'ending.html'],
    ['/app.css', 'app.css'], ['/common.js', 'common.js'], ['/app.js', 'app.js'], ['/editor.js', 'editor.js'], ['/ending.js', 'ending.js']]);
  const assets = new Map([['/assets/coast.png', join(PROJECT, 'Assets/Seabreeze/Resources/Art/lighthouse.png')],
    ['/assets/z.png', join(PROJECT, 'Assets/Seabreeze/Resources/Art/CastMotion/z/z_idle_000.png')]]);
  const mime = file => file.endsWith('.css') ? 'text/css' : file.endsWith('.js') ? 'text/javascript' : file.endsWith('.png') ? 'image/png' : 'text/html';
  const body = async req => {
    const chunks = []; let bytes = 0;
    for await (const chunk of req) { bytes += chunk.length; if (bytes > 1000000) throw new EndingError('输入超过1MB，请缩短经历', 413); chunks.push(chunk); }
    try { return JSON.parse(Buffer.concat(chunks).toString('utf8')); } catch { throw new EndingError('输入不是合法JSON'); }
  };
  const server = httpServer(async (req, res) => {
    try {
      const address = server.address(), authority = `127.0.0.1:${address.port}`;
      if (![authority, `localhost:${address.port}`].includes(req.headers.host)) throw new EndingError('仅支持本机访问', 403);
      if (req.headers.origin && ![`http://${authority}`, `http://localhost:${address.port}`].includes(req.headers.origin)) throw new EndingError('请从本机Demo页面操作', 403);
      const url = new URL(req.url, `http://${authority}`);
      res.setHeader('X-Content-Type-Options', 'nosniff');
      res.setHeader('Content-Security-Policy', "default-src 'self'; img-src 'self'; style-src 'self'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'");
      if (req.method === 'GET' && (publicFiles.has(url.pathname) || assets.has(url.pathname))) {
        const file = assets.get(url.pathname) || join(ROOT, 'public', publicFiles.get(url.pathname));
        res.writeHead(200, { 'Content-Type': `${mime(file)}${file.endsWith('.png') ? '' : '; charset=utf-8'}`, 'Cache-Control': 'no-cache' }); res.end(await readFile(file)); return;
      }
      if (req.method === 'GET' && url.pathname === '/api/status') {
        json(res, 200, { app: 'project-z-ending-demo', token, busy, model: provider.publicConfig(), revision: revisionOf(scenario) }); return;
      }
      if (req.headers['x-ending-token'] !== token) throw new EndingError('页面连接已更新，请刷新', 403);
      if (req.method === 'GET' && url.pathname === '/api/scenario') { json(res, 200, { scenario, revision: revisionOf(scenario) }); return; }
      if (req.method === 'POST' && url.pathname === '/api/validate') {
        json(res, 200, { scenario: validateScenario((await body(req)).scenario) }); return;
      }
      if (req.method === 'POST' && url.pathname === '/api/import') {
        if (busy) throw new EndingError('正在整理或生成，请等待当前操作完成', 409);
        const input = await body(req);
        if (typeof input.name !== 'string' || !/\.(txt|md|json)$/i.test(input.name)) throw new EndingError('请选择TXT、Markdown或JSON文件');
        if (typeof input.text !== 'string') throw new EndingError('未读取到文件正文');
        if (/\.json$/i.test(input.name)) { json(res, 200, { scenario: validateScenario(parseModel(input.text)), revision: revisionOf(scenario) }); return; }
        const blocks = sourceBlocks(input.text);
        if (!provider.publicConfig().configured) throw new EndingError('整理TXT或Markdown需要模型连接；请展开下方连接设置，或导入已有JSON经历文件', 422);
        busy = true;
        try {
          const result = await provider.call(provider.request(importMessages(blocks), { json: true, maxTokens: 7000 }));
          json(res, 200, { scenario: annotatedScenario(parseModel(result.text), blocks, input.name, input.text), revision: revisionOf(scenario) });
        } finally { busy = false; }
        return;
      }
      if (req.method === 'PUT' && url.pathname === '/api/scenario') {
        const input = await body(req);
        if (busy) throw new EndingError('Z正在作出选择，请等待完成再保存', 409);
        if (input.revision !== revisionOf(scenario)) throw new EndingError('本局经历已在另一个窗口更新，请重新加载后合并', 409);
        const next = validateScenario(input.scenario); await atomic(scenarioFile, next); scenario = next;
        if (input.useFile && selectionPath) await atomic(selectionPath, { scenarioPath: scenarioFile, revision: revisionOf(next) });
        json(res, 200, { scenario, revision: revisionOf(scenario) }); return;
      }
      if (req.method === 'PUT' && url.pathname === '/api/model') {
        if (busy) throw new EndingError('请等待当前生成完成', 409);
        const input = await body(req), result = provider.set(input);
        await atomic(configFile, { ...provider.config, apiKey: provider.key }); json(res, 200, result); return;
      }
      if (req.method === 'POST' && url.pathname === '/api/generate') {
        const input = await body(req);
        if (busy) throw new EndingError('正在生成，请勿重复提交', 409);
        if (input.revision !== revisionOf(scenario)) throw new EndingError('经历已更新，请重新加载后再开始', 409);
        if (!['model', 'preview-leave', 'preview-stay'].includes(input.mode)) throw new EndingError('生成模式无效');
        if (input.mode === 'model' && !provider.publicConfig().configured) throw new EndingError('请先在编辑入口连接模型，或查看示例演出', 422);
        busy = true;
        const snapshot = structuredClone(scenario), revision = revisionOf(snapshot);
        try {
          let decision, recollections, dailyPages;
          if (input.mode === 'model') {
            const first = await provider.call(provider.request(decisionMessages(snapshot, persona), { json: true, maxTokens: 2500 }));
            decision = validateDecision(parseModel(first.text), snapshot);
            if (snapshot.memories.some(m => m.zKnows)) {
              const second = await provider.call(provider.request(recollectionMessages(snapshot, decision, persona), { json: true, maxTokens: 14000 }));
              recollections = validateRecollections(parseModel(second.text), snapshot);
              dailyPages = [];
              const knownDays = [...new Set(snapshot.memories.filter(m => m.zKnows).map(m => m.day))].sort((a, b) => a - b);
              const approved = approvedDailyPagesFor(snapshot);
              if (approved) {
                dailyPages = validateDailyPages({ dailyPages: approved }, snapshot);
              } else {
                const recollectionById = new Map(recollections.map(item => [item.memoryId, item.text]));
                for (const day of knownDays) {
                  const dayScenario = { ...snapshot, memories: snapshot.memories.filter(m => m.zKnows && m.day === day).map(memory => ({
                    ...memory, text: recollectionById.get(memory.id) || memory.text,
                  })) };
                  dailyPages.push(await requestDailyPage(provider, dayScenario, decision, day, persona));
                }
              }
            } else { recollections = []; dailyPages = []; }
          } else {
            decision = previewDecision(snapshot, input.mode === 'preview-leave' ? 'leave' : 'stay');
            recollections = previewRecollections(snapshot); dailyPages = previewDailyPages(snapshot);
          }
          const run = { id: randomUUID(), revision, mode: input.mode, createdAt: new Date().toISOString(),
            playerName: snapshot.playerName, scenarioTitle: snapshot.title, decision, recollections, dailyPages,
            outcome: resolveOutcome(snapshot, decision.choice), memoryCount: recollections.length };
          await atomic(join(dataDir, 'runs', `${run.id}.json`), run); runs.set(run.id, run);
          if (runs.size > 50) runs.delete(runs.keys().next().value);
          json(res, 200, run);
        } finally { busy = false; }
        return;
      }
      if (req.method === 'GET' && /^\/api\/runs\/[0-9a-f-]{36}$/.test(url.pathname)) {
        const id = url.pathname.split('/').pop(); let run = runs.get(id);
        if (!run) { try { run = JSON.parse(await readFile(join(dataDir, 'runs', `${id}.json`), 'utf8')); } catch { throw new EndingError('找不到这次结局，请回到码头重新开始', 404); } }
        json(res, 200, run); return;
      }
      throw new EndingError('没有这个页面', 404);
    } catch (error) { json(res, error.status || 500, { error: error.status ? error.message : '本机服务未能完成操作，请查看终端并重试' }); }
  });
  return { server, provider, dataDir };
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const app = await createEndingServer();
  app.server.listen(Number(process.env.Z_ENDING_PORT || 4319), '127.0.0.1', () => {
    console.log(`Project Z 结局试玩：http://127.0.0.1:${app.server.address().port}\n上传故事文件：http://127.0.0.1:${app.server.address().port}/editor\n本机保存目录：${app.dataDir}`);
  });
}
