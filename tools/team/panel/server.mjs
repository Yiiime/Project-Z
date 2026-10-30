#!/usr/bin/env node
// Local-only collaboration panel. It listens on 127.0.0.1, calls the local gh CLI and never stores tokens.
import {spawn} from 'node:child_process';
import {randomBytes} from 'node:crypto';
import {existsSync} from 'node:fs';
import {readFile} from 'node:fs/promises';
import {createServer} from 'node:http';
import {homedir} from 'node:os';
import {join} from 'node:path';
import * as core from '../collab-core.mjs';
import {
  ROOT, TaskError, UNITY_VERSION, board, currentUser, exec, findGh, git, loadConfig, prompt, repoName,
  saveTask, startTask, stillWorking, takeoverTask,
} from '../task.mjs';

const HOST = '127.0.0.1';
const PORT = Number(process.env.Z_PANEL_PORT || 4387);
const SESSION = randomBytes(24).toString('hex');
const MAX_BODY = 64 * 1024;
const jobs = new Map();
let busy = null;
let login = null;

const send = (res, status, body, type = 'application/json; charset=utf-8') => {
  res.writeHead(status, {
    'Content-Type': type,
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
    'Referrer-Policy': 'no-referrer',
    'Content-Security-Policy': "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'",
  });
  res.end(typeof body === 'string' || Buffer.isBuffer(body) ? body : JSON.stringify(body));
};

async function body(req) {
  let text = '';
  for await (const chunk of req) {
    text += chunk;
    if (text.length > MAX_BODY) throw new TaskError('请求太大。');
  }
  return text ? JSON.parse(text) : {};
}

function guard(req) {
  const host = req.headers.host || '';
  if (host !== `${HOST}:${PORT}` && host !== `localhost:${PORT}`) throw new TaskError('只接受本机访问。', {code: 403});
  if (req.method !== 'GET') {
    const origin = req.headers.origin;
    if (origin && origin !== `http://${HOST}:${PORT}` && origin !== `http://localhost:${PORT}`) throw new TaskError('拒绝来自其他网页的请求。', {code: 403});
    if (req.headers['x-z-session'] !== SESSION) throw new TaskError('页面已过期，请刷新。', {code: 403});
  }
}

async function unityPath() {
  const candidates = [
    process.env.Z_UNITY_EDITOR,
    `/Applications/Unity/Hub/Editor/${UNITY_VERSION}/Unity.app/Contents/MacOS/Unity`,
    join(homedir(), `Applications/Unity/Hub/Editor/${UNITY_VERSION}/Unity.app/Contents/MacOS/Unity`),
    join(ROOT, `../../.tooling/project-z/unity-${UNITY_VERSION}/Unity.app/Contents/MacOS/Unity`),
  ].filter(Boolean);
  return candidates.find(path => existsSync(path)) || '';
}

async function dotnetOk() {
  const candidates = [process.env.Z_DOTNET, join(ROOT, '.tooling/project-z/dotnet/dotnet'), join(ROOT, '../../.tooling/project-z/dotnet/dotnet'), '/usr/local/share/dotnet/dotnet', 'dotnet'].filter(Boolean);
  for (const command of candidates) {
    const result = await exec(command, ['--list-sdks'], {allowFail: true}).catch(() => null);
    if (result?.code === 0 && /^8\./m.test(result.stdout)) return command;
  }
  return '';
}

