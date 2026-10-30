#!/usr/bin/env node
// Local task commands used by tools/team/z and the collaboration panel. Uses only git, gh and Node built-ins.
import {spawn} from 'node:child_process';
import {constants} from 'node:fs';
import {access, mkdtemp, readFile, rm, writeFile} from 'node:fs/promises';
import {homedir, tmpdir} from 'node:os';
import {delimiter, dirname, join, resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import * as core from './collab-core.mjs';

export const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
export const UNITY_VERSION = '6000.3.27f1';
export const UNITY_CHANGESET = '672d562a42a7';
const LIST_FIELDS = 'number';
const PR_FIELDS = 'number,title,url,author,isDraft,createdAt,state,labels,body,headRefName,files,commits,comments,mergeable,statusCheckRollup';
const AREA_COLOR = '0969DA';
const STATUS_COLOR = {active: '1F883D', paused: 'BF8700', stale: '6E7781'};

export class TaskError extends Error {
  constructor(message, {code = 1, details} = {}) {
    super(message);
    this.code = code;
    this.details = details;
  }
}

const executable = async path => {
  try { await access(path, constants.X_OK); return true; } catch { return false; }
};

export async function findGh(env = process.env) {
  if (env.Z_GH && await executable(env.Z_GH)) return env.Z_GH;
  const dirs = (env.PATH || '').split(delimiter).filter(Boolean);
  for (const dir of [...dirs, '/opt/homebrew/bin', '/usr/local/bin', join(homedir(), '.local/bin')]) {
    if (await executable(join(dir, 'gh'))) return join(dir, 'gh');
  }
  return '';
}

export function exec(command, args, {cwd = ROOT, input, env = {}, allowFail = false} = {}) {
  return new Promise((resolvePromise, reject) => {
    const child = spawn(command, args, {cwd, env: {...process.env, GIT_TERMINAL_PROMPT: '0', GH_PROMPT_DISABLED: '1', ...env}});
    let stdout = '';
    let stderr = '';
    child.stdout.on('data', chunk => { stdout += chunk; });
    child.stderr.on('data', chunk => { stderr += chunk; });
    child.on('error', error => reject(new TaskError(`无法运行 ${command}：${error.message}`)));
    child.on('close', code => {
      const result = {code, stdout, stderr};
      if (code && !allowFail) reject(new TaskError(`${[command.split('/').pop(), ...args.slice(0, 2)].join(' ')} 失败：${(stderr || stdout).trim()}`, {details: result}));
      else resolvePromise(result);
    });
    child.stdin.end(input || '');
  });
}

export const git = async (args, options) => (await exec('git', args, options)).stdout.trim();
let ghPath = '';
export async function gh(args, options) {
  ghPath ||= await findGh();
  if (!ghPath) throw new TaskError('没有找到 GitHub CLI（gh）。打开协作面板，按“电脑检查”的提示安装。');
  return (await exec(ghPath, args, options)).stdout.trim();
}
const ghJson = async args => JSON.parse(await gh(args) || 'null');

export const slugify = text => String(text).toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 40);
const stamp = (date = new Date()) => {
  const pad = value => String(value).padStart(2, '0');
  return `${pad(date.getMonth() + 1)}${pad(date.getDate())}-${pad(date.getHours())}${pad(date.getMinutes())}`;
};
const localTime = (iso = new Date().toISOString()) => new Date(iso).toLocaleString('zh-CN', {hour12: false, timeZone: 'Asia/Shanghai'});

