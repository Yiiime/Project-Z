// GitHub Actions entry points for status, labels and overlap reminders.
// These functions only call the GitHub API. They never check out, install or execute pull request code.
import {readFile} from 'node:fs/promises';
import {
  AREA_LABEL_PREFIX, MARKERS, OVERLAP_LABELS, STATUS, STATUS_LABELS, areaIdsFromLabels, areaLabel, assertConfig, computeStatus,
  findOverlaps, latestActivity, mergeOrder, outOfScopeFiles, parseTaskBody,
} from './collab-core.mjs';

const COLORS = Object.freeze({active: '1F883D', paused: 'BF8700', stale: '6E7781', area: '0969DA', yellow: 'D4A72C', red: 'CF222E'});

export async function loadConfig(path = new URL('./areas.json', import.meta.url)) {
  return assertConfig(JSON.parse(await readFile(path, 'utf8')));
}

function api({github, context, core}) {
  const repo = context.repo;
  const data = async request => (await request).data;
  return {
    repo,
    openPulls: () => github.paginate(github.rest.pulls.list, {...repo, state: 'open', base: 'main', per_page: 100}),
    recentClosedPulls: async () => (await data(github.rest.pulls.list({...repo, state: 'closed', base: 'main', sort: 'updated', direction: 'desc', per_page: 30}))),
    files: number => github.paginate(github.rest.pulls.listFiles, {...repo, pull_number: number, per_page: 100}),
    commits: number => github.paginate(github.rest.pulls.listCommits, {...repo, pull_number: number, per_page: 100}),
    comments: number => github.paginate(github.rest.issues.listComments, {...repo, issue_number: number, per_page: 100}),
    async ensureLabel(name, color, description) {
      try { await github.rest.issues.getLabel({...repo, name}); }
      catch (error) {
        if (error.status !== 404) throw error;
        try { await github.rest.issues.createLabel({...repo, name, color, description}); }
        catch (createError) { if (createError.status !== 422) throw createError; }
      }
    },
    addLabels: (number, labels) => labels.length ? github.rest.issues.addLabels({...repo, issue_number: number, labels}) : null,
    async removeLabel(number, name) {
      try { await github.rest.issues.removeLabel({...repo, issue_number: number, name}); }
      catch (error) { if (error.status !== 404) throw error; }
    },
    async commentOnce(number, marker, body, comments) {
      const existing = comments || await data(github.rest.issues.listComments({...repo, issue_number: number, per_page: 100}));
      if (existing.some(comment => comment.body?.includes(marker))) return false;
      await github.rest.issues.createComment({...repo, issue_number: number, body: `<!-- ${marker} -->\n${body}`});
      return true;
    },
    warn: message => core.warning(message),
    info: message => core.info(message),
  };
}

async function collectPull(client, pr) {
  const [files, commits, comments] = await Promise.all([client.files(pr.number), client.commits(pr.number), client.comments(pr.number)]);
  const meta = parseTaskBody(pr.body || '');
  const labels = pr.labels.map(label => label.name);
  return {
    pr, comments, labels, meta,
    number: pr.number,
    owner: meta?.owner || pr.user.login,
    draft: pr.draft,
    created_at: pr.created_at,
    files: files.flatMap(file => [file.filename, file.previous_filename].filter(Boolean)),
    areas: [...new Set([...(meta?.areas || []), ...areaIdsFromLabels(labels)])],
    lastActivity: latestActivity({pr, commits, comments, owner: meta?.owner || pr.user.login}),
  };
}

async function syncStatus(client, config, item, now) {
  const status = computeStatus({pr: item.pr, lastActivity: item.lastActivity, now, thresholds: config.thresholds});
  const wanted = STATUS_LABELS[status];
  for (const [key, label] of Object.entries(STATUS_LABELS)) {
    if (label !== wanted && item.labels.includes(label)) await client.removeLabel(item.number, label);
    if (label === wanted && !item.labels.includes(label)) {
      await client.ensureLabel(label, COLORS[key], key === 'stale' ? '超过阈值没有活动，可以接手' : '由协作机器人根据 GitHub 活动更新');
      await client.addLabels(item.number, [label]);
    }
  }
  if (status === STATUS.paused) {
    const marker = `${MARKERS.pausedPing}:${item.lastActivity}`;
    await client.commentOnce(item.number, marker,
      `@${item.owner} 这个任务已经 ${config.thresholds.pausedAfterDays} 天以上没有新提交。还在做的话，在协作面板点 **还在做**；准备暂停的话，先点 **保存进度** 并写好交接说明。超过 ${config.thresholds.staleAfterDays} 天没有活动时，看板会显示为可接手。`,
      item.comments);
  }
  return status;
}

