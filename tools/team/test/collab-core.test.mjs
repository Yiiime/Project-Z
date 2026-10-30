import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {
  DAY_MS, MARKERS, STATUS, areasForFile, assertConfig, codexPrompt, computeStatus, findOverlaps, globToRegExp, latestActivity,
  mergeOrder, missingConfirmations, outOfScopeFiles, parseTaskBody, replaceHandoff, replaceTaskMeta, selectedAreas, shipWarnings,
  startWarnings, taskBody, validateConfig,
} from '../collab-core.mjs';

const config = assertConfig(JSON.parse(readFileSync(new URL('../areas.json', import.meta.url), 'utf8')));
const thresholds = config.thresholds;
const now = '2026-10-30T12:00:00.000Z';
const ago = days => new Date(Date.parse(now) - days * DAY_MS).toISOString();
const open = {state: 'open', created_at: ago(30), user: {login: 'ana'}};

test('status uses configured three-day and seven-day boundaries', () => {
  assert.equal(computeStatus({pr: open, lastActivity: ago(0), now, thresholds}), STATUS.active);
  assert.equal(computeStatus({pr: open, lastActivity: new Date(Date.parse(ago(3)) + 1).toISOString(), now, thresholds}), STATUS.active);
  assert.equal(computeStatus({pr: open, lastActivity: ago(3), now, thresholds}), STATUS.paused);
  assert.equal(computeStatus({pr: open, lastActivity: ago(7), now, thresholds}), STATUS.paused);
  assert.equal(computeStatus({pr: open, lastActivity: new Date(Date.parse(ago(7)) - 1).toISOString(), now, thresholds}), STATUS.stale);
});

test('status disappears when a PR is merged or closed', () => {
  assert.equal(computeStatus({pr: {...open, state: 'closed', closed_at: now}, lastActivity: now, now, thresholds}), STATUS.done);
  assert.equal(computeStatus({pr: {...open, state: 'closed', merged_at: now}, lastActivity: now, now, thresholds}), STATUS.done);
});

test('still working refreshes activity only when the owner posts it', () => {
  const commits = [{author: {login: 'ana'}, commit: {committer: {date: ago(8)}}}];
  const comments = [
    {body: `${MARKERS.still}\n还在做`, created_at: ago(1), user: {login: 'ana'}},
    {body: `${MARKERS.still}\n别人点的`, created_at: ago(0), user: {login: 'bo'}},
  ];
  assert.equal(latestActivity({pr: open, commits, comments, owner: 'ana'}), ago(1));
});

test('automatic main sync commits do not keep an abandoned task active', () => {
  const commits = [
    {author: {login: 'ana'}, commit: {committer: {date: ago(9)}}},
    {author: {login: 'github-actions[bot]'}, commit: {author: {name: 'github-actions[bot]'}, committer: {date: ago(0)}}},
  ];
  assert.equal(latestActivity({pr: open, commits, owner: 'ana'}), ago(9));
});

test('glob matching supports directories and single path segments', () => {
  assert.equal(globToRegExp('unity/**/*.prefab').test('unity/Assets/A/B.prefab'), true);
  assert.equal(globToRegExp('unity/*.command').test('unity/打开项目.command'), true);
  assert.equal(globToRegExp('unity/*.command').test('unity/Tools/x.command'), false);
  assert.throws(() => globToRegExp('/absolute/path'));
});

test('overlaps are yellow for normal files and red for hard-to-merge files', () => {
  const pulls = [
    {number: 1, files: ['docs/plan.md', 'unity/Assets/ProjectZ/Scenes/Boot.unity']},
    {number: 2, files: ['docs/plan.md']},
    {number: 3, files: ['unity/Assets/ProjectZ/Scenes/Boot.unity']},
    {number: 4, files: ['unity/Assets/ProjectZ/Art/Prefabs/Z.prefab']},
  ];
  assert.deepEqual(findOverlaps(config, pulls).map(item => [item.pulls.join('/'), item.level, item.files]), [
    ['1/2', 'yellow', ['docs/plan.md']],
    ['1/3', 'red', ['unity/Assets/ProjectZ/Scenes/Boot.unity']],
  ]);
});

