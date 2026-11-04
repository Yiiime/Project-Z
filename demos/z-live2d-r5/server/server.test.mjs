import assert from 'node:assert/strict';
import http from 'node:http';
import { mkdtemp, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { test } from 'node:test';

import {
  buildPrompt,
  createApp,
  createServer,
  createOpenAICompatibleProvider,
  loadRuntimeSkill,
  normalizeModelResponse,
  RESPONSE_SCHEMA
} from './server.mjs';
import {
  EXPRESSION_LABELS,
  GESTURE_LABELS,
  GAZE_LABELS,
  PERFORMANCE_SELECTION_RULES,
} from './performance-contract.mjs';

const validReply = (text = '收到。') => ({
  segments: [{ text, expression: 'neutral', gesture: 'none', gaze: 'player' }],
  memory_updates: []
});

async function openServer({ provider, dbPath = ':memory:', timeoutMs = 500 } = {}) {
  const app = createApp({ dbPath, provider, timeoutMs });
  const server = createServer({ app, port: 0 });
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', resolve);
  });
  const address = server.address();
  const base = `http://127.0.0.1:${address.port}`;
  return {
    app,
    server,
    base,
    async close() {
      await new Promise(resolve => server.close(resolve));
      app.close();
    }
  };
}

async function request(base, path, options = {}) {
  const response = await fetch(`${base}${path}`, {
    ...options,
    headers: {
      Origin: base,
      ...(options.body ? { 'Content-Type': 'application/json' } : {}),
      ...(options.headers || {})
    }
  });
  const body = response.status === 204 ? null : await response.json();
  return { response, body };
}

async function post(base, path, body) {
  return request(base, path, { method: 'POST', body: JSON.stringify(body) });
}

async function chunkedPost(base, path, body, splitIndex) {
  const url = new URL(`${base}${path}`);
  const payload = Buffer.from(JSON.stringify(body), 'utf8');
  return new Promise((resolve, reject) => {
    const req = http.request({
      hostname: url.hostname,
      port: url.port,
      path: `${url.pathname}${url.search}`,
      method: 'POST',
      headers: {
        Origin: base,
        'Content-Type': 'application/json',
        'Content-Length': payload.byteLength
      }
    }, response => {
      const chunks = [];
      response.on('data', chunk => chunks.push(Buffer.from(chunk)));
      response.on('end', () => {
        try {
          resolve({ response, body: JSON.parse(Buffer.concat(chunks).toString('utf8')) });
        } catch (error) {
          reject(error);
        }
      });
    });
    req.on('error', reject);
    req.write(payload.subarray(0, splitIndex));
    req.end(payload.subarray(splitIndex));
  });
}

test('health and unconfigured provider are explicit', async () => {
  const runtime = await openServer({ provider: null });
  try {
    const health = await request(runtime.base, '/health');
    assert.equal(health.response.status, 200);
    assert.deepEqual(health.body, {
      status: 'ok',
      provider: 'injected',
      configured: false,
      skill_version: 'z-runtime-v0.1'
    });
    const chat = await post(runtime.base, '/v1/chat', { save_id: 'main', turn_id: 't1', message: '你好' });
    assert.equal(chat.response.status, 503);
    assert.equal(chat.body.code, 'PROVIDER_NOT_CONFIGURED');
  } finally {
    await runtime.close();
  }
});

