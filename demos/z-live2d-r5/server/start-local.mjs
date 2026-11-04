import { existsSync } from 'node:fs';
import { spawn } from 'node:child_process';
import net from 'node:net';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const MIN_NODE_MAJOR = 22;
export const MIN_NODE_MINOR = 13;
export const LOCAL_HOST = '127.0.0.1';
export const LOCAL_PORT = 8796;

const SERVER_DIR = dirname(fileURLToPath(import.meta.url));
const SERVER_ENTRY = resolve(SERVER_DIR, 'server.mjs');
const ENV_FILE = resolve(SERVER_DIR, '.env');

export function supportsRequiredNode(version = process.versions.node) {
  const [major, minor] = String(version).replace(/^v/, '').split('.').map(Number);
  if (!Number.isInteger(major) || !Number.isInteger(minor)) return false;
  return major > MIN_NODE_MAJOR || (major === MIN_NODE_MAJOR && minor >= MIN_NODE_MINOR);
}

function portIsInUse(host, port) {
  return new Promise(resolveResult => {
    const socket = net.createConnection({ host, port });
    let settled = false;
    const finish = value => {
      if (settled) return;
      settled = true;
      socket.destroy();
      resolveResult(value);
    };
    socket.once('connect', () => finish(true));
    socket.once('error', () => finish(false));
    socket.setTimeout(400, () => finish(false));
  });
}

function childEnvironment(hasEnvFile, hasExplicitProvider) {
  const environment = { ...process.env };
  if (!hasEnvFile && !hasExplicitProvider) {
    // Do not accidentally activate an inherited compatible-provider secret when
    // this launcher has no local configuration and no explicit provider choice.
    delete environment.Z_LLM_BASE_URL;
    delete environment.Z_LLM_MODEL;
    delete environment.Z_LLM_API_KEY;
  }
  return environment;
}

function printStartupBanner(hasEnvFile, hasExplicitProvider) {
  console.log('Moon Z local service（仅本机对话服务，不代表真实 LLM 已验证）');
  console.log(`Node ${process.versions.node}：${process.execPath}`);
  console.log(`HTTP：http://${LOCAL_HOST}:${LOCAL_PORT}`);
  if (hasEnvFile) {
    console.log('发现 server/.env：将通过 Node --env-file 传入，不 source shell，也不打印变量值。');
  } else if (hasExplicitProvider) {
    console.log('未发现 server/.env：使用显式环境中的 Z_LLM_PROVIDER（不打印值）。');
  } else {
    console.log('未发现 server/.env：不猜测 provider，服务以 configured:false 启动；请配置后重启。');
  }
}

async function main() {
  if (!supportsRequiredNode()) {
    console.error(`需要 Node >= ${MIN_NODE_MAJOR}.${MIN_NODE_MINOR}，当前为 ${process.versions.node}。`);
    process.exitCode = 1;
    return;
  }
  if (!existsSync(SERVER_ENTRY)) {
    console.error(`找不到服务入口：${SERVER_ENTRY}`);
    process.exitCode = 1;
    return;
  }

  const hasEnvFile = existsSync(ENV_FILE);
  const hasExplicitProvider = typeof process.env.Z_LLM_PROVIDER === 'string'
    && process.env.Z_LLM_PROVIDER.trim().length > 0;
  printStartupBanner(hasEnvFile, hasExplicitProvider);

  if (await portIsInUse(LOCAL_HOST, LOCAL_PORT)) {
    console.log(`端口 ${LOCAL_PORT} 已被占用；不终止现有进程，请直接使用已有本地服务。`);
    return;
  }

  const args = [];
  if (hasEnvFile) args.push(`--env-file=${ENV_FILE}`);
  args.push(SERVER_ENTRY);

  const child = spawn(process.execPath, args, {
    cwd: SERVER_DIR,
    env: childEnvironment(hasEnvFile, hasExplicitProvider),
    stdio: 'inherit'
  });

  let forwardingSignal = false;
  const forwardSignal = signal => {
    forwardingSignal = true;
    if (!child.killed) child.kill(signal);
  };
  const onSigint = () => forwardSignal('SIGINT');
  const onSigterm = () => forwardSignal('SIGTERM');
  process.once('SIGINT', onSigint);
  process.once('SIGTERM', onSigterm);

  child.once('error', error => {
    console.error(`无法启动本地服务（${error.code || 'spawn error'}）。`);
  });
  child.once('exit', (code, signal) => {
    process.removeListener('SIGINT', onSigint);
    process.removeListener('SIGTERM', onSigterm);
    if (forwardingSignal || signal === 'SIGINT' || signal === 'SIGTERM') {
      process.exitCode = 0;
    } else {
      process.exitCode = code ?? 1;
    }
  });
}

if (import.meta.url === `file://${process.argv[1]}` || resolve(process.argv[1] || '') === fileURLToPath(import.meta.url)) {
  void main().catch(() => {
    console.error('本地服务启动器遇到未预期错误。');
    process.exitCode = 1;
  });
}