async function syncScope(client, config, item) {
  if (!item.meta) return {areaIds: [], unmatched: []};
  const scope = outOfScopeFiles(config, item.areas, item.files);
  const missingLabels = scope.areaIds.map(areaLabel).filter(label => !item.labels.includes(label));
  for (const id of scope.areaIds) {
    const area = config.areas.find(value => value.id === id);
    await client.ensureLabel(areaLabel(id), COLORS.area, `协作区域：${area.name}`);
  }
  await client.addLabels(item.number, missingLabels);
  if (scope.areaIds.length || scope.unmatched.length) {
    const names = scope.areaIds.map(id => config.areas.find(area => area.id === id).name);
    const key = [...scope.areaIds, ...scope.unmatched].join('|');
    await client.commentOnce(item.number, `${MARKERS.scope}:${key}`,
      `提示：这个 PR 改到了开工时没有选择的范围。${names.length ? `已补充区域标签：${names.join('、')}。` : ''}${scope.unmatched.length ? `另有文件不属于任何区域：${scope.unmatched.map(path => `\`${path}\``).join('、')}。` : ''}检查不会因此失败；请确认这些改动是本任务需要的，必要时告诉相关同学。`,
      item.comments);
  }
  return scope;
}

async function remindHardOverlap(client, items, overlap) {
  const [left, right] = overlap.pulls.map(number => items.find(item => item.number === number));
  const [first, second] = mergeOrder(left, right);
  const files = overlap.hardFiles.map(path => `\`${path}\``).join('、');
  const marker = `${MARKERS.conflict}:${[left.number, right.number].sort((a, b) => a - b).join('-')}`;
  const body = item => {
    const other = item.number === left.number ? right : left;
    return `🔴 难合并文件重叠：这个 PR 和 #${other.number}（@${other.owner}）都改了 ${files}。这些文件很难由 Git 自动合并。\n\n建议先合并 #${first.number}（@${first.owner}）${first.draft === second.draft ? '，因为它开工更早' : '，因为它已经准备交付'}；另一位等它合并后同步 main，再在 Unity 或 Blender 中重做自己的修改。继续开工或交付前，请先和对方确认，并在协作面板勾选“我已和 @${other.owner} 确认”。`;
  };
  await client.commentOnce(left.number, marker, body(left), left.comments);
  await client.commentOnce(right.number, marker, body(right), right.comments);
}

async function syncOverlapLabels(client, item, overlaps) {
  const mine = overlaps.filter(overlap => overlap.pulls.includes(item.number));
  const wanted = mine.some(overlap => overlap.level === 'red') ? 'red' : mine.length ? 'yellow' : null;
  for (const [level, label] of Object.entries(OVERLAP_LABELS)) {
    if (level === wanted && !item.labels.includes(label)) {
      await client.ensureLabel(label, COLORS[level], level === 'red' ? '和其他 PR 改了同一个难合并文件' : '和其他 PR 改了同一个普通文件');
      await client.addLabels(item.number, [label]);
    }
    if (level !== wanted && item.labels.includes(label)) await client.removeLabel(item.number, label);
  }
}

export async function collaborate({github, context, core, now = new Date().toISOString(), config}) {
  const client = api({github, context, core});
  config ||= await loadConfig();
  const pulls = (await client.openPulls()).filter(pr => pr.head.repo?.full_name === `${client.repo.owner}/${client.repo.repo}`);
  const items = [];
  for (const pr of pulls) {
    try { items.push(await collectPull(client, pr)); }
    catch (error) { client.warn(`#${pr.number}: could not read PR (${error.status || error.message})`); }
  }
  const results = [];
  for (const item of items) {
    try {
      const status = item.meta ? await syncStatus(client, config, item, now) : 'untracked';
      const scope = await syncScope(client, config, item);
      results.push({number: item.number, status, addedAreas: scope.areaIds, unmatched: scope.unmatched});
    } catch (error) {
      client.warn(`#${item.number}: collaboration update failed (${error.status || error.message})`);
      results.push({number: item.number, status: 'error'});
    }
  }
  const overlaps = findOverlaps(config, items);
  for (const item of items) {
    try { await syncOverlapLabels(client, item, overlaps); }
    catch (error) { client.warn(`#${item.number}: overlap label failed (${error.status || error.message})`); }
  }
  for (const overlap of overlaps.filter(value => value.level === 'red')) {
    try { await remindHardOverlap(client, items, overlap); }
    catch (error) { client.warn(`overlap ${overlap.pulls.join('/')}: reminder failed (${error.status || error.message})`); }
  }
  const cleaned = [];
  const managed = new Set([...Object.values(STATUS_LABELS), ...Object.values(OVERLAP_LABELS)]);
  try {
    for (const pr of await client.recentClosedPulls()) {
      const labels = pr.labels.map(label => label.name).filter(name => managed.has(name));
      for (const label of labels) await client.removeLabel(pr.number, label);
      if (labels.length) cleaned.push(pr.number);
    }
  } catch (error) {
    client.warn(`closed PR label cleanup failed (${error.status || error.message})`);
  }
  core.info(JSON.stringify({results, overlaps, cleaned}, null, 2));
  return {results, overlaps, cleaned};
}

// Closed pull requests leave the board immediately. Labels are removed for a tidy closed PR list.
export async function clearClosed({github, context, core, number}) {
  const client = api({github, context, core});
  for (const label of [...Object.values(STATUS_LABELS), ...Object.values(OVERLAP_LABELS)]) await client.removeLabel(number, label);
}

export const AREA_PREFIX = AREA_LABEL_PREFIX;
