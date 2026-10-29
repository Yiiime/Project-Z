import test from 'node:test';
import assert from 'node:assert/strict';
import {CONTEXT, TEXT, integrate, report, touchesMaintainerPaths} from '../automation.mjs';

const sha = n => String(n).repeat(40).slice(0, 40);
const repoName = 'Yiiime/Project-Z';
function pull(number, overrides = {}) {
  return {
    number, state: 'open', draft: false, title: `Task ${number}`, mergeable: true,
    user: {login: `user${number}`}, labels: [],
    head: {sha: sha(number), ref: `task/user${number}/demo`, repo: {full_name: repoName}},
    ...overrides,
  };
}
function fakeGitHub({pulls, statuses = {}, behind = {}, files = {}, permissions = {}, updateFails = false}) {
  const calls = {statuses: [], dispatches: [], merges: [], comments: [], updates: [], removedLabels: []};
  const byNumber = new Map(pulls.map(pr => [pr.number, pr]));
  const github = {
    paginate: async (method, params) => method(params).then(result => result.data),
    rest: {
      pulls: {
        list: async () => ({data: pulls}),
        get: async ({pull_number}) => ({data: byNumber.get(pull_number)}),
        listFiles: async ({pull_number}) => ({data: files[pull_number] || [{filename: 'unity/Assets/ProjectZ/Core/Models.cs'}]}),
        updateBranch: async ({pull_number, expected_head_sha}) => {
          calls.updates.push(pull_number);
          if (updateFails) throw Object.assign(new Error('conflict'), {status: 422});
          const pr = byNumber.get(pull_number);
          assert.equal(expected_head_sha, pr.head.sha);
          pr.head.sha = `${pull_number}`.repeat(40).replace(/./, 'f').slice(0, 40);
          behind[pull_number] = 0;
          return {data: {}};
        },
        merge: async ({pull_number, sha: mergedSha}) => {
          calls.merges.push({number: pull_number, sha: mergedSha});
          return {data: {merged: true, message: 'merged'}};
        },
      },
      repos: {
        getCollaboratorPermissionLevel: async ({username}) => ({data: {permission: permissions[username] || 'write'}}),
        getBranch: async () => ({data: {commit: {sha: 'a'.repeat(40)}}}),
        compareCommitsWithBasehead: async ({basehead}) => {
          const head = basehead.split('...')[1];
          const pr = pulls.find(item => item.head.sha === head);
          return {data: {behind_by: behind[pr.number] || 0}};
        },
        getCombinedStatusForRef: async ({ref}) => ({data: {statuses: statuses[ref] ? [statuses[ref]] : []}}),
        createCommitStatus: async status => {
          calls.statuses.push(status);
          statuses[status.sha] = {context: status.context, state: status.state, description: status.description, updated_at: new Date().toISOString()};
          return {data: {}};
        },
      },
      issues: {
        listComments: async () => ({data: calls.comments.map(body => ({body}))}),
        createComment: async ({body}) => { calls.comments.push(body); return {data: {}}; },
        removeLabel: async ({name}) => { calls.removedLabels.push(name); return {data: {}}; },
      },
      actions: {
        createWorkflowDispatch: async request => { calls.dispatches.push(request); return {data: {}}; },
      },
      git: {deleteRef: async () => ({data: {}})},
    },
  };
  return {github, calls};
}
const context = {repo: {owner: 'Yiiime', repo: 'Project-Z'}, runId: 7, serverUrl: 'https://github.com'};
const core = {info() {}, warning() {}, setFailed(message) { throw new Error(message); }};
const noWait = async () => {};

test('starts only one check when several ready PRs arrive together', async () => {
  const {github, calls} = fakeGitHub({pulls: [pull(1), pull(2)]});
  const result = await integrate({github, context, core, sleep: noWait});
  assert.deepEqual(result.map(item => item.result), ['checks-started', 'queued']);
  assert.equal(calls.dispatches.filter(item => item.workflow_id === 'project-checks.yml').length, 1);
  assert.equal(calls.statuses.find(item => item.sha === sha(2)).description, TEXT.queued);
});

test('updates a stale branch before checking it', async () => {
  const pr = pull(3);
  const {github, calls} = fakeGitHub({pulls: [pr], behind: {3: 2}});
  const [result] = await integrate({github, context, core, sleep: noWait});
  assert.equal(result.result, 'checks-started');
  assert.deepEqual(calls.updates, [3]);
  assert.equal(calls.dispatches[0].inputs.head_sha, pr.head.sha);
});