async function checks() {
  const items = [];
  const add = (id, label, ok, detail, action = null, optional = false) => items.push({id, label, ok, detail, action, optional});
  const gitVersion = await exec('git', ['--version'], {allowFail: true}).catch(() => ({code: 1}));
  add('git', 'Git', gitVersion.code === 0, gitVersion.code === 0 ? gitVersion.stdout.trim() : '安装 Xcode Command Line Tools 后重开面板。', gitVersion.code ? {type: 'link', label: '安装说明', url: 'https://developer.apple.com/xcode/resources/'} : null);
  const major = Number(process.versions.node.split('.')[0]);
  const minor = Number(process.versions.node.split('.')[1]);
  add('node', 'Node.js', major > 20 || (major === 20 && minor >= 19), `当前 ${process.version}；需要 22 LTS（至少 20.19）。`, {type: 'link', label: '下载 Node.js 22', url: 'https://nodejs.org/'});
  const dotnet = await dotnetOk();
  add('dotnet', '.NET 8 SDK', Boolean(dotnet), dotnet ? '可以在本机运行 Unity 快速检查。' : '没有也能开发，GitHub 会检查；装上后本机交付更快发现问题。', dotnet ? null : {type: 'link', label: '下载 .NET 8 SDK', url: 'https://dotnet.microsoft.com/download/dotnet/8.0'}, true);
  const ghPath = await findGh();
  add('gh', 'GitHub CLI', Boolean(ghPath), ghPath ? ghPath : '面板通过它访问 GitHub。', ghPath ? null : {type: 'link', label: '下载 GitHub CLI', url: 'https://cli.github.com/'});
  let loggedIn = false;
  let user = null;
  if (ghPath) {
    const status = await exec(ghPath, ['auth', 'status', '--hostname', 'github.com'], {allowFail: true});
    loggedIn = status.code === 0;
    if (loggedIn) user = await currentUser().catch(() => null);
    add('login', 'GitHub 登录', loggedIn, loggedIn ? `已登录：${user?.login || 'GitHub'}` : '点按钮后，在浏览器输入面板显示的一次性代码并确认。', loggedIn ? null : {type: 'login', label: '在浏览器登录'});
    if (loggedIn) {
      const helpers = (await exec('git', ['config', '--global', '--get-all', 'credential.https://github.com.helper'], {allowFail: true})).stdout;
      const setup = helpers.includes('auth git-credential') ? {code: 0} : await exec(ghPath, ['auth', 'setup-git', '--hostname', 'github.com'], {allowFail: true});
      add('gitauth', 'Git 推送授权', setup.code === 0, setup.code === 0 ? 'Git 使用 GitHub CLI 的登录推送。' : setup.stderr.trim());
    }
  }
  if (loggedIn) {
    const repo = await repoName().catch(() => '');
    const permission = repo ? await exec(ghPath, ['api', `repos/${repo}`, '--jq', '.permissions.push'], {allowFail: true}) : {code: 1};
    const ok = permission.code === 0 && permission.stdout.trim() === 'true';
    add('write', '仓库写权限', ok, ok ? repo : '请把 GitHub 用户名发给维护者，接受邀请后点“重新检查”。', ok ? null : {type: 'link', label: '查看我的邀请', url: 'https://github.com/notifications'});
  }
  const name = (await exec('git', ['config', 'user.name'], {allowFail: true})).stdout.trim();
  const email = (await exec('git', ['config', 'user.email'], {allowFail: true})).stdout.trim();
  const canFix = Boolean(user?.login);
  add('identity', 'Git 署名', Boolean(name && email), name && email ? `${name} <${email}>` : '提交记录需要你的名字和 GitHub noreply 邮箱。', name && email ? null : (canFix ? {type: 'identity', label: '用 GitHub 账号设置'} : null));
  const unity = await unityPath();
  add('unity', `Unity ${UNITY_VERSION}`, Boolean(unity), unity ? '已找到编辑器。' : '改场景、动画或试玩时需要。用 Unity Hub 安装 Apple Silicon 版本。', unity ? null : {type: 'link', label: '打开 Unity Hub 安装页', url: `unityhub://${UNITY_VERSION}/672d562a42a7`}, true);
  return {items, ready: items.filter(item => !item.optional).every(item => item.ok)};
}