test('performance contract keeps the 10 expressions and independent gestures', () => {
  assert.deepEqual(EXPRESSION_LABELS, [
    'neutral', 'smile', 'surprise', 'angry', 'troubled', 'blush',
    'cry', 'bawl', 'sigh', 'gloom',
  ]);
  assert.deepEqual(GESTURE_LABELS, ['none', 'wave', 'explain', 'point']);
  assert.deepEqual(GAZE_LABELS, ['player', 'away']);
  assert.deepEqual(RESPONSE_SCHEMA.properties.segments.items.properties.expression.enum, EXPRESSION_LABELS);
  assert.deepEqual(RESPONSE_SCHEMA.properties.segments.items.properties.gesture.enum, GESTURE_LABELS);
  assert.deepEqual(RESPONSE_SCHEMA.properties.segments.items.properties.gaze.enum, GAZE_LABELS);

  const systemPrompt = buildPrompt({
    skillText: loadRuntimeSkill(),
    history: [],
    memories: [],
    message: '今天有点难过。',
    saveId: 'main',
    turnId: 'contract-prompt',
  })[0].content;
  for (const label of [...EXPRESSION_LABELS, ...GESTURE_LABELS, ...GAZE_LABELS]) {
    assert.ok(systemPrompt.includes(label), `prompt 缺少标签 ${label}`);
  }
  for (const meaning of Object.values(PERFORMANCE_SELECTION_RULES.expressions)) {
    assert.ok(systemPrompt.includes(meaning), `prompt 缺少表情语义：${meaning}`);
  }
  for (const meaning of Object.values(PERFORMANCE_SELECTION_RULES.gestures)) {
    assert.ok(systemPrompt.includes(meaning), `prompt 缺少手势语义：${meaning}`);
  }
  for (const rule of PERFORMANCE_SELECTION_RULES.composition) {
    assert.ok(systemPrompt.includes(rule), `prompt 缺少组合规则：${rule}`);
  }

  for (const expression of EXPRESSION_LABELS) {
    for (const gesture of GESTURE_LABELS) {
      const normalized = normalizeModelResponse({
        segments: [{ text: 'fixture', expression, gesture, gaze: 'player' }],
        memory_updates: [],
      }, 'fixture');
      assert.deepEqual(normalized.segments[0], { text: 'fixture', expression, gesture, gaze: 'player' });
    }
  }

  const fallback = normalizeModelResponse({
    segments: [{ text: 'fixture', expression: 'unknown', gesture: 'dance', gaze: 'moon' }],
    memory_updates: [],
  }, 'fixture');
  assert.deepEqual(fallback.segments[0], {
    text: 'fixture',
    expression: 'neutral',
    gesture: 'none',
    gaze: 'player',
  });
});

test('chat waits for ack, commits history and memory, and is idempotent', async () => {
  let calls = 0;
  const runtime = await openServer({
    provider: {
      name: 'test-provider',
      async generateReply({ messages, schema }) {
        calls += 1;
        assert.equal(messages.at(-1).role, 'user');
        assert.equal(messages.at(-1).content, '我喜欢海边');
        assert.equal(schema.properties.segments.maxItems, 3);
        assert.deepEqual(schema.properties.segments.items.properties.expression.enum, EXPRESSION_LABELS);
        assert.deepEqual(schema.properties.segments.items.properties.gesture.enum, GESTURE_LABELS);
        return {
          segments: [{ text: '那我们找个时间去看看。', expression: 'smile', gesture: 'wave', gaze: 'player' }],
          memory_updates: [{ key: 'player.favorite_place', value: '海边', evidence: '我喜欢海边' }]
        };
      }
    }
  });
  try {
    const first = await post(runtime.base, '/v1/chat', { save_id: 'main', turn_id: 'turn-1', message: '我喜欢海边' });
    assert.equal(first.response.status, 200);
    assert.equal(first.body.provider, 'test-provider');
    assert.equal(first.body.memory_count, 0);
    assert.equal(first.body.segments[0].expression, 'smile');

    const beforeAck = await request(runtime.base, '/v1/session?save_id=main');
    assert.deepEqual(beforeAck.body.history, []);
    assert.deepEqual(beforeAck.body.memories, []);

    const duplicate = await post(runtime.base, '/v1/chat', { save_id: 'main', turn_id: 'turn-1', message: '我喜欢海边' });
    assert.equal(duplicate.response.status, 200);
    assert.deepEqual(duplicate.body.segments, first.body.segments);
    assert.equal(calls, 1);

    const ack = await post(runtime.base, '/v1/ack', { save_id: 'main', turn_id: 'turn-1' });
    assert.equal(ack.response.status, 200);
    assert.equal(ack.body.memory_count, 1);
    const ackAgain = await post(runtime.base, '/v1/ack', { save_id: 'main', turn_id: 'turn-1' });
    assert.equal(ackAgain.response.status, 200);
    assert.equal(ackAgain.body.memory_count, 1);

    const afterAck = await request(runtime.base, '/v1/session?save_id=main');
    assert.equal(afterAck.body.history.length, 2);
    assert.equal(afterAck.body.history[0].role, 'user');
    assert.equal(afterAck.body.history[1].role, 'assistant');
    assert.deepEqual(afterAck.body.memories, [{ key: 'player.favorite_place', value: '海边' }]);
  } finally {
    await runtime.close();
  }
});

