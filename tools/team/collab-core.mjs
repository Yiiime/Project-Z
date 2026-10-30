// Shared rules for the local panel and GitHub automation. Pure functions only; no network or file access.
export const STATUS = Object.freeze({active: 'active', paused: 'paused', stale: 'stale', done: 'done'});
export const STATUS_LABELS = Object.freeze({active: 'z:开发中', paused: 'z:可能暂停', stale: 'z:已搁置'});
export const OVERLAP_LABELS = Object.freeze({yellow: 'z:文件重叠', red: 'z:难合并重叠'});
export const AREA_LABEL_PREFIX = 'area:';
export const MARKERS = Object.freeze({
  task: 'project-z-task',
  still: '<!-- project-z:still-working -->',
  pausedPing: 'project-z:paused-ping',
  scope: 'project-z:scope',
  conflict: 'project-z:hard-overlap',
});
export const DAY_MS = 24 * 60 * 60 * 1000;
const ID = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

export function globToRegExp(pattern) {
  if (typeof pattern !== 'string' || !pattern || pattern.startsWith('/') || pattern.includes('\\')) {
    throw new Error(`无效路径规则：${pattern}`);
  }
  let source = '^';
  for (let i = 0; i < pattern.length; i++) {
    const char = pattern[i];
    if (char === '*') {
      if (pattern[i + 1] === '*') {
        if (pattern[i + 2] === '/') { source += '(?:.*/)?'; i += 2; }
        else { source += '.*'; i += 1; }
      } else source += '[^/]*';
    } else if (char === '?') source += '[^/]';
    else source += char.replace(/[|{}()[\]^$+.]/g, '\\$&');
  }
  return new RegExp(`${source}$`);
}

export const matchesAny = (path, patterns) => patterns.some(pattern => globToRegExp(pattern).test(path));

export function validateConfig(config) {
  const errors = [];
  const fail = message => errors.push(message);
  if (!config || typeof config !== 'object') return ['配置必须是 JSON 对象'];
  if (config.version !== 1) fail('version 必须是 1');
  const paused = config.thresholds?.pausedAfterDays;
  const stale = config.thresholds?.staleAfterDays;
  if (!Number.isFinite(paused) || paused <= 0) fail('thresholds.pausedAfterDays 必须是正数');
  if (!Number.isFinite(stale) || stale <= paused) fail('thresholds.staleAfterDays 必须大于 pausedAfterDays');
  if (!Array.isArray(config.hardToMergePatterns) || !config.hardToMergePatterns.length) fail('hardToMergePatterns 至少需要一条规则');
  for (const pattern of config.hardToMergePatterns || []) {
    try { globToRegExp(pattern); } catch (error) { fail(`hardToMergePatterns：${error.message}`); }
  }
  if (!Array.isArray(config.areas) || !config.areas.length) fail('areas 至少需要一个区域');
  const ids = new Set();
  const names = new Set();
  for (const [index, area] of (config.areas || []).entries()) {
    const where = `areas[${index}]`;
    if (!ID.test(area?.id || '')) fail(`${where}.id 只能用小写英文、数字和连字符`);
    if (ids.has(area?.id)) fail(`${where}.id 重复：${area.id}`);
    ids.add(area?.id);
    if (typeof area?.name !== 'string' || !area.name.trim()) fail(`${where}.name 不能为空`);
    if (names.has(area?.name)) fail(`${where}.name 重复：${area.name}`);
    names.add(area?.name);
    if (typeof area?.description !== 'string' || !area.description.trim()) fail(`${where}.description 不能为空`);
    if (!Array.isArray(area?.paths) || !area.paths.length) fail(`${where}.paths 至少需要一条路径`);
    for (const pattern of area?.paths || []) {
      try { globToRegExp(pattern); } catch (error) { fail(`${where}.paths：${error.message}`); }
    }
  }
  return errors;
}

export function assertConfig(config) {
  const errors = validateConfig(config);
  if (errors.length) throw new Error(`tools/team/areas.json 有误：\n- ${errors.join('\n- ')}`);
  return config;
}

export function selectedAreas(config, ids) {
  const unique = [...new Set(ids)];
  if (unique.length < 1 || unique.length > 2) throw new Error('每个任务选择 1～2 个区域');
  return unique.map(id => {
    const area = config.areas.find(item => item.id === id);
    if (!area) throw new Error(`未知区域：${id}`);
    return area;
  });
}

export const areaLabel = id => `${AREA_LABEL_PREFIX}${id}`;
export const areaIdsFromLabels = labels => labels
  .map(label => typeof label === 'string' ? label : label.name)
  .filter(name => name?.startsWith(AREA_LABEL_PREFIX))
  .map(name => name.slice(AREA_LABEL_PREFIX.length));