test('ready PR is recommended before a draft; otherwise the earlier PR goes first', () => {
  const draft = {number: 1, draft: true, created_at: ago(5)};
  const ready = {number: 2, draft: false, created_at: ago(1)};
  assert.deepEqual(mergeOrder(draft, ready).map(item => item.number), [2, 1]);
  assert.deepEqual(mergeOrder({...draft, draft: false}, ready).map(item => item.number), [1, 2]);
});

test('every tracked Unity and design file belongs to at least one area', () => {
  const files = execFileSync('git', ['-c', 'core.quotePath=false', 'ls-files', 'unity', 'docs', 'research', 'ui', 'README.md'], {encoding: 'utf8'}).trim().split('\n');
  assert.deepEqual(files.filter(path => !areasForFile(config, path).length), []);
  assert.deepEqual(areasForFile(config, 'unity/Assets/ProjectZ/Art/Animation/Z_Wave.anim'), ['z-motion-expression']);
  assert.deepEqual(areasForFile(config, 'unity/Assets/ProjectZ/Resources/Content/episodes.json'), ['story-events']);
});

test('config validation reports PM-editable mistakes clearly', () => {
  const bad = structuredClone(config);
  bad.thresholds.staleAfterDays = 2;
  bad.areas[1].id = bad.areas[0].id;
  bad.areas[2].paths = ['/tmp/no'];
  const errors = validateConfig(bad);
  assert.ok(errors.some(error => error.includes('staleAfterDays')));
  assert.ok(errors.some(error => error.includes('id 重复')));
  assert.ok(errors.some(error => error.includes('无效路径规则')));
  assert.throws(() => selectedAreas(config, []), /1～2/);
  assert.throws(() => selectedAreas(config, ['story-events', 'memory-ai', 'design-docs']), /1～2/);
});

test('out-of-scope files add labels without failing', () => {
  const result = outOfScopeFiles(config, ['story-events'], ['unity/Assets/ProjectZ/Resources/Content/episodes.json', 'unity/Assets/ProjectZ/Scenes/Boot.unity', 'other/file.txt']);
  assert.deepEqual(result, {areaIds: ['map-gameplay'], unmatched: ['other/file.txt']});
});

test('start and ship warnings require confirmation only for red overlaps', () => {
  const tasks = [
    {number: 8, owner: 'ana', ownerName: '小A', status: STATUS.active, areas: ['map-gameplay'], lastActivity: ago(.1), files: ['unity/Assets/ProjectZ/Scenes/Boot.unity']},
    {number: 9, owner: 'bo', status: STATUS.paused, areas: ['map-gameplay'], lastActivity: ago(4), files: ['unity/Assets/ProjectZ/Runtime/IslandScene.cs']},
    {number: 10, owner: 'cy', status: STATUS.stale, areas: ['map-gameplay'], lastActivity: ago(9), files: ['unity/Assets/ProjectZ/Scenes/Boot.unity']},
  ];
  const warnings = startWarnings(config, tasks, ['map-gameplay'], 'me');
  assert.deepEqual(warnings.map(item => [item.number, item.level]), [[8, 'red'], [9, 'yellow']]);
  assert.deepEqual(missingConfirmations(warnings), ['ana']);
  assert.deepEqual(missingConfirmations(warnings, ['ana']), []);
  const shipping = shipWarnings([{level: 'red', pulls: [8, 11], hardFiles: ['x.prefab']}], tasks, 11);
  assert.deepEqual(shipping.map(item => item.owner), ['ana']);
});

test('task metadata, handoff and prompts keep the selected scope', () => {
  const areas = selectedAreas(config, ['story-events']);
  const body = taskBody({goal: '加入潮水叶子', areas, owner: 'ana', startedAt: now});
  assert.deepEqual(parseTaskBody(body), {version: 1, goal: '加入潮水叶子', areas: ['story-events'], owner: 'ana', startedAt: now});
  const changed = replaceTaskMeta(replaceHandoff(body, '下一步：补次日台词'), {owner: 'bo'});
  assert.equal(parseTaskBody(changed).owner, 'bo');
  assert.match(changed, /下一步：补次日台词/);
  for (const kind of ['start', 'save', 'ship', 'conflict', 'checks']) {
    const text = codexPrompt(kind, {goal: '加入潮水叶子', areas, number: 12, branch: 'task/ana/leaf'});
    assert.match(text, /unity\/Assets\/ProjectZ\/Resources\/Content/);
    assert.match(text, /先停下来问我/);
  }
});