test('SQLite state survives restart and stays isolated by save', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'moon-z-server-'));
  const dbPath = join(directory, 'state.sqlite');
  const first = await openServer({
    dbPath,
    provider: async () => ({
      segments: [{ text: '记住了。', expression: 'neutral', gesture: 'none', gaze: 'player' }],
      memory_updates: [{ key: 'player.nickname', value: '小Z', evidence: '叫我小Z' }]
    })
  });
  try {
    const chat = await post(first.base, '/v1/chat', { save_id: 'main', turn_id: 'restart-1', message: '叫我小Z' });
    assert.equal(chat.response.status, 200);
    await post(first.base, '/v1/ack', { save_id: 'main', turn_id: 'restart-1' });
    const isolated = await request(first.base, '/v1/session?save_id=other');
    assert.deepEqual(isolated.body.history, []);
    assert.deepEqual(isolated.body.memories, []);
  } finally {
    await first.close();
  }

  const second = await openServer({ dbPath, provider: null });
  try {
    const restored = await request(second.base, '/v1/session?save_id=main');
    assert.equal(restored.body.history.length, 2);
    assert.deepEqual(restored.body.memories, [{ key: 'player.nickname', value: '小Z' }]);
    const isolated = await request(second.base, '/v1/session?save_id=other');
    assert.deepEqual(isolated.body.history, []);
  } finally {
    await second.close();
    await rm(directory, { recursive: true, force: true });
  }
});

test('new turn cancels old turn and ignores a late provider result', async () => {
  let calls = 0;
  let oldResolve;
  let firstCall;
  const firstCalled = new Promise(resolve => { firstCall = resolve; });
  const runtime = await openServer({
    provider: {
      async generateReply({ messages }) {
        calls += 1;
        if (calls === 1) {
          firstCall();
          return new Promise(resolve => { oldResolve = resolve; });
        }
        return validReply(`第 ${messages.at(-1).content} 轮。`);
      }
    }
  });
  try {
    const oldChat = post(runtime.base, '/v1/chat', { save_id: 'main', turn_id: 'old', message: '旧问题' });
    await firstCalled;
    const newChat = await post(runtime.base, '/v1/chat', { save_id: 'main', turn_id: 'new', message: '新问题' });
    assert.equal(newChat.response.status, 200);
    const oldResponse = await oldChat;
    assert.equal(oldResponse.response.status, 409);
    assert.equal(oldResponse.body.code, 'TURN_CANCELLED');
    oldResolve(validReply('迟到的旧回复。'));
    await new Promise(resolve => setTimeout(resolve, 20));
    const session = await request(runtime.base, '/v1/session?save_id=main');
    assert.deepEqual(session.body.history, []);
  } finally {
    await runtime.close();
  }
});

test('cancel endpoint handles a provider that ignores AbortSignal', async () => {
  let resolveProvider;
  let providerCalled;
  const called = new Promise(resolve => { providerCalled = resolve; });
  const runtime = await openServer({
    provider: {
      async generateReply() {
        providerCalled();
        return new Promise(resolve => { resolveProvider = resolve; });
      }
    }
  });
  try {
    const chatPromise = post(runtime.base, '/v1/chat', { save_id: 'main', turn_id: 'cancel-1', message: '等一下' });
    await called;
    const cancelled = await post(runtime.base, '/v1/cancel', { save_id: 'main', turn_id: 'cancel-1' });
    assert.equal(cancelled.response.status, 200);
    assert.equal(cancelled.body.cancelled, true);
    const chat = await chatPromise;
    assert.equal(chat.response.status, 409);
    resolveProvider(validReply('迟到。'));
    await new Promise(resolve => setTimeout(resolve, 20));
    const session = await request(runtime.base, '/v1/session?save_id=main');
    assert.deepEqual(session.body.history, []);
  } finally {
    await runtime.close();
  }
});

test('a generated but unacked turn can be cancelled without committing', async () => {
  const runtime = await openServer({ provider: async () => validReply('先别提交。') });
  try {
    const chat = await post(runtime.base, '/v1/chat', { save_id: 'main', turn_id: 'generated-cancel', message: '先显示' });
    assert.equal(chat.response.status, 200);

    const cancelled = await post(runtime.base, '/v1/cancel', { save_id: 'main', turn_id: 'generated-cancel' });
    assert.equal(cancelled.response.status, 200);
    assert.equal(cancelled.body.cancelled, true);

    const ack = await post(runtime.base, '/v1/ack', { save_id: 'main', turn_id: 'generated-cancel' });
    assert.equal(ack.response.status, 409);
    assert.equal(ack.body.code, 'TURN_NOT_READY');
    const session = await request(runtime.base, '/v1/session?save_id=main');
    assert.deepEqual(session.body.history, []);
    assert.deepEqual(session.body.memories, []);
  } finally {
    await runtime.close();
  }
});

