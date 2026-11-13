import { randomBytes, randomUUID } from 'node:crypto';
import { TurnError } from './policy.mjs';
import { validateScenario, parseModel, sourceBlocks, importMessages, annotatedScenario } from '../../../Assets/Seabreeze/Dialogue/EndingDemo/ending.mjs';

// Uploads are authored source material for one save, not actual world events.
export class EndingMemoryUploads {
  constructor(endings, engine, provider, checkRun, { now = Date.now } = {}) {
    this.endings = endings; this.engine = engine; this.provider = provider; this.checkRun = checkRun;
    this.now = now; this.sessions = new Map(); this.jobs = new Map();
  }
  open(input, origin) {
    const world = this.checkRun(input);
    for (const [key, value] of this.sessions) if (value.expires < this.now()) this.sessions.delete(key);
    if (this.sessions.size >= 64) throw new TurnError('上传窗口过多，请稍后重试');
    const token = randomBytes(24).toString('hex');
    this.sessions.set(token, { worldId: input.worldId, runId: input.runId, epoch: world.epoch,
      expires: this.now() + 30 * 60_000, generation: 0 });
    return `${origin}/ending-upload#${token}`;
  }
  session(token) {
    const session = this.sessions.get(token);
    if (!session || session.expires < this.now()) throw new TurnError('上传窗口已过期，请从游戏重新打开');
    const world = this.checkRun(session);
    if (world.epoch !== session.epoch) throw new TurnError('存档已切换，请从当前游戏重新打开上传窗口');
    return { session, world };
  }
  status(token) {
    const { session, world } = this.session(token);
    const source = this.endings.sourceState(world);
    const job = this.jobs.get(`${world.worldId}:${world.runId}`);
    return { ok: true, source, configured: this.provider.publicConfig().configured,
      ...(job?.sourceRevision === source.sourceRevision ? { job: { id: job.id, status: job.status, error: job.error || '' } } : {}) };
  }
  async import(token, input) {
    const { session, world } = this.session(token);
    if (typeof input.name !== 'string' || !/\.(txt|md|json)$/i.test(input.name) || input.name.length > 160)
      throw new TurnError('请选择 TXT、Markdown 或 JSON 记忆文件');
    const limit = /\.json$/i.test(input.name) ? 500_000 : 200_000;
    if (typeof input.text !== 'string' || Buffer.byteLength(input.text, 'utf8') > limit)
      throw new TurnError('文件过大或没有正文，TXT/Markdown 最多200KB，JSON最多500KB');
    const version = ++session.generation;
    const expectedRevision = this.endings.sourceState(world).sourceRevision;
    let scenario;
    if (/\.json$/i.test(input.name)) {
      scenario = validateScenario(parseModel(input.text), { maxMemories: 200 });
    } else {
      const blocks = sourceBlocks(input.text);
      if (!this.provider.publicConfig().configured) throw new TurnError('尚未连接模型，请先在游戏模型设置中连接，再在线解析文本');
      const response = await this.provider.call(this.provider.request(importMessages(blocks), { json: true, maxTokens: 7000 }), { signal: AbortSignal.timeout(120_000) });
      scenario = annotatedScenario(parseModel(response.text), blocks, input.name, input.text);
    }
    this.session(token);
    if (session.generation !== version) throw new TurnError('已选择另一份文件，旧解析结果已取消');
    session.draft = { id: randomUUID(), scenario, fileName: input.name, originalText: input.text, expectedRevision, version };
    return { ok: true, draftId: session.draft.id, scenario, expectedRevision };
  }
  use(token, input) {
    const { session, world } = this.session(token), draft = session.draft;
    if (!draft || input.draftId !== draft.id || draft.version !== session.generation)
      throw new TurnError('解析结果已更新，请重新选择文件');
    if (input.expectedRevision !== draft.expectedRevision || this.endings.sourceState(world).sourceRevision !== draft.expectedRevision)
      throw new TurnError('记忆已在另一个窗口更新，请重新解析后使用');
    if (!this.provider.publicConfig().configured) throw new TurnError('需要先连接模型，才能根据这份记忆生成结局');
    world.endingMemorySource = { scenario: structuredClone(draft.scenario), fileName: draft.fileName,
      originalText: draft.originalText, version: draft.id, uploadedAt: new Date(this.now()).toISOString() };
    this.engine.save(world);
    session.draft = null;
    const sourceRevision = this.endings.sourceState(world).sourceRevision;
    const job = { id: `upload_${draft.id}`, sourceRevision, status: 'generating', error: '' };
    this.jobs.set(`${world.worldId}:${world.runId}`, job);
    const request = { worldId: world.worldId, runId: world.runId, requestId: job.id, details: { mode: 'model' } };
    job.promise = this.endings.generate(request, this.checkRun).then(result => {
      job.status = 'complete'; job.result = result;
    }).catch(error => { job.status = 'failed'; job.error = error.message || '生成未完成，请重试'; });
    return { ok: true, jobId: job.id, source: this.endings.sourceState(world) };
  }
  export(token) {
    const { world } = this.session(token);
    const scenario = this.endings.authored(world);
    // Export only the uploaded base. The engine adds current game events separately,
    // so reimporting this file never duplicates game memory IDs or private records.
    return { ok: true, scenario };
  }
}