export function areasForFile(config, path) {
  return config.areas.filter(area => matchesAny(path, area.paths)).map(area => area.id);
}

export const isHardToMerge = (config, path) => matchesAny(path, config.hardToMergePatterns);

export function parseTaskBody(body = '') {
  const match = body.match(/<!--\s*project-z-task\s+({[\s\S]*?})\s*-->/);
  if (!match) return null;
  try {
    const data = JSON.parse(match[1]);
    return Array.isArray(data.areas) ? data : null;
  } catch {
    return null;
  }
}

export function taskBody({goal, areas, owner, startedAt, handoff = ''}) {
  const meta = JSON.stringify({version: 1, goal, areas: areas.map(area => area.id), owner, startedAt});
  const areaLines = areas.map(area => `- **${area.name}**（\`${area.id}\`）：${area.paths.map(path => `\`${path}\``).join('、')}`).join('\n');
  return `<!-- ${MARKERS.task} ${meta} -->
## 开发目标

${goal}

## 区域

${areaLines}

## 负责人

@${owner} · 开工于 ${startedAt}

## 进度与交接

${handoff || '开发中。暂停前运行“保存进度”，并在这里写清已完成、未完成和下一步。'}

---
草稿 PR 不会自动合并。交付时由协作面板或 \`tools/team/z ship\` 转为可合并。`;
}

const BOT_LOGINS = new Set(['github-actions[bot]', 'web-flow']);
const isBotCommit = commit => BOT_LOGINS.has(commit.author?.login) || commit.commit?.author?.name === 'github-actions[bot]';

export function latestActivity({pr, commits = [], comments = [], owner = pr.user?.login}) {
  const times = [Date.parse(pr.created_at)];
  for (const commit of commits) {
    if (isBotCommit(commit)) continue;
    const date = commit.commit?.committer?.date || commit.commit?.author?.date;
    if (date) times.push(Date.parse(date));
  }
  for (const comment of comments) {
    if (comment.body?.includes(MARKERS.still) && comment.user?.login === owner) times.push(Date.parse(comment.created_at));
  }
  return new Date(Math.max(...times.filter(Number.isFinite))).toISOString();
}

export function computeStatus({pr, lastActivity, now, thresholds}) {
  if (pr.state !== 'open' || pr.merged_at || pr.closed_at) return STATUS.done;
  const idleDays = (Date.parse(now) - Date.parse(lastActivity)) / DAY_MS;
  if (idleDays < thresholds.pausedAfterDays) return STATUS.active;
  if (idleDays <= thresholds.staleAfterDays) return STATUS.paused;
  return STATUS.stale;
}

export function overlapLevel(config, files) {
  return files.some(path => isHardToMerge(config, path)) ? 'red' : files.length ? 'yellow' : 'none';
}

export function findOverlaps(config, pulls) {
  const results = [];
  for (let i = 0; i < pulls.length; i++) {
    for (let j = i + 1; j < pulls.length; j++) {
      const left = pulls[i];
      const right = pulls[j];
      const rightFiles = new Set(right.files);
      const files = [...new Set(left.files.filter(path => rightFiles.has(path)))].sort();
      const level = overlapLevel(config, files);
      if (level !== 'none') {
        results.push({level, files, hardFiles: files.filter(path => isHardToMerge(config, path)), pulls: [left.number, right.number]});
      }
    }
  }
  return results;
}

export function mergeOrder(left, right) {
  if (left.draft !== right.draft) return left.draft ? [right, left] : [left, right];
  return Date.parse(left.created_at) <= Date.parse(right.created_at) ? [left, right] : [right, left];
}

export function outOfScopeFiles(config, selectedIds, files) {
  const selected = new Set(selectedIds);
  const missing = new Set();
  const unmatched = [];
  for (const path of files) {
    const ids = areasForFile(config, path);
    if (!ids.length) unmatched.push(path);
    else if (!ids.some(id => selected.has(id))) ids.forEach(id => missing.add(id));
  }
  return {areaIds: [...missing].sort(), unmatched: unmatched.sort()};
}