test('a new turn cancels a persisted generated turn before its old ack', async () => {
  let calls = 0;
  const runtime = await openServer({
    provider: async ({ message }) => {
      calls += 1;
      return validReply(calls === 1 ? `旧：${message}` : `新：${message}`);
    }
  });
  try {
    const old = await post(runtime.base, '/v1/chat', { save_id: 'main', turn_id: 'generated-old', message: '旧输入' });
    assert.equal(old.response.status, 200);
    const newer = await post(runtime.base, '/v1/chat', { save_id: 'main', turn_id: 'generated-new', message: '新输入' });
    assert.equal(newer.response.status, 200);

    const oldAck = await post(runtime.base, '/v1/ack', { save_id: 'main', turn_id: 'generated-old' });
    assert.equal(oldAck.response.status, 409);
    assert.equal(oldAck.body.code, 'TURN_NOT_READY');
    const newAck = await post(runtime.base, '/v1/ack', { save_id: 'main', turn_id: 'generated-new' });
    assert.equal(newAck.response.status, 200);
    const session = await request(runtime.base, '/v1/session?save_id=main');
    assert.deepEqual(session.body.history.map(item => item.turn_id), ['generated-new', 'generated-new']);
  } finally {
    await runtime.close();
  }
});

test('request bodies remain valid when UTF-8 JSON splits across chunks', async () => {
  let receivedMessage;
  const runtime = await openServer({
    provider: async ({ messages }) => {
      receivedMessage = messages.at(-1).content;
      return validReply('收到中文。');
    }
  });
  try {
    const body = { save_id: 'main', turn_id: 'utf8-chunk', message: '你好，海边' };
    const payload = Buffer.from(JSON.stringify(body), 'utf8');
    const marker = payload.indexOf(Buffer.from('你', 'utf8'));
    assert.ok(marker >= 0);
    const response = await chunkedPost(runtime.base, '/v1/chat', body, marker + 1);
    assert.equal(response.response.statusCode, 200);
    assert.equal(receivedMessage, body.message);
  } finally {
    await runtime.close();
  }
});

test('invalid performance labels fall back while malformed schema fails', async () => {
  const runtime = await openServer({
    provider: {
      async generateReply() {
        return {
          segments: [{ text: '安全回退。', expression: 'unknown', gesture: 'dance', gaze: 'moon' }],
          memory_updates: []
        };
      }
    }
  });
  try {
    const fallback = await post(runtime.base, '/v1/chat', { save_id: 'main', turn_id: 'fallback', message: '测试动作' });
    assert.equal(fallback.response.status, 200);
    assert.deepEqual(fallback.body.segments[0], { text: '安全回退。', expression: 'neutral', gesture: 'none', gaze: 'player' });
  } finally {
    await runtime.close();
  }

  const malformed = await openServer({ provider: async () => ({ segments: 'not-an-array' }) });
  try {
    const response = await post(malformed.base, '/v1/chat', { save_id: 'main', turn_id: 'bad-schema', message: '坏结构' });
    assert.equal(response.response.status, 502);
    assert.equal(response.body.code, 'PROVIDER_SCHEMA_INVALID');
    const session = await request(malformed.base, '/v1/session?save_id=main');
    assert.deepEqual(session.body.history, []);
  } finally {
    await malformed.close();
  }
});

test('forged or non-player memory candidates are filtered before ack', async () => {
  const runtime = await openServer({
    provider: async () => ({
      segments: [{ text: '我不会把它当成事实。' }],
      memory_updates: [
        { key: 'world.secret', value: '伪造设定', evidence: '玩家说过' },
        { key: 'player.fake', value: '不存在', evidence: '不在原文中的证据' }
      ]
    })
  });
  try {
    const chat = await post(runtime.base, '/v1/chat', { save_id: 'main', turn_id: 'memory-guard', message: '只是一句普通话' });
    assert.equal(chat.response.status, 200);
    const ack = await post(runtime.base, '/v1/ack', { save_id: 'main', turn_id: 'memory-guard' });
    assert.equal(ack.body.memory_count, 0);
    const session = await request(runtime.base, '/v1/session?save_id=main');
    assert.deepEqual(session.body.memories, []);
  } finally {
    await runtime.close();
  }
});