function startLogin() {
  if (login?.running) return login;
  login = {running: true, code: '', url: 'https://github.com/login/device', output: '', done: false, ok: false};
  findGh().then(path => {
    if (!path) throw new TaskError('没有找到 GitHub CLI。');
    const child = spawn(path, ['auth', 'login', '--hostname', 'github.com', '--git-protocol', 'https', '--web', '--skip-ssh-key'], {cwd: ROOT, env: {...process.env, GH_PROMPT_DISABLED: ''}});
    const read = chunk => {
      login.output += chunk.toString();
      login.code ||= login.output.match(/[A-Z0-9]{4}-[A-Z0-9]{4}/)?.[0] || '';
    };
    child.stdout.on('data', read);
    child.stderr.on('data', read);
    setTimeout(() => child.stdin.write('\n'), 800);
    child.on('close', code => Object.assign(login, {running: false, done: true, ok: code === 0, output: code === 0 ? '登录完成。' : '登录没有完成，可以再试一次。'}));
  }).catch(error => Object.assign(login, {running: false, done: true, ok: false, output: error.message}));
  return login;
}

async function setIdentity() {
  const user = await currentUser();
  await git(['config', '--global', 'user.name', user.login]);
  await git(['config', '--global', 'user.email', `${user.id}+${user.login}@users.noreply.github.com`]);
  return {ok: true};
}

function runShip(confirmed = []) {
  const id = randomBytes(8).toString('hex');
  const job = {id, running: true, ok: false, output: '', startedAt: new Date().toISOString()};
  jobs.set(id, job);
  const args = ['tools/team/z', 'ship', ...confirmed.flatMap(login => ['--confirmed', login])];
  findGh().then(path => {
    const child = spawn('bash', args, {cwd: ROOT, env: {...process.env, Z_GH: path}});
    const read = chunk => { job.output = (job.output + chunk.toString()).slice(-30000); };
    child.stdout.on('data', read);
    child.stderr.on('data', read);
    child.on('close', code => Object.assign(job, {running: false, ok: code === 0, code, finishedAt: new Date().toISOString()}));
  });
  return job;
}

function exclusive(name, work) {
  if (busy) throw new TaskError(`正在${busy}，请等它完成。`, {code: 409});
  busy = name;
  return Promise.resolve().then(work).finally(() => { busy = null; });
}

function promptsFor(data) {
  const result = {};
  for (const task of data.tasks) {
    result[task.number] = Object.fromEntries(['start', 'save', 'ship', 'conflict', 'checks'].map(kind => [kind, prompt(kind, {...data, task})]));
  }
  return result;
}

async function boardPayload() {
  const data = await board();
  return {...data, prompts: promptsFor(data), busy};
}

