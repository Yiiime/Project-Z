import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { access, writeFile } from 'node:fs/promises';
import { PassThrough, Writable } from 'node:stream';
import test from 'node:test';

import {
  CODEX_TOOL_CALL_BLOCKED,
  DISABLED_FEATURES,
  buildCodexArgs,
  createCodexProvider,
} from './codex-provider.mjs';

const schema = {
  type: 'object',
  properties: { answer: { type: 'string' } },
  required: ['answer'],
  additionalProperties: false,
};

const messages = [
  { role: 'system', content: '你是 Z。' },
  { role: 'user', content: '今天还好吗？' },
];

function createFakeSpawn({ onInputEnd, closeOnInput = true, stdoutLines = [] } = {}) {
  const calls = [];

  const spawnImpl = (command, args, options) => {
    const child = new EventEmitter();
    const stdout = new PassThrough();
    const stderr = new PassThrough();
    let input = '';
    let closed = false;

    child.pid = 987654321;
    child.stdout = stdout;
    child.stderr = stderr;
    child.killed = false;
    child.killSignal = null;
    child.kill = (signal) => {
      child.killed = true;
      child.killSignal = signal;
      finish(null, signal);
      return true;
    };

    const finish = (code = 0, signal = null) => {
      if (closed) return;
      closed = true;
      for (const line of stdoutLines) stdout.write(`${JSON.stringify(line)}\n`);
      stdout.end();
      stderr.end();
      queueMicrotask(() => child.emit('close', code, signal));
    };

    child.stdin = new Writable({
      write(chunk, _encoding, callback) {
        input += chunk.toString();
        callback();
      },
      final(callback) {
        Promise.resolve(onInputEnd?.({ args, child, input, options }))
          .then(() => {
            callback();
            if (closeOnInput) finish();
          })
          .catch(callback);
      },
    });

    calls.push({ args, child, command, input: () => input, options });
    return child;
  };

  return { calls, spawnImpl };
}

async function assertRemoved(path) {
  await assert.rejects(access(path), { code: 'ENOENT' });
}

async function waitForCall(calls, { timeoutMs = 2_000, pollMs = 10 } = {}) {
  const deadline = Date.now() + timeoutMs;
  while (calls.length === 0 && Date.now() < deadline) {
    await new Promise((resolve) => setTimeout(resolve, pollMs));
  }
  if (calls.length > 0) return calls[0];
  throw new Error(`fake spawn was not called within ${timeoutMs}ms`);
}

async function abortAndDrain(promise, controller) {
  controller.abort();
  await promise.catch(() => {});
}

test('buildCodexArgs pins the safe non-interactive invocation', () => {
  const args = buildCodexArgs({
    model: 'test-model',
    outputPath: '/tmp/moon-z-last.json',
    schemaPath: '/tmp/moon-z-schema.json',
  });

  assert.deepEqual(args.slice(0, 2), ['exec', '--ignore-user-config']);
  assert.ok(args.includes('--ephemeral'));
  assert.ok(args.includes('--skip-git-repo-check'));
  assert.ok(args.includes('--json'));
  assert.ok(args.includes('--color'));
  assert.ok(args.includes('never'));
  assert.ok(args.includes('--output-schema'));
  assert.ok(args.includes('/tmp/moon-z-schema.json'));
  assert.ok(args.includes('--output-last-message'));
  assert.ok(args.includes('/tmp/moon-z-last.json'));
  assert.ok(args.includes('-s'));
  assert.ok(args.includes('read-only'));
  assert.ok(args.includes('-c'));
  assert.ok(args.includes('project_doc_max_bytes=0'));
  assert.ok(args.includes("web_search='disabled'"));
  assert.ok(args.includes("model_reasoning_effort='low'"));
  assert.ok(args.includes('--model'));
  assert.ok(args.includes('test-model'));
  assert.ok(args.includes('--enable'));
  assert.ok(args.includes('skip_host_skill_discovery'));
  for (const feature of DISABLED_FEATURES) {
    assert.ok(args.includes('--disable'), `missing --disable for ${feature}`);
    assert.ok(args.includes(feature), `missing disabled feature ${feature}`);
  }
  assert.equal(args.at(-1), '-');
  assert.ok(!args.includes('--ignore-rules'));
  assert.ok(!args.includes('--yolo'));
  assert.ok(!args.includes('--dangerously-bypass-approvals-and-sandbox'));
});