test('merges the exact checked head after success', async () => {
  const pr = pull(4);
  const statuses = {[pr.head.sha]: {context: CONTEXT, state: 'success', description: TEXT.passed, updated_at: new Date().toISOString()}};
  const {github, calls} = fakeGitHub({pulls: [pr], statuses});
  const [result] = await integrate({github, context, core, sleep: noWait});
  assert.equal(result.result, 'merged');
  assert.deepEqual(calls.merges, [{number: 4, sha: pr.head.sha}]);
});

test('conflicts are marked and explained once', async () => {
  const pr = pull(5, {mergeable: false});
  const {github, calls} = fakeGitHub({pulls: [pr]});
  await integrate({github, context, core, sleep: noWait});
  await integrate({github, context, core, sleep: noWait});
  assert.equal(calls.statuses.filter(item => item.description === TEXT.conflict).length, 1);
  assert.equal(calls.comments.length, 1);
  assert.equal(calls.merges.length, 0);
});

test('workflow and automation changes wait for a maintainer', async () => {
  const pr = pull(6);
  const statuses = {[pr.head.sha]: {context: CONTEXT, state: 'success', description: TEXT.passed, updated_at: new Date().toISOString()}};
  const {github, calls} = fakeGitHub({pulls: [pr], statuses, files: {6: [{filename: '.github/workflows/team-merge.yml'}]}});
  const [result] = await integrate({github, context, core, sleep: noWait});
  assert.equal(result.result, 'needs-maintainer');
  assert.equal(calls.merges.length, 0);
  assert.equal(touchesMaintainerPaths([{filename: 'unity/Tools/check.sh'}]), true);
});

test('draft, hold, fork and read-only PRs are skipped', async () => {
  const pulls = [
    pull(7, {draft: true}),
    pull(8, {labels: [{name: 'hold'}]}),
    pull(9, {head: {sha: sha(9), ref: 'x', repo: {full_name: 'someone/Z-from-Moon'}}}),
    pull(10, {user: {login: 'reader'}}),
  ];
  const {github, calls} = fakeGitHub({pulls, permissions: {reader: 'read'}});
  const result = await integrate({github, context, core, sleep: noWait});
  assert.deepEqual(result.map(item => item.result), ['draft', 'hold', 'fork-needs-maintainer', 'author-needs-write-access']);
  assert.equal(calls.dispatches.length, 0);
});

test('check report writes the required status and wakes the merger', async () => {
  const pr = pull(11);
  const {github, calls} = fakeGitHub({pulls: [pr]});
  await report({github, context, core, number: 11, head: pr.head.sha, result: 'success'});
  assert.equal(calls.statuses.at(-1).state, 'success');
  assert.equal(calls.dispatches.at(-1).workflow_id, 'team-merge.yml');
});

test('cancelled check runs leave the pending status for the stale-run retry', async () => {
  const pr = pull(12);
  const {github, calls} = fakeGitHub({pulls: [pr]});
  assert.equal(await report({github, context, core, number: 12, head: pr.head.sha, result: 'cancelled'}), 'cancelled');
  assert.equal(calls.statuses.length, 0);
  assert.equal(calls.comments.length, 0);
});

test('stale pending checks are restarted', async () => {
  const pr = pull(13);
  const old = new Date(Date.now() - 31 * 60 * 1000).toISOString();
  const statuses = {[pr.head.sha]: {context: CONTEXT, state: 'pending', description: TEXT.checking, updated_at: old}};
  const {github, calls} = fakeGitHub({pulls: [pr], statuses});
  const [result] = await integrate({github, context, core, sleep: noWait});
  assert.equal(result.result, 'checks-restarted');
  assert.equal(calls.dispatches[0].workflow_id, 'project-checks.yml');
});

test('a comment permission error does not block status or the merge queue', async () => {
  const pr = pull(14);
  const {github, calls} = fakeGitHub({pulls: [pr]});
  github.rest.issues.createComment = async () => { throw Object.assign(new Error('forbidden'), {status: 403}); };
  await report({github, context, core, number: 14, head: pr.head.sha, result: 'failure'});
  assert.equal(calls.statuses.at(-1).state, 'failure');
  assert.equal(calls.dispatches.at(-1).workflow_id, 'team-merge.yml');
});
