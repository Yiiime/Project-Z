import { spawn as defaultSpawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { mkdtemp as defaultMkdtemp, readFile as defaultReadFile, rm as defaultRm, writeFile as defaultWriteFile } from 'node:fs/promises';
import { homedir, tmpdir } from 'node:os';
import { join } from 'node:path';
import { StringDecoder } from 'node:string_decoder';

export const DEFAULT_TIMEOUT_MS = 120_000;
export const DEFAULT_MAX_STDOUT_BYTES = 1_048_576;
export const DEFAULT_MAX_STDERR_BYTES = 64 * 1024;

// These names were checked against `codex features list` on 2026-11-02 with
// codex-cli 0.153.4. Keep this list explicit so a feature can never be
// silently re-enabled by a user's config file.
export const DISABLED_FEATURES = Object.freeze([
  'shell_tool',
  'apps',
  'plugins',
  'browser_use',
  'computer_use',
  'multi_agent',
  'view_image',
  'image_generation',
  'skill_search',
  'memories',
  'hooks',
  'goals',
  'sleep_tool',
  'artifact',
  'code_mode',
]);

export const CODEX_TOOL_CALL_BLOCKED = 'CODEX_TOOL_CALL_BLOCKED';

const TOOL_TYPE_PATTERN = /(?:tool[_-]?call|tool[_-]?use|mcp|command[_-]?execution|function[_-]?call|shell[_-]?tool|browser[_-]?use|computer[_-]?use|web[_-]?search|image[_-]?generation|skill[_-]?search|plugin|code[_-]?mode|multi[_-]?agent)/i;
const TOOL_KEY_PATTERN = /^(?:tool|tool[_-]?name|tool[_-]?call|mcp|command|function|browser|computer|plugin|skill)$/i;

export class CodexProviderError extends Error {
  constructor(code, message, options = {}) {
    super(message, options.cause ? { cause: options.cause } : undefined);
    this.name = 'CodexProviderError';
    this.code = code;
  }
}

function createAbortError(code, message) {
  const error = new CodexProviderError(code, message);
  error.name = 'AbortError';
  return error;
}

function isNonEmptyString(value) {
  return typeof value === 'string' && value.trim().length > 0;
}

/**
 * Resolve the executable without depending on an interactive shell PATH.
 * Z_CODEX_BIN is a path only; it is passed to spawn as argv[0], never parsed
 * as shell text. The fallback checks the conventional standalone install
 * location before falling back to PATH lookup.
 */
export function resolveCodexCommand() {
  if (isNonEmptyString(process.env.Z_CODEX_BIN)) return process.env.Z_CODEX_BIN.trim();
  const localInstall = join(homedir(), '.local', 'bin', 'codex');
  return existsSync(localInstall) ? localInstall : 'codex';
}

function assertPath(value, name) {
  if (!isNonEmptyString(value)) throw new TypeError(`${name} must be a non-empty path.`);
}

/**
 * Build argv for the official `codex exec` CLI. The final `-` makes stdin the
 * complete prompt; all user/role text therefore remains data rather than a
 * shell argument. No approval or sandbox bypass flag is included.
 */
export function buildCodexArgs({ schemaPath, outputPath, model = process.env.Z_CODEX_MODEL } = {}) {
  assertPath(schemaPath, 'schemaPath');
  assertPath(outputPath, 'outputPath');

  const args = [
    'exec',
    '--ignore-user-config',
    '--ephemeral',
    '--skip-git-repo-check',
    '--json',
    '--color',
    'never',
    '--output-schema',
    schemaPath,
    '--output-last-message',
    outputPath,
    '-s',
    'read-only',
    '-c',
    'project_doc_max_bytes=0',
    '-c',
    "web_search='disabled'",
    '-c',
    "model_reasoning_effort='low'",
    '--enable',
    'skip_host_skill_discovery',
  ];

  for (const feature of DISABLED_FEATURES) args.push('--disable', feature);

  if (model !== undefined && model !== null && String(model).trim() !== '') {
    args.push('--model', String(model));
  }

  args.push('-');
  return args;
}

/**
 * Compile already-filtered role and user messages into a JSON stdin payload.
 * The model receives the role context from the messages, while the wrapper
 * asks only for the schema-constrained final object. No input is logged.
 */
export function compilePrompt(messages) {
  if (!Array.isArray(messages)) throw new CodexProviderError('CODEX_INPUT_INVALID', 'messages 必须是数组。');

  const normalized = messages.map((message, index) => {
    if (!message || typeof message !== 'object' || !isNonEmptyString(message.role) || typeof message.content !== 'string') {
      throw new CodexProviderError('CODEX_INPUT_INVALID', `第 ${index + 1} 条消息结构无效。`);
    }
    return { role: message.role, content: message.content };
  });

  return JSON.stringify({
    kind: 'moon-z-dialogue-context',
    messages: normalized,
    output_instruction: 'Return exactly one JSON object matching the supplied output schema. Do not call tools.',
  });
}

/**
 * Detect tool-call events in Codex JSONL without treating arbitrary message
 * text as a tool call. Unknown event types are allowed; structured tool-like
 * item/type/key values are blocked conservatively.
 */
export function isToolCallEvent(event) {
  let found = false;

  const visit = (value) => {
    if (found || value === null || value === undefined) return;
    if (Array.isArray(value)) {
      for (const item of value) visit(item);
      return;
    }
    if (typeof value !== 'object') return;

    for (const [key, child] of Object.entries(value)) {
      if (found) return;
      if (TOOL_KEY_PATTERN.test(key)) {
        found = true;
        return;
      }
      if (key.toLowerCase() === 'type' && typeof child === 'string' && TOOL_TYPE_PATTERN.test(child)) {
        found = true;
        return;
      }
      visit(child);
    }
  };

  visit(event);
  return found;
}

function asBufferByteLength(chunk) {
  if (typeof chunk === 'string') return Buffer.byteLength(chunk);
  if (chunk && typeof chunk.byteLength === 'number') return chunk.byteLength;
  return Buffer.byteLength(String(chunk ?? ''));
}

function killProcessGroup(child) {
  if (!child) return;

  let signalled = false;
  if (process.platform !== 'win32' && Number.isInteger(child.pid) && child.pid > 0) {
    try {
      process.kill(-child.pid, 'SIGTERM');
      signalled = true;
    } catch {
      // A test double or a process that already exited may have no group.
    }
  }

  if (!signalled && typeof child.kill === 'function') {
    try {
      child.kill('SIGTERM');
    } catch {
      // Cleanup is best effort; the caller still receives the original error.
    }
  }
}

function killProcessGroupHard(child) {
  if (!child) return;

  let signalled = false;
  if (process.platform !== 'win32' && Number.isInteger(child.pid) && child.pid > 0) {
    try {
      process.kill(-child.pid, 'SIGKILL');
      signalled = true;
    } catch {
      // Fall through to the child handle for test doubles/exited groups.
    }
  }

  if (!signalled && typeof child.kill === 'function') {
    try {
      child.kill('SIGKILL');
    } catch {
      // Nothing else can be done without exposing process details.
    }
  }
}

function inspectJsonLine(line, onToolCall) {
  const trimmed = line.trim();
  if (!trimmed) return;

  let event;
  try {
    event = JSON.parse(trimmed);
  } catch {
    // Codex owns stdout formatting. A malformed progress line is ignored here;
    // the canonical response still must come from --output-last-message.
    return;
  }
  if (isToolCallEvent(event)) onToolCall();
}

function runCodex({ command, args, cwd, prompt, signal, spawnImpl, timeoutMs, maxStdoutBytes, maxStderrBytes, killGraceMs }) {
  return new Promise((resolve, reject) => {
    let child;
    let settled = false;
    let failure = null;
    let timeoutHandle;
    let forceKillHandle;
    let stdoutBytes = 0;
    let stderrBytes = 0;
    let stdoutText = '';
    const stdoutDecoder = new StringDecoder('utf8');

    const removeAbortListener = () => {
      if (signal && typeof signal.removeEventListener === 'function') signal.removeEventListener('abort', onAbort);
    };

    const settle = (error = null) => {
      if (settled) return;
      settled = true;
      if (timeoutHandle) clearTimeout(timeoutHandle);
      if (forceKillHandle) clearTimeout(forceKillHandle);
      removeAbortListener();
      if (error) reject(error);
      else resolve();
    };

    const terminate = () => {
      killProcessGroup(child);
      if (killGraceMs <= 0) {
        settle(failure || new CodexProviderError('CODEX_PROCESS_FAILED', 'Codex CLI 进程未正常结束。'));
        return;
      }
      forceKillHandle = setTimeout(() => {
        killProcessGroupHard(child);
        settle(failure || new CodexProviderError('CODEX_PROCESS_FAILED', 'Codex CLI 进程未正常结束。'));
      }, killGraceMs);
    };

    const fail = (error) => {
      if (settled || failure) return;
      failure = error;
      terminate();
    };

    const onAbort = () => fail(createAbortError('PROVIDER_ABORTED', 'Codex 请求已取消。'));

    const onStdout = (chunk) => {
      if (settled || failure) return;
      stdoutBytes += asBufferByteLength(chunk);
      if (stdoutBytes > maxStdoutBytes) {
        fail(new CodexProviderError('CODEX_OUTPUT_LIMIT', 'Codex stdout 超出大小限制。'));
        return;
      }

      stdoutText += stdoutDecoder.write(typeof chunk === 'string' ? Buffer.from(chunk) : chunk);
      let newlineIndex;
      while ((newlineIndex = stdoutText.indexOf('\n')) >= 0) {
        const line = stdoutText.slice(0, newlineIndex);
        stdoutText = stdoutText.slice(newlineIndex + 1);
        inspectJsonLine(line, () => fail(new CodexProviderError(CODEX_TOOL_CALL_BLOCKED, 'Codex 试图调用未授权工具。')));
        if (failure || settled) return;
      }
    };

    const onStderr = (chunk) => {
      if (settled || failure) return;
      stderrBytes += asBufferByteLength(chunk);
      if (stderrBytes > maxStderrBytes) fail(new CodexProviderError('CODEX_OUTPUT_LIMIT', 'Codex stderr 超出大小限制。'));
    };

    const onClose = (code, closeSignal) => {
      if (settled) return;
      if (failure) {
        settle(failure);
        return;
      }
      if (code !== 0) {
        settle(new CodexProviderError('CODEX_PROCESS_FAILED', 'Codex CLI 返回失败状态。'));
        return;
      }
      const tail = stdoutText + stdoutDecoder.end();
      if (tail.trim()) inspectJsonLine(tail, () => fail(new CodexProviderError(CODEX_TOOL_CALL_BLOCKED, 'Codex 试图调用未授权工具。')));
      if (failure) settle(failure);
      else settle();
      void closeSignal;
    };

    try {
      child = spawnImpl(command, args, {
        cwd,
        detached: process.platform !== 'win32',
        shell: false,
        stdio: ['pipe', 'pipe', 'pipe'],
        windowsHide: true,
      });
    } catch (error) {
      settle(new CodexProviderError('CODEX_SPAWN_FAILED', 'Codex CLI 启动失败。', { cause: error }));
      return;
    }

    child.stdout?.on('data', onStdout);
    child.stderr?.on('data', onStderr);
    child.on?.('error', (error) => {
      if (failure || settled) return;
      fail(new CodexProviderError('CODEX_SPAWN_FAILED', 'Codex CLI 进程出错。', { cause: error }));
    });
    child.on?.('close', onClose);

    if (signal && typeof signal.addEventListener === 'function') {
      signal.addEventListener('abort', onAbort, { once: true });
      if (signal.aborted) {
        onAbort();
        return;
      }
    }

    timeoutHandle = setTimeout(() => fail(createAbortError('PROVIDER_TIMEOUT', 'Codex 请求超时。')), timeoutMs);

    try {
      child.stdin.end(prompt);
    } catch (error) {
      fail(new CodexProviderError('CODEX_SPAWN_FAILED', '无法向 Codex CLI 写入请求。', { cause: error }));
    }
  });
}

function serializeSchema(schema) {
  let text;
  try {
    text = JSON.stringify(schema);
  } catch (error) {
    throw new CodexProviderError('CODEX_INPUT_INVALID', 'schema 不是可序列化 JSON。', { cause: error });
  }
  if (!text) throw new CodexProviderError('CODEX_INPUT_INVALID', 'schema 不能为空。');
  return text;
}

function parseLastMessage(text) {
  try {
    return JSON.parse(String(text).replace(/^\uFEFF/, ''));
  } catch (error) {
    throw new CodexProviderError('CODEX_OUTPUT_INVALID', 'Codex 最终消息不是有效 JSON。', { cause: error });
  }
}

/**
 * Create an injectable provider. Tests replace only the child-process boundary;
 * production uses the official `codex` executable and real filesystem cleanup.
 */
export function createCodexProvider(options = {}) {
  const {
    command = resolveCodexCommand(),
    spawnImpl = defaultSpawn,
    mkdtempImpl = defaultMkdtemp,
    readFileImpl = defaultReadFile,
    rmImpl = defaultRm,
    writeFileImpl = defaultWriteFile,
    tempRoot = tmpdir(),
    timeoutMs = DEFAULT_TIMEOUT_MS,
    maxStdoutBytes = DEFAULT_MAX_STDOUT_BYTES,
    maxStderrBytes = DEFAULT_MAX_STDERR_BYTES,
    killGraceMs = 500,
  } = options;

  if (typeof spawnImpl !== 'function') throw new TypeError('spawnImpl must be a function.');
  if (typeof mkdtempImpl !== 'function' || typeof readFileImpl !== 'function' || typeof rmImpl !== 'function' || typeof writeFileImpl !== 'function') {
    throw new TypeError('filesystem injection helpers must be functions.');
  }
  if (!Number.isFinite(timeoutMs) || timeoutMs <= 0) throw new TypeError('timeoutMs must be positive.');
  if (!Number.isFinite(maxStdoutBytes) || maxStdoutBytes <= 0 || !Number.isFinite(maxStderrBytes) || maxStderrBytes <= 0) {
    throw new TypeError('output limits must be positive.');
  }
  if (!Number.isFinite(killGraceMs) || killGraceMs < 0) throw new TypeError('killGraceMs must be non-negative.');

  const generateReply = async ({ messages, schema, signal } = {}) => {
    const prompt = compilePrompt(messages);
    const schemaText = serializeSchema(schema);
    let tempDirectory;
    let result;
    let caughtError = null;

    try {
      tempDirectory = await mkdtempImpl(join(tempRoot, 'moon-z-codex-'));
      const schemaPath = join(tempDirectory, 'response.schema.json');
      const outputPath = join(tempDirectory, 'last-message.json');
      await writeFileImpl(schemaPath, schemaText, 'utf8');
      const args = buildCodexArgs({ model: process.env.Z_CODEX_MODEL, outputPath, schemaPath });
      await runCodex({
        args,
        command,
        cwd: tempDirectory,
        killGraceMs,
        maxStderrBytes,
        maxStdoutBytes,
        prompt,
        signal,
        spawnImpl,
        timeoutMs,
      });
      result = parseLastMessage(await readFileImpl(outputPath, 'utf8'));
    } catch (error) {
      caughtError = error;
    }

    if (tempDirectory) {
      try {
        await rmImpl(tempDirectory, { recursive: true, force: true });
      } catch (error) {
        if (!caughtError) caughtError = new CodexProviderError('CODEX_CLEANUP_FAILED', '无法清理 Codex 临时目录。', { cause: error });
      }
    }

    if (caughtError) throw caughtError;
    return result;
  };

  return { name: 'codex', generateReply };
}

const defaultProvider = createCodexProvider();

// Fixed provider contract used by server.mjs. The default is lazy: importing
// this module never starts Codex; the CLI is launched only when called.
export async function generateReply({ messages, schema, signal } = {}) {
  return defaultProvider.generateReply({ messages, schema, signal });
}

export default generateReply;