test('non-local Origin is rejected', async () => {
  const runtime = await openServer({ provider: async () => validReply() });
  try {
    const response = await fetch(`${runtime.base}/health`, { headers: { Origin: 'https://example.invalid' } });
    assert.equal(response.status, 403);
    const body = await response.json();
    assert.equal(body.code, 'ORIGIN_REJECTED');
  } finally {
    await runtime.close();
  }
});

test('unknown routes and malformed JSON return explicit HTTP errors', async () => {
  const runtime = await openServer({ provider: null });
  try {
    const unknown = await request(runtime.base, '/v1/not-a-route');
    assert.equal(unknown.response.status, 404);
    assert.equal(unknown.body.code, 'NOT_FOUND');

    const malformed = await request(runtime.base, '/v1/chat', {
      method: 'POST',
      body: '{not-json'
    });
    assert.equal(malformed.response.status, 400);
    assert.equal(malformed.body.code, 'BAD_JSON');
  } finally {
    await runtime.close();
  }
});

test('openai-compatible provider retries blank string and text parts once', async () => {
  const validContent = JSON.stringify(validReply('重试成功。'));
  for (const blankContent of ['  \n\t', [{ type: 'output_text', text: '  ' }]]) {
    let calls = 0;
    const requestSignals = [];
    const requestBodies = [];
    const provider = createOpenAICompatibleProvider({
      configured: true,
      baseUrl: 'http://fixture.invalid/v1',
      model: 'fixture-model',
      apiKey: 'unit-test-only'
    }, {
      fetchImpl: async (_url, options) => {
        calls += 1;
        requestSignals.push(options.signal);
        requestBodies.push(JSON.parse(options.body));
        return {
          ok: true,
          status: 200,
          async json() {
            return { choices: [{ message: { content: calls === 1 ? blankContent : validContent } }] };
          }
        };
      }
    });
    const controller = new AbortController();
    const actual = await provider.generateReply({
      messages: [{ role: 'user', content: 'synthetic probe' }],
      schema: RESPONSE_SCHEMA,
      signal: controller.signal
    });
    assert.deepEqual(actual, validReply('重试成功。'));
    assert.equal(calls, 2);
    assert.equal(requestSignals[0], controller.signal);
    assert.equal(requestSignals[1], controller.signal);
    assert.deepEqual(requestBodies[1].messages, requestBodies[0].messages);
    assert.deepEqual(requestBodies[1].response_format, { type: 'json_object' });
    assert.equal(requestBodies[1].stream, false);
  }
});

test('openai-compatible provider retries blank content when refusal is null', async () => {
  const validContent = JSON.stringify(validReply('null refusal 也可重试。'));
  let calls = 0;
  const provider = createOpenAICompatibleProvider({
    configured: true,
    baseUrl: 'http://fixture.invalid/v1',
    model: 'fixture-model',
    apiKey: 'unit-test-only'
  }, {
    fetchImpl: async () => {
      calls += 1;
      return {
        ok: true,
        status: 200,
        async json() {
          return { choices: [{ message: { content: calls === 1 ? ' ' : validContent, refusal: null } }] };
        }
      };
    }
  });

  const actual = await provider.generateReply({
    messages: [{ role: 'user', content: 'synthetic refusal-null probe' }],
    schema: RESPONSE_SCHEMA
  });
  assert.deepEqual(actual, validReply('null refusal 也可重试。'));
  assert.equal(calls, 2);
});

test('openai-compatible provider reports two blank responses distinctly', async () => {
  let calls = 0;
  const provider = createOpenAICompatibleProvider({
    configured: true,
    baseUrl: 'http://fixture.invalid/v1',
    model: 'fixture-model',
    apiKey: 'unit-test-only'
  }, {
    fetchImpl: async () => {
      calls += 1;
      return {
        ok: true,
        status: 200,
        async json() {
          return { choices: [{ message: { content: ' \n' } }] };
        }
      };
    }
  });

  await assert.rejects(provider.generateReply({
    messages: [{ role: 'user', content: 'synthetic probe' }],
    schema: RESPONSE_SCHEMA
  }), error => {
    assert.equal(error.status, 502);
    assert.equal(error.code, 'PROVIDER_EMPTY_RESPONSE');
    assert.match(error.message, /重试/);
    return true;
  });
  assert.equal(calls, 2);
});