test('generateReply sends compiled context, parses schema output, and cleans up', async () => {
  const result = { answer: '我还好。' };
  const fake = createFakeSpawn({
    onInputEnd: async ({ args, input }) => {
      const prompt = JSON.parse(input);
      assert.deepEqual(prompt.messages, messages);
      assert.equal(prompt.kind, 'moon-z-dialogue-context');
      const outputPath = args[args.indexOf('--output-last-message') + 1];
      await writeFile(outputPath, JSON.stringify(result), 'utf8');
    },
    stdoutLines: [
      { type: 'thread.started', thread_id: 'test-thread' },
      { type: 'item.completed', item: { type: 'agent_message', text: '已生成。' } },
      { type: 'turn.completed' },
    ],
  });
  const provider = createCodexProvider({ spawnImpl: fake.spawnImpl });

  const actual = await provider.generateReply({ messages, schema });

  assert.deepEqual(actual, result);
  assert.equal(fake.calls.length, 1);
  const call = fake.calls[0];
  assert.match(call.command, /(?:^|\/)codex$/);
  assert.equal(call.options.shell, false);
  assert.equal(call.options.cwd, call.options.cwd.trim());
  assert.ok(call.options.cwd.startsWith('/tmp/') || call.options.cwd.includes('/T/'));
  await assertRemoved(call.options.cwd);
});

test('a tool-call JSON event aborts the child and never returns output', async () => {
  const fake = createFakeSpawn({
    closeOnInput: false,
    stdoutLines: [],
  });
  const provider = createCodexProvider({ spawnImpl: fake.spawnImpl, killGraceMs: 20, timeoutMs: 2_000 });
  const controller = new AbortController();

  const promise = provider.generateReply({ messages, schema, signal: controller.signal });
  promise.catch(() => {});
  let call;
  try {
    call = await waitForCall(fake.calls);
    call.child.stdout.write(JSON.stringify({
      type: 'item.started',
      item: { type: 'command_execution', command: 'echo forbidden' },
    }) + '\n');

    await assert.rejects(promise, (error) => {
      assert.equal(error.code, CODEX_TOOL_CALL_BLOCKED);
      return true;
    });
    assert.equal(call.child.killed, true);
    await assertRemoved(call.options.cwd);
  } catch (error) {
    await abortAndDrain(promise, controller);
    throw error;
  }
});

test('AbortSignal cancels the process group and cleans up', async () => {
  const fake = createFakeSpawn({ closeOnInput: false });
  const provider = createCodexProvider({ spawnImpl: fake.spawnImpl, killGraceMs: 20, timeoutMs: 2_000 });
  const controller = new AbortController();

  const promise = provider.generateReply({ messages, schema, signal: controller.signal });
  promise.catch(() => {});
  let call;
  try {
    call = await waitForCall(fake.calls);
    controller.abort();

    await assert.rejects(promise, (error) => {
      assert.equal(error.name, 'AbortError');
      assert.equal(error.code, 'PROVIDER_ABORTED');
      return true;
    });
    assert.equal(call.child.killed, true);
    await assertRemoved(call.options.cwd);
  } catch (error) {
    await abortAndDrain(promise, controller);
    throw error;
  }
});

test('invalid last-message JSON is rejected without leaving a temp directory', async () => {
  const fake = createFakeSpawn({
    onInputEnd: async ({ args }) => {
      const outputPath = args[args.indexOf('--output-last-message') + 1];
      await writeFile(outputPath, 'not-json', 'utf8');
    },
  });
  const provider = createCodexProvider({ spawnImpl: fake.spawnImpl });

  await assert.rejects(provider.generateReply({ messages, schema }), (error) => {
    assert.equal(error.code, 'CODEX_OUTPUT_INVALID');
    return true;
  });
  await assertRemoved(fake.calls[0].options.cwd);
});