export async function repoName() {
  if (process.env.Z_REPO) return process.env.Z_REPO;
  const url = await git(['remote', 'get-url', 'origin']);
  const name = url.replace(/^https:\/\/github\.com\//, '').replace(/^git@github\.com:/, '').replace(/^ssh:\/\/git@github\.com\//, '').replace(/\.git$/, '').replace(/\/$/, '');
  if (!/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(name)) throw new TaskError('无法从 origin 识别 GitHub 仓库。');
  return name;
}

export async function loadConfig() {
  return core.assertConfig(JSON.parse(await readFile(join(ROOT, 'tools/team/areas.json'), 'utf8')));
}

let me = null;
export async function currentUser() {
  me ||= await ghJson(['api', 'user']);
  return me;
}

const names = new Map();
async function displayName(login) {
  if (!login) return '';
  if (!names.has(login)) {
    try { names.set(login, (await ghJson(['api', `users/${login}`]))?.name || login); }
    catch { names.set(login, login); }
  }
  return names.get(login);
}

export function toTask(config, raw, now = new Date().toISOString()) {
  const meta = core.parseTaskBody(raw.body || '');
  const owner = meta?.owner || raw.author?.login;
  const pr = {state: 'open', created_at: raw.createdAt, user: {login: raw.author?.login}};
  const commits = (raw.commits || []).map(commit => ({
    message: commit.messageHeadline,
    author: {login: commit.authors?.[0]?.login},
    commit: {author: {name: commit.authors?.[0]?.name}, committer: {date: commit.committedDate || commit.authoredDate}},
  }));
  const comments = (raw.comments || []).map(comment => ({body: comment.body, created_at: comment.createdAt, user: {login: comment.author?.login}}));
  const files = (raw.files || []).map(file => file.path);
  const labels = (raw.labels || []).map(label => label.name);
  const known = new Set(config.areas.map(area => area.id));
  let areas = [...new Set([...(meta?.areas || []), ...core.areaIdsFromLabels(labels)])].filter(id => known.has(id));
  if (!areas.length) areas = [...new Set(files.flatMap(path => core.areasForFile(config, path)))];
  const lastActivity = core.latestActivity({pr, commits, comments, owner});
  const ready = (raw.statusCheckRollup || []).find(item => (item.context || item.name) === 'Project Z / Ready');
  return {
    number: raw.number, title: raw.title, url: raw.url, owner, ownerName: owner, goal: meta?.goal || raw.title,
    branch: raw.headRefName, draft: raw.isDraft, created_at: raw.createdAt, startedAt: meta?.startedAt || raw.createdAt,
    lastActivity, status: core.computeStatus({pr, lastActivity, now, thresholds: config.thresholds}), areas, files, labels,
    tracked: Boolean(meta), handoff: core.handoffText(raw.body || ''), body: raw.body || '', mergeable: raw.mergeable,
    check: ready ? {state: ready.state || ready.conclusion, description: ready.description || ''} : null,
    recentCommits: commits.slice(-5).reverse().map(commit => ({message: commit.message, date: commit.commit.committer.date, author: commit.author.login || commit.commit.author.name})),
  };
}

async function mapLimit(items, limit, work) {
  const results = new Array(items.length);
  let next = 0;
  await Promise.all(Array.from({length: Math.min(limit, items.length)}, async () => {
    while (next < items.length) {
      const index = next++;
      results[index] = await work(items[index], index);
    }
  }));
  return results;
}

export async function board({now = new Date().toISOString(), withNames = true} = {}) {
  const [config, repo, user] = await Promise.all([loadConfig(), repoName(), currentUser()]);
  const numbers = await ghJson(['pr', 'list', '--repo', repo, '--state', 'open', '--base', 'main', '--limit', '100', '--json', LIST_FIELDS]);
  const raws = await mapLimit(numbers, 6, item => ghJson(['pr', 'view', String(item.number), '--repo', repo, '--json', PR_FIELDS]));
  const tasks = raws.map(raw => toTask(config, raw, now)).sort((a, b) => a.number - b.number);
  if (withNames) await Promise.all(tasks.map(async task => { task.ownerName = await displayName(task.owner); }));
  const branch = await git(['branch', '--show-current']);
  const dirty = (await git(['-c', 'core.quotePath=false', 'status', '--porcelain'])).split('\n').filter(Boolean).map(line => line.slice(3));
  const tracked = (await git(['-c', 'core.quotePath=false', 'ls-files'])).split('\n').filter(Boolean);
  const areaStats = Object.fromEntries(config.areas.map(area => {
    const files = tracked.filter(path => core.matchesAny(path, area.paths));
    const hard = files.filter(path => core.isHardToMerge(config, path) && !path.endsWith('.meta'));
    return [area.id, {files: files.length, hard: hard.length, hardTypes: [...new Set(hard.map(path => path.includes('ProjectSettings/') ? 'ProjectSettings' : path.split('.').pop().toLowerCase()))].sort()}];
  }));
  return {repo, me: {login: user.login, name: user.name || user.login}, config, tasks, overlaps: core.findOverlaps(config, tasks),
    local: {branch, dirty}, current: tasks.find(task => task.branch === branch) || null, areaStats, now};
}

async function cleanTree() {
  if (await git(['status', '--porcelain'])) throw new TaskError('当前目录还有未保存的修改。先点“保存进度”，或让 agent 运行 tools/team/z save。');
}

async function ensureLabel(repo, name, color, description) {
  await gh(['label', 'create', name, '--repo', repo, '--color', color, '--description', description, '--force']);
}

async function withTempFile(text, callback) {
  const dir = await mkdtemp(join(tmpdir(), 'project-z-'));
  const file = join(dir, 'body.md');
  try {
    await writeFile(file, text);
    return await callback(file);
  } finally {
    await rm(dir, {recursive: true, force: true});
  }
}

async function prForBranch(repo, branch) {
  try { return await ghJson(['pr', 'view', branch, '--repo', repo, '--json', 'number,state,isDraft,body,url,author']); }
  catch { return null; }
}

async function confirmationComment(repo, number, warnings, confirmed, stage) {
  const relevant = warnings.filter(item => item.level === 'red' && confirmed.includes(item.owner));
  if (!number || !relevant.length) return;
  const lines = relevant.map(item => `- @${item.owner}（#${item.number}）${item.hardFiles.length ? `：${item.hardFiles.map(path => `\`${path}\``).join('、')}` : ''}`).join('\n');
  await gh(['pr', 'comment', String(number), '--repo', repo, '--body', `${stage}前已确认难合并文件：\n${lines}`]);
}

export async function startTask({goal, areaIds, slug = '', confirmed = [], handoff = '', now = new Date()} = {}) {
  goal = String(goal || '').trim();
  if (!goal) throw new TaskError('请写一句开发目标。');
  if (goal.length > 200) throw new TaskError('开发目标请控制在 200 字以内。');
  const [config, repo, user] = await Promise.all([loadConfig(), repoName(), currentUser()]);
  const areas = core.selectedAreas(config, areaIds);
  await cleanTree();
  const current = await board({withNames: true});
  const warnings = core.startWarnings(config, current.tasks, areas.map(area => area.id), user.login);
  const missing = core.missingConfirmations(warnings, confirmed);
  if (missing.length) {
    throw new TaskError(`这些区域里有难合并文件正在被修改。请先和 ${missing.map(login => `@${login}`).join('、')} 确认，再勾选确认后开工。`, {code: 4, details: {warnings, missing}});
  }
  const branch = `task/${slugify(user.login) || 'member'}/${slugify(slug) || `${areas.map(area => area.id).join('-')}-${stamp(now)}`}`;
  await git(['fetch', 'origin', 'main']);
  if ((await git(['branch', '--list', branch])) || (await git(['ls-remote', '--heads', 'origin', branch]))) {
    throw new TaskError(`分支 ${branch} 已存在，请换一个任务短名。`);
  }
  await git(['switch', '--no-track', '-c', branch, 'origin/main']);
  await git(['commit', '--allow-empty', '-m', `开工：${goal}`]);
  await git(['push', '-u', 'origin', 'HEAD']);
  for (const area of areas) await ensureLabel(repo, core.areaLabel(area.id), AREA_COLOR, `协作区域：${area.name}`);
  await ensureLabel(repo, core.STATUS_LABELS.active, STATUS_COLOR.active, '由协作机器人根据 GitHub 活动更新');
  const startedAt = now.toISOString();
  const body = core.taskBody({goal, areas, owner: user.login, startedAt, handoff});
  const title = goal.length > 72 ? `${goal.slice(0, 71)}…` : goal;
  const labelArgs = [...areas.map(area => core.areaLabel(area.id)), core.STATUS_LABELS.active].flatMap(label => ['--label', label]);
  const url = await withTempFile(body, file => gh(['pr', 'create', '--repo', repo, '--draft', '--base', 'main', '--head', branch, '--title', title, '--body-file', file, ...labelArgs]));
  const number = Number(url.match(/\/pull\/(\d+)/)?.[1]);
  await confirmationComment(repo, number, warnings, confirmed, '开工');
  return {branch, url, number, goal, areas: areas.map(area => area.id), warnings};
}

export async function saveTask({message = '', handoff = ''} = {}) {
  const [repo, user] = await Promise.all([repoName(), currentUser()]);
  const branch = await git(['branch', '--show-current']);
  if (!branch.startsWith('task/')) throw new TaskError(`当前在 ${branch || '未命名分支'}，不是任务分支。请先开工或切换到自己的任务。`);
  let committed = false;
  if (await git(['status', '--porcelain'])) {
    await git(['add', '-A']);
    const check = await exec(process.execPath, [join(ROOT, 'tools/team/project-check.mjs')], {allowFail: true});
    if (check.code) {
      await git(['reset', '-q']);
      throw new TaskError(`保存前检查未通过，修改仍在电脑上：\n${(check.stderr || check.stdout).trim()}`);
    }
    await git(['commit', '-m', message.trim() || `保存进度：${localTime()}`]);
    committed = true;
  }
  const push = await exec('git', ['push', '-u', 'origin', 'HEAD'], {allowFail: true});
  if (push.code) {
    if (!/non-fast-forward|fetch first|rejected/.test(push.stderr)) throw new TaskError(`推送失败：${push.stderr.trim()}`);
    const pull = await exec('git', ['pull', '--no-rebase', '--no-edit', 'origin', branch], {allowFail: true});
    if (pull.code) {
      const files = await git(['diff', '--name-only', '--diff-filter=U']);
      throw new TaskError(`远端分支有别人推送的新内容，合并时出现冲突：\n${files}\n请复制“修复冲突”提示词交给 agent。`, {code: 3});
    }
    await git(['push', '-u', 'origin', 'HEAD']);
  }
  const pr = await prForBranch(repo, branch);
  if (pr?.state === 'OPEN' && !pr.isDraft) await gh(['pr', 'ready', String(pr.number), '--repo', repo, '--undo']);
  if (pr?.state === 'OPEN' && handoff.trim()) {
    const text = `${handoff.trim()}\n\n（@${user.login} 更新于 ${localTime()}）`;
    await withTempFile(core.replaceHandoff(pr.body || '', text), file => gh(['pr', 'edit', String(pr.number), '--repo', repo, '--body-file', file]));
  }
  return {branch, committed, url: pr?.url || '', number: pr?.number || null};
}

async function taskByNumber(number) {
  const data = await board({withNames: false});
  const task = data.tasks.find(item => item.number === Number(number));
  if (!task) throw new TaskError(`没有找到未合并的 PR #${number}。`);
  return {data, task};
}

async function currentNumber() {
  const repo = await repoName();
  const pr = await prForBranch(repo, await git(['branch', '--show-current']));
  if (pr?.state !== 'OPEN') throw new TaskError('当前分支没有打开的任务 PR。');
  return pr.number;
}

export async function stillWorking({number} = {}) {
  const [repo, user] = await Promise.all([repoName(), currentUser()]);
  number ||= await currentNumber();
  const {task} = await taskByNumber(number);
  if (task.owner !== user.login) throw new TaskError(`只有负责人 @${task.owner} 可以点“还在做”。已搁置的任务可以用“接手”。`);
  await gh(['pr', 'comment', String(number), '--repo', repo, '--body', `${core.MARKERS.still}\n负责人 @${user.login} 还在做这个任务（${localTime()}）。`]);
  return {number, url: task.url};
}

export async function takeoverTask({number, mode = 'continue', note = ''} = {}) {
  const [config, repo, user] = await Promise.all([loadConfig(), repoName(), currentUser()]);
  const {task} = await taskByNumber(number);
  if (task.status !== core.STATUS.stale) throw new TaskError(`#${number} 还没有搁置（当前：${task.status}），请先联系 @${task.owner}。`);
  if (task.owner === user.login) throw new TaskError('这是你自己的任务，直接继续即可。');
  await cleanTree();
  const now = new Date().toISOString();
  const handoffLine = `@${user.login} 于 ${localTime(now)} 接手（原负责人 @${task.owner}）。${note.trim() ? `接手说明：${note.trim()}` : ''}`;
  if (mode === 'fresh') {
    const result = await startTask({goal: `接手 #${number}：${task.goal}`.slice(0, 200), areaIds: task.areas.slice(0, 2), slug: `takeover-${number}-${stamp()}`,
      handoff: `${handoffLine}\n\n从最新 main 重新开始；原任务 #${number} 和分支 \`${task.branch}\` 保留。`});
    await gh(['pr', 'comment', String(number), '--repo', repo, '--body', `@${user.login} 已从最新 main 重新开始这个任务：#${result.number}。本 PR 和分支保留。`]);
    return {...result, mode, from: number};
  }
  if (mode !== 'continue') throw new TaskError('接手方式只能是 continue 或 fresh。');
  await git(['fetch', 'origin', task.branch]);
  if (await git(['branch', '--list', task.branch])) {
    await git(['switch', task.branch]);
    await git(['merge', '--ff-only', `origin/${task.branch}`]);
  } else {
    await git(['switch', '-c', task.branch, '--track', `origin/${task.branch}`]);
  }
  let body = core.replaceTaskMeta(task.body, {owner: user.login, previousOwner: task.owner, takenOverAt: now});
  const previous = task.handoff ? `${task.handoff}\n\n` : '';
  body = core.replaceHandoff(body, `${previous}${handoffLine}`);
  body = body.replace(/## 负责人\s*\n\s*@[^\n]+/, `## 负责人\n\n@${user.login} · 接手于 ${now}（原负责人 @${task.owner}）`);
  await withTempFile(body, file => gh(['pr', 'edit', String(number), '--repo', repo, '--body-file', file]));
  await gh(['pr', 'comment', String(number), '--repo', repo, '--body', `${core.MARKERS.still}\n@${user.login} 接手了这个任务（原负责人 @${task.owner}）。${note.trim() ? `\n\n接手说明：${note.trim()}` : ''}`]);
  return {mode, number, branch: task.branch, url: task.url, areas: task.areas.filter(id => config.areas.some(area => area.id === id))};
}

export async function shipCheck({confirmed = [], post = false} = {}) {
  const [config, repo, user] = await Promise.all([loadConfig(), repoName(), currentUser()]);
  const branch = await git(['branch', '--show-current']);
  await git(['fetch', 'origin', 'main']);
  const committed = (await git(['-c', 'core.quotePath=false', 'diff', '--name-only', 'origin/main...HEAD'])).split('\n').filter(Boolean);
  const working = (await git(['-c', 'core.quotePath=false', 'status', '--porcelain'])).split('\n').filter(Boolean).map(line => line.slice(3).split(' -> ').pop());
  const data = await board({withNames: true});
  const mine = data.tasks.find(task => task.branch === branch);
  const self = {number: mine?.number || 0, files: [...new Set([...committed, ...working])], owner: user.login, draft: false, created_at: new Date().toISOString()};
  const others = data.tasks.filter(task => task.branch !== branch);
  const overlaps = core.findOverlaps(config, [self, ...others]).filter(item => item.pulls.includes(self.number));
  const warnings = overlaps.map(item => {
    const other = others.find(task => task.number === item.pulls.find(value => value !== self.number));
    return {level: item.level, number: other.number, owner: other.owner, ownerName: other.ownerName, files: item.files, hardFiles: item.hardFiles};
  });
  const missing = core.missingConfirmations(warnings, confirmed);
  if (post && !missing.length) await confirmationComment(repo, mine?.number, warnings, confirmed, '交付');
  return {branch, number: mine?.number || null, files: self.files, warnings, missing};
}

export function prompt(kind, {config, task, overlaps = [], tasks = []}) {
  const areas = config.areas.filter(area => task.areas.includes(area.id));
  const related = overlaps.filter(item => item.pulls.includes(task.number));
  const others = related.map(item => tasks.find(value => value.number === item.pulls.find(number => number !== task.number))).filter(Boolean);
  return core.codexPrompt(kind, {goal: task.goal, areas, branch: task.branch, number: task.number, url: task.url, others,
    hardFiles: [...new Set(related.flatMap(item => item.hardFiles))]});
}

function parseArgs(argv) {
  const options = {_: [], area: [], confirmed: []};
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (!arg.startsWith('--')) { options._.push(arg); continue; }
    const key = arg.slice(2);
    if (['json', 'post'].includes(key)) { options[key] = true; continue; }
    const value = argv[++i];
    if (value === undefined) throw new TaskError(`${arg} 需要一个值`);
    if (key === 'area') options.area.push(...value.split(',').filter(Boolean));
    else if (key === 'confirmed') options.confirmed.push(...value.split(',').map(item => item.replace(/^@/, '')).filter(Boolean));
    else options[key] = value;
  }
  return options;
}

function print(result, json, text) {
  if (json) process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
  else process.stdout.write(`${text}\n`);
}

async function main(argv) {
  const [command, ...rest] = argv;
  const options = parseArgs(rest);
  if (command === 'start') {
    if (!options.goal || !options.area.length) throw new TaskError('用法：tools/team/z start <英文短名> --goal "一句目标" --area <区域ID> [--area <区域ID>]\n区域见 tools/team/areas.json；也可以用协作面板开工。');
    const result = await startTask({goal: options.goal, areaIds: options.area, slug: options._[0] || '', confirmed: options.confirmed});
    print(result, options.json, `✓ 已开工：${result.branch}\n✓ 草稿 PR：${result.url}\n${result.warnings.length ? `提醒：${result.warnings.map(item => `@${item.owner} 正在改 #${item.number}`).join('；')}` : ''}`);
  } else if (command === 'save') {
    const result = await saveTask({message: options.message || '', handoff: options.handoff || ''});
    print(result, options.json, `${result.committed ? '✓ 已提交修改' : '没有新的本地修改'}\n✓ 已推送到 ${result.branch}${result.url ? `\nPR 保持草稿：${result.url}` : ''}`);
  } else if (command === 'still') {
    const result = await stillWorking({number: options.pr ? Number(options.pr) : undefined});
    print(result, options.json, `✓ 已更新最近活动时间：${result.url}`);
  } else if (command === 'takeover') {
    if (!options.pr) throw new TaskError('用法：tools/team/z takeover --pr <编号> [--mode continue|fresh] [--note "接手说明"]');
    const result = await takeoverTask({number: Number(options.pr), mode: options.mode || 'continue', note: options.note || ''});
    print(result, options.json, `✓ 已接手：${result.branch}\n${result.url}`);
  } else if (command === 'ship-check') {
    const result = await shipCheck({confirmed: options.confirmed, post: options.post});
    for (const item of result.warnings) {
      process.stderr.write(`${item.level === 'red' ? '🔴 难合并文件重叠' : '🟡 文件重叠'}：#${item.number}（@${item.owner}）${item.files.join('、')}\n`);
    }
    if (result.missing.length) {
      throw new TaskError(`交付前请先和 ${result.missing.map(login => `@${login}`).join('、')} 确认难合并文件。确认后运行：tools/team/z ship ${result.missing.map(login => `--confirmed ${login}`).join(' ')}`, {code: 4, details: result});
    }
    if (options.json) print(result, true, '');
  } else if (command === 'board') {
    const result = await board();
    print({...result, config: undefined}, true, '');
  } else {
    throw new TaskError(`未知命令：${command || ''}`);
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main(process.argv.slice(2)).catch(error => {
    process.stderr.write(`✗ ${error.message}\n`);
    if (process.argv.includes('--json') && error.details) process.stdout.write(`${JSON.stringify({error: error.message, ...error.details}, null, 2)}\n`);
    process.exit(error.code || 1);
  });
}
