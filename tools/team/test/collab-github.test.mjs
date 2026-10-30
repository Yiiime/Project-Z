import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {assertConfig, MARKERS, OVERLAP_LABELS, STATUS_LABELS, taskBody, selectedAreas} from '../collab-core.mjs';
import {collaborate} from '../collab-github.mjs';

const config = assertConfig(JSON.parse(readFileSync(new URL('../areas.json', import.meta.url), 'utf8')));
const now = '2026-10-30T12:00:00.000Z';
const ago = days => new Date(Date.parse(now) - days * 86400000).toISOString();
function pull(number, owner, areaIds, created, labels = []) {
  return {number, state: 'open', draft: true, created_at: created, user: {login: owner}, labels: labels.map(name => ({name})),
    head: {repo: {full_name: 'Yiiime/Project-Z'}}, body: taskBody({goal: `任务 ${number}`, areas: selectedAreas(config, areaIds), owner, startedAt: created})};
}
function fake({pulls, files, commits = {}, comments = {}, closed = []}) {
  const calls = {labels: [], removed: [], comments: [], created: []};
  const github = {
    paginate: async (method, params) => (await method(params)).data,
    rest: {
      pulls: {
        list: async ({state}) => ({data: state === 'closed' ? closed : pulls}),
        listFiles: async ({pull_number}) => ({data: files[pull_number].map(filename => ({filename}))}),
        listCommits: async ({pull_number}) => ({data: commits[pull_number] || []}),
      },
      issues: {
        listComments: async ({issue_number}) => ({data: comments[issue_number] || []}),
        createComment: async ({issue_number, body}) => { calls.comments.push({issue_number, body}); (comments[issue_number] ||= []).push({body}); return {data: {}}; },
        getLabel: async () => ({data: {}}),
        createLabel: async ({name}) => { calls.created.push(name); return {data: {}}; },
        addLabels: async ({issue_number, labels}) => { calls.labels.push({issue_number, labels}); return {data: {}}; },
        removeLabel: async ({issue_number, name}) => { calls.removed.push({issue_number, name}); return {data: {}}; },
      },
    },
  };
  return {github, calls};
}
const context = {repo: {owner: 'Yiiime', repo: 'Project-Z'}};
const core = {info() {}, warning(message) { throw new Error(message); }};

test('daily update labels status and pings a paused owner once', async () => {
  const pulls = [pull(1, 'ana', ['story-events'], ago(4))];
  const {github, calls} = fake({pulls, files: {1: ['unity/Assets/ProjectZ/Resources/Content/episodes.json']}});
  await collaborate({github, context, core, now, config});
  await collaborate({github, context, core, now, config});
  assert.ok(calls.labels.some(item => item.labels.includes(STATUS_LABELS.paused)));
  assert.equal(calls.comments.filter(item => item.body.includes(MARKERS.pausedPing)).length, 1);
  assert.match(calls.comments[0].body, /@ana/);
});

test('red overlap comments both PRs once and labels both', async () => {
  const pulls = [pull(2, 'ana', ['map-gameplay'], ago(1)), pull(3, 'bo', ['map-gameplay'], ago(.5))];
  const files = {2: ['unity/Assets/ProjectZ/Scenes/Boot.unity'], 3: ['unity/Assets/ProjectZ/Scenes/Boot.unity']};
  const {github, calls} = fake({pulls, files});
  await collaborate({github, context, core, now, config});
  await collaborate({github, context, core, now, config});
  assert.deepEqual(calls.comments.filter(item => item.body.includes(MARKERS.conflict)).map(item => item.issue_number).sort(), [2, 3]);
  assert.ok(calls.labels.filter(item => item.labels.includes(OVERLAP_LABELS.red)).length >= 2);
  assert.match(calls.comments.find(item => item.issue_number === 3).body, /建议先合并 #2/);
});

test('yellow overlap labels without comments; out-of-scope files add an area label', async () => {
  const pulls = [pull(4, 'ana', ['story-events'], ago(1)), pull(5, 'bo', ['design-docs'], ago(1))];
  const files = {4: ['docs/游戏设计总案-v2.md'], 5: ['docs/游戏设计总案-v2.md']};
  const {github, calls} = fake({pulls, files});
  await collaborate({github, context, core, now, config});
  assert.ok(calls.labels.some(item => item.issue_number === 4 && item.labels.includes('area:design-docs')));
  assert.ok(calls.labels.some(item => item.labels.includes(OVERLAP_LABELS.yellow)));
  assert.equal(calls.comments.filter(item => item.body.includes(MARKERS.conflict)).length, 0);
  assert.equal(calls.comments.filter(item => item.body.includes(MARKERS.scope)).length, 1);
});

test('recently merged PRs lose status and overlap labels', async () => {
  const closed = [{number: 9, labels: [{name: STATUS_LABELS.active}, {name: OVERLAP_LABELS.red}, {name: 'area:design-docs'}]}];
  const {github, calls} = fake({pulls: [], files: {}, closed});
  const result = await collaborate({github, context, core, now, config});
  assert.deepEqual(result.cleaned, [9]);
  assert.deepEqual(calls.removed.map(item => item.name).sort(), [OVERLAP_LABELS.red, STATUS_LABELS.active].sort());
});