export function relativeTime(iso, now = new Date().toISOString()) {
  const minutes = Math.max(0, Math.round((Date.parse(now) - Date.parse(iso)) / 60000));
  if (minutes < 1) return '刚刚';
  if (minutes < 60) return `${minutes} 分钟前`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} 小时前`;
  return `${Math.round(hours / 24)} 天前`;
}

export function filesInAreas(config, areaIds, files) {
  const patterns = config.areas.filter(area => areaIds.includes(area.id)).flatMap(area => area.paths);
  return files.filter(path => matchesAny(path, patterns));
}

// Before starting: active or paused tasks in the chosen areas. Red when they already changed hard files there.
export function startWarnings(config, tasks, areaIds, me) {
  return tasks
    .filter(task => task.owner !== me && [STATUS.active, STATUS.paused].includes(task.status))
    .filter(task => task.areas.some(id => areaIds.includes(id)))
    .map(task => {
      const hardFiles = filesInAreas(config, areaIds, task.files).filter(path => isHardToMerge(config, path));
      return {level: hardFiles.length ? 'red' : 'yellow', number: task.number, owner: task.owner, ownerName: task.ownerName || task.owner,
        status: task.status, lastActivity: task.lastActivity, hardFiles};
    });
}

// Before delivering: hard-file overlaps between this PR and other open PRs.
export function shipWarnings(overlaps, tasks, number) {
  return overlaps.filter(item => item.level === 'red' && item.pulls.includes(number)).map(item => {
    const other = tasks.find(task => task.number === item.pulls.find(value => value !== number));
    return {level: 'red', number: other?.number, owner: other?.owner, ownerName: other?.ownerName || other?.owner, hardFiles: item.hardFiles};
  });
}

export function missingConfirmations(warnings, confirmed = []) {
  const done = new Set(confirmed);
  return [...new Set(warnings.filter(item => item.level === 'red').map(item => item.owner))].filter(owner => !done.has(owner));
}

export function handoffText(body = '') {
  const match = body.match(/## 进度与交接\s*\n([\s\S]*?)(?=\n## |\n---|$)/);
  return match ? match[1].trim() : '';
}

export function replaceHandoff(body, text) {
  if (/## 进度与交接\s*\n/.test(body)) return body.replace(/(## 进度与交接\s*\n)([\s\S]*?)(?=\n## |\n---|$)/, `$1\n${text.trim()}\n`);
  return `${body.trimEnd()}\n\n## 进度与交接\n\n${text.trim()}\n`;
}

export function replaceTaskMeta(body, changes) {
  const meta = parseTaskBody(body);
  if (!meta) return body;
  const next = JSON.stringify({...meta, ...changes});
  return body.replace(/<!--\s*project-z-task\s+{[\s\S]*?}\s*-->/, `<!-- ${MARKERS.task} ${next} -->`);
}

function scopeText(areas) {
  return areas.map(area => `- ${area.name}：${area.paths.join('、')}`).join('\n');
}

export function codexPrompt(kind, {goal, areas, branch, number, url, others = [], hardFiles = []}) {
  const scope = scopeText(areas);
  const rule = `只修改下面这些路径：\n${scope}\n需要改其他路径时，先停下来问我，说明原因和要改的文件。`;
  const pr = number ? `PR #${number}${url ? `（${url}）` : ''}` : '当前任务的 PR';
  const people = others.length ? others.map(item => `@${item.owner}（PR #${item.number}）`).join('、') : '对方';
  const texts = {
    start: `我在 Z 项目协作面板开工了：分支 ${branch || '当前任务分支'}，草稿 ${pr}。\n目标：${goal}\n${rule}\n请先读 AGENTS.md，复述你的计划和验收方式，等我确认后再开发。开发中可以分多次提交；我说暂停时运行 tools/team/z save --message "<这次做了什么>"。`,
    save: `先保存进度：检查 git diff，确认只改了所选区域：\n${scope}\n发现超出范围的修改时先停下来问我。确认后运行 tools/team/z save --message "<一句话说明这次做了什么>" --handoff "<已完成、未完成、下一步>"。PR 保持草稿，不要交付。`,
    ship: `完成并交付 ${pr}。目标：${goal}\n先检查 git diff，确认只改了所选区域：\n${scope}\n发现超出范围的修改时先停下来问我。涉及画面、操作或流程时在 Unity 试玩并说明结果。然后运行 tools/team/z ship，告诉我 PR 链接、检查结果和未验证部分。`,
    conflict: `${pr} 和 ${people} 有冲突或改到了同一文件。运行 tools/team/z sync，读懂双方改动后再处理。\n${hardFiles.length ? `难合并文件：${hardFiles.join('、')}。这些文件不要逐行硬合并；先告诉我需要和谁确认，再按确认结果在 Unity 或 Blender 中重做。\n` : ''}${rule}\n解决后运行 tools/team/z check，再运行 tools/team/z save。`,
    checks: `${pr} 的自动检查没有通过。运行 gh pr checks ${number || ''} 和 gh run view --log-failed 查看原因，在当前任务分支修复。\n${rule}\n修复后运行 tools/team/z check；通过后运行 tools/team/z ship。`,
  };
  if (!texts[kind]) throw new Error(`未知提示词类型：${kind}`);
  return texts[kind];
}