const routes = {
  'GET /api/session': async () => ({session: SESSION, version: 1}),
  'GET /api/checks': checks,
  'POST /api/login': async () => startLogin(),
  'GET /api/login': async () => login || {running: false, done: false},
  'POST /api/identity': async () => exclusive('设置 Git 署名', setIdentity),
  'GET /api/board': boardPayload,
  'POST /api/preview-start': async input => {
    const [config, data] = await Promise.all([loadConfig(), board()]);
    const areas = core.selectedAreas(config, input.areaIds || []);
    const warnings = core.startWarnings(config, data.tasks, areas.map(area => area.id), data.me.login);
    return {warnings, prompt: core.codexPrompt('start', {goal: input.goal || '（写一句目标）', areas, branch: '开工后自动创建'})};
  },
  'POST /api/start': async input => exclusive('开工', () => startTask({goal: input.goal, areaIds: input.areaIds || [], slug: input.slug || '', confirmed: input.confirmed || []})),
  'POST /api/save': async input => exclusive('保存进度', () => saveTask({message: input.message || '', handoff: input.handoff || ''})),
  'POST /api/still': async input => exclusive('更新活动时间', () => stillWorking({number: input.number})),
  'POST /api/takeover': async input => exclusive('接手任务', () => takeoverTask({number: input.number, mode: input.mode, note: input.note || ''})),
  'POST /api/switch': async input => exclusive('切换任务', async () => {
    if (await git(['status', '--porcelain'])) throw new TaskError('当前有未保存修改，先保存进度再切换。');
    const data = await board({withNames: false});
    const task = data.tasks.find(item => item.number === Number(input.number));
    if (!task || task.owner !== data.me.login) throw new TaskError('只能切换到自己负责的任务。');
    await git(['fetch', 'origin', task.branch]);
    if (await git(['branch', '--list', task.branch])) {
      await git(['switch', task.branch]);
      await git(['merge', '--ff-only', `origin/${task.branch}`]);
    } else await git(['switch', '-c', task.branch, '--track', `origin/${task.branch}`]);
    return {branch: task.branch};
  }),
  'POST /api/ship-check': async input => {
    const result = await exec(process.execPath, ['tools/team/task.mjs', 'ship-check', '--json', ...(input.confirmed || []).flatMap(login => ['--confirmed', login])], {allowFail: true, env: {Z_GH: await findGh()}});
    const payload = JSON.parse(result.stdout || '{}');
    return {...payload, ok: result.code === 0, message: result.code ? result.stderr.replace(/^✗ /, '').trim() : ''};
  },
  'POST /api/ship': async input => {
    if (busy) throw new TaskError(`正在${busy}，请等它完成。`, {code: 409});
    return runShip(input.confirmed || []);
  },
  'GET /api/job': async (_input, url) => {
    const job = jobs.get(url.searchParams.get('id'));
    if (!job) throw new TaskError('任务已过期。', {code: 404});
    return job;
  },
  'POST /api/open': async input => {
    const allowed = /^https:\/\/(github\.com|nodejs\.org|dotnet\.microsoft\.com|cli\.github\.com|developer\.apple\.com)\//;
    if (!allowed.test(input.url || '') && !/^unityhub:\/\//.test(input.url || '')) throw new TaskError('不支持打开这个地址。');
    await exec('open', [input.url]);
    return {ok: true};
  },
};

const STATIC = {
  '/': ['index.html', 'text/html; charset=utf-8'],
  '/app.js': ['app.js', 'text/javascript; charset=utf-8'],
  '/styles.css': ['styles.css', 'text/css; charset=utf-8'],
};

const server = createServer(async (req, res) => {
  const url = new URL(req.url, `http://${HOST}:${PORT}`);
  try {
    guard(req);
    const file = STATIC[url.pathname];
    if (req.method === 'GET' && file) return send(res, 200, await readFile(new URL(file[0], import.meta.url)), file[1]);
    const route = routes[`${req.method} ${url.pathname}`];
    if (!route) return send(res, 404, {error: '没有这个页面。'});
    send(res, 200, await route(req.method === 'POST' ? await body(req) : {}, url));
  } catch (error) {
    const status = [403, 404, 409].includes(error.code) ? error.code : 400;
    send(res, status, {error: error.message || String(error), details: error.details || null});
  }
});

server.on('error', async error => {
  if (error.code === 'EADDRINUSE') {
    const response = await fetch(`http://${HOST}:${PORT}/api/session`).catch(() => null);
    if (response?.ok) {
      if (!process.env.Z_PANEL_NO_OPEN) spawn('open', [`http://${HOST}:${PORT}/`], {stdio: 'ignore', detached: true}).unref();
      console.log(`协作面板已经在运行：http://${HOST}:${PORT}/`);
      process.exit(0);
    }
  }
  console.error(`无法启动协作面板：${error.message}`);
  process.exit(1);
});

server.listen(PORT, HOST, () => {
  const address = `http://${HOST}:${PORT}/`;
  console.log(`Z 协作面板：${address}`);
  console.log('关闭这个窗口会停止面板。');
  if (!process.env.Z_PANEL_NO_OPEN) spawn('open', [address], {stdio: 'ignore', detached: true}).unref();
});