test('openai-compatible provider does not retry non-blank invalid JSON or blocked shapes', async () => {
  const cases = [
    ['non-blank-invalid-json', { choices: [{ message: { content: 'not-json' } }] }],
    ['no-choices', { choices: [] }],
    ['no-message', { choices: [{}] }],
    ['null-content', { choices: [{ message: { content: null } }] }],
    ['tool-call', { choices: [{ message: { content: ' ', tool_calls: [{ type: 'function' }] }, finish_reason: 'tool_calls' }] }],
    ['refusal', { choices: [{ message: { content: ' ', refusal: 'fixture' } }] }],
    ['length', { choices: [{ message: { content: ' ' }, finish_reason: 'length' }] }],
    ['content-filter', { choices: [{ message: { content: ' ' }, finish_reason: 'content_filter' }] }],
    ['non-text-part', { choices: [{ message: { content: [{ type: 'refusal', refusal: 'fixture' }] } }] }],
    ['refusal-text-part', { choices: [{ message: { content: [{ type: 'refusal', text: ' ' }] } }] }]
  ];

  for (const [name, payload] of cases) {
    let calls = 0;
    const provider = createOpenAICompatibleProvider({
      configured: true,
      baseUrl: 'http://fixture.invalid/v1',
      model: 'fixture-model',
      apiKey: 'unit-test-only'
    }, {
      fetchImpl: async () => {
        calls += 1;
        return { ok: true, status: 200, async json() { return payload; } };
      }
    });
    await assert.rejects(provider.generateReply({
      messages: [{ role: 'user', content: `synthetic ${name}` }],
      schema: RESPONSE_SCHEMA
    }), error => {
      assert.equal(error.status, 502, name);
      assert.equal(error.code, 'PROVIDER_SCHEMA_INVALID', name);
      return true;
    });
    assert.equal(calls, 1, name);
  }
});

test('openai-compatible provider stops before retry when signal aborts', async () => {
  const controller = new AbortController();
  let calls = 0;
  const provider = createOpenAICompatibleProvider({
    configured: true,
    baseUrl: 'http://fixture.invalid/v1',
    model: 'fixture-model',
    apiKey: 'unit-test-only'
  }, {
    fetchImpl: async (_url, options) => {
      calls += 1;
      assert.equal(options.signal, controller.signal);
      controller.abort();
      return {
        ok: true,
        status: 200,
        async json() {
          return { choices: [{ message: { content: ' ' } }] };
        }
      };
    }
  });

  await assert.rejects(provider.generateReply({
    messages: [{ role: 'user', content: 'synthetic abort probe' }],
    schema: RESPONSE_SCHEMA,
    signal: controller.signal
  }), error => {
    assert.equal(error.status, 504);
    assert.equal(error.code, 'PROVIDER_ABORTED');
    return true;
  });
  assert.equal(calls, 1);
});

test('openai-compatible provider does not retry authentication or HTTP failures', async () => {
  for (const status of [401, 429, 500]) {
    let calls = 0;
    const provider = createOpenAICompatibleProvider({
      configured: true,
      baseUrl: 'http://fixture.invalid/v1',
      model: 'fixture-model',
      apiKey: 'unit-test-only'
    }, {
      fetchImpl: async () => {
        calls += 1;
        return { ok: false, status, async json() { return {}; } };
      }
    });
    await assert.rejects(provider.generateReply({
      messages: [{ role: 'user', content: 'synthetic http probe' }],
      schema: RESPONSE_SCHEMA
    }), error => {
      assert.equal(error.status, 502);
      assert.ok(['PROVIDER_AUTH_FAILED', 'PROVIDER_RATE_LIMITED', 'PROVIDER_HTTP_ERROR'].includes(error.code));
      return true;
    });
    assert.equal(calls, 1);
  }
});

assert.equal(RESPONSE_SCHEMA.properties.segments.maxItems, 3);
assert.deepEqual(RESPONSE_SCHEMA.properties.segments.items.required, ['text', 'expression', 'gesture', 'gaze']);
assert.deepEqual(RESPONSE_SCHEMA.required, ['segments', 'memory_updates']);
