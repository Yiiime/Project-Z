// Runs from main inside privileged workflows. It only calls the GitHub API and never runs pull request code.
export const CONTEXT = 'Project Z / Ready';
export const CHECK_WORKFLOW = 'project-checks.yml';
export const MERGE_WORKFLOW = 'team-merge.yml';
export const LABELS = Object.freeze({hold: 'hold', recheck: 'recheck'});
export const MAINTAINER_PATHS = Object.freeze([/^\.github\//, /^tools\/team\//, /^unity\/Tools\/check\.sh$/]);
export const TEXT = Object.freeze({
  queued: '排队中：前面的 PR 处理完后自动检查',
  checking: '检查中：正在检查与最新 main 合并后的版本',
  passed: '通过：可以合并',
  failed: '未通过：点 Details 查看原因',
  conflict: '冲突：请在任务分支同步 main 并解决',
  syncFailed: '无法自动同步 main：请在本地同步后推送',
  dispatchFailed: '无法启动检查：请联系维护者',
});

const RETRYABLE = new Set([TEXT.queued, TEXT.conflict, TEXT.syncFailed, TEXT.dispatchFailed]);
const WRITE = new Set(['admin', 'maintain', 'write']);
const BUSY = new Set(['checks-started', 'checks-restarted', 'checking', 'maintainer-checks-started', 'maintainer-checking', 'sync-pending', 'main-moved']);
const TRANSIENT = new Set(['calculating', 'sync-pending', 'main-moved', 'merge-blocked']);
const STALE_PENDING_MS = 30 * 60 * 1000;
const MAX_ATTEMPTS = 8;
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));

export const touchesMaintainerPaths = files => files.some(file =>
  [file.filename, file.previous_filename].filter(Boolean).some(path => MAINTAINER_PATHS.some(rule => rule.test(path))));

function client({github, context, core, sleep = pause}) {
  const repo = context.repo;
  const fullName = `${repo.owner}/${repo.repo}`;
  const server = context.serverUrl || 'https://github.com';
  const checksUrl = `${server}/${fullName}/actions/workflows/${CHECK_WORKFLOW}`;
  const runUrl = context.runId ? `${server}/${fullName}/actions/runs/${context.runId}` : checksUrl;
  const data = async request => (await request).data;
  const api = {
    fullName, runUrl, sleep,
    pull: number => data(github.rest.pulls.get({...repo, pull_number: number})),
    openPulls: () => github.paginate(github.rest.pulls.list, {...repo, state: 'open', base: 'main', sort: 'created', direction: 'asc', per_page: 100}),
    permission: async username => (await data(github.rest.repos.getCollaboratorPermissionLevel({...repo, username}))).permission,
    mainSha: async () => (await data(github.rest.repos.getBranch({...repo, branch: 'main'}))).commit.sha,
    async behindBy(mainSha, headSha) {
      const request = github.rest.repos.compareCommitsWithBasehead
        ? github.rest.repos.compareCommitsWithBasehead({...repo, basehead: `${mainSha}...${headSha}`})
        : github.rest.repos.compareCommits({...repo, base: mainSha, head: headSha});
      return (await data(request)).behind_by;
    },
    async currentStatus(sha) {
      const statuses = (await data(github.rest.repos.getCombinedStatusForRef({...repo, ref: sha}))).statuses || [];
      return statuses.find(status => status.context === CONTEXT) || null;
    },
    status: (sha, state, description, targetUrl = runUrl) => github.rest.repos.createCommitStatus({
      ...repo, sha, state, context: CONTEXT, description, target_url: targetUrl,
    }),
    files: number => github.paginate(github.rest.pulls.listFiles, {...repo, pull_number: number, per_page: 100}),
    async removeLabel(number, name) {
      try { await github.rest.issues.removeLabel({...repo, issue_number: number, name}); }
      catch (error) { if (error.status !== 404) throw error; }
    },
    async notify(pr, kind, body) {
      const marker = `<!-- project-z-bot:${kind}:${pr.head.sha} -->`;
      const comments = await github.paginate(github.rest.issues.listComments, {...repo, issue_number: pr.number, per_page: 100});
      if (comments.some(comment => comment.body?.includes(marker))) return;
      await github.rest.issues.createComment({...repo, issue_number: pr.number, body: `${marker}\n${body}`});
    },
    async waitMergeable(pr) {
      for (let attempt = 0; pr.mergeable === null && attempt < 5; attempt++) {
        await sleep(2000);
        pr = await api.pull(pr.number);
      }
      return pr;
    },
    async updateBranch(pr) {
      const previous = pr.head.sha;
      try {
        await github.rest.pulls.updateBranch({...repo, pull_number: pr.number, expected_head_sha: previous});
      } catch (error) {
        core.warning(`#${pr.number}: update branch failed (${error.status || error.message})`);
        await api.status(previous, 'failure', TEXT.syncFailed);
        await api.notify(pr, 'sync', `机器人无法把最新 main 自动同步到这个分支。请对你的 agent 说：「运行 Z 项目的 sync，处理好以后重新 ship」。`);
        return {state: 'sync-failed'};
      }
      for (let attempt = 0; attempt < 20; attempt++) {
        await sleep(3000);
        const latest = await api.pull(pr.number);
        if (latest.head.sha !== previous) return {state: 'updated', pr: latest};
      }
      return {state: 'sync-pending'};
    },
    async dispatchChecks(pr) {
      await api.status(pr.head.sha, 'pending', TEXT.checking, checksUrl);
      try {
        await github.rest.actions.createWorkflowDispatch({
          ...repo, workflow_id: CHECK_WORKFLOW, ref: 'main', inputs: {pr_number: String(pr.number), head_sha: pr.head.sha},
        });
      } catch (error) {
        await api.status(pr.head.sha, 'failure', TEXT.dispatchFailed, checksUrl);
        throw error;
      }
    },
    dispatchMerge: attempt => github.rest.actions.createWorkflowDispatch({
      ...repo, workflow_id: MERGE_WORKFLOW, ref: 'main', inputs: {attempt: String(attempt)},
    }),
    async merge(pr) {
      try {
        const result = await data(github.rest.pulls.merge({
          ...repo, pull_number: pr.number, sha: pr.head.sha, merge_method: 'squash', commit_title: `${pr.title} (#${pr.number})`,
        }));
        if (!result.merged) return false;
      } catch (error) {
        if (![405, 409, 422].includes(error.status)) throw error;
        core.warning(`#${pr.number}: merge blocked (${error.status}: ${error.message}); retrying later`);
        return false;
      }
      try { await github.rest.git.deleteRef({...repo, ref: `heads/${pr.head.ref}`}); }
      catch (error) { core.info(`#${pr.number}: branch kept (${error.status || error.message})`); }
      return true;
    },
  };
  return api;
}

async function evaluate(api, number) {
  let pr = await api.pull(number);
  if (pr.state !== 'open') return {result: 'closed'};
  if (pr.draft) return {result: 'draft'};
  if (pr.head.repo?.full_name !== api.fullName) return {result: 'fork-needs-maintainer'};
  const labels = new Set(pr.labels.map(label => label.name));
  if (labels.has(LABELS.hold)) return {result: 'hold'};
  if (!WRITE.has(await api.permission(pr.user.login))) return {result: 'author-needs-write-access'};
  pr = await api.waitMergeable(pr);
  if (pr.mergeable === null) return {result: 'calculating'};
  if (pr.mergeable === false) {
    const status = await api.currentStatus(pr.head.sha);
    if (status?.description !== TEXT.conflict) await api.status(pr.head.sha, 'failure', TEXT.conflict);
    await api.notify(pr, 'conflict', `这个 PR 和最新 main 有冲突，机器人不能自动合并。请对你的 agent 说：「运行 Z 项目的 sync，解决冲突后重新 ship」。冲突涉及别人改动的意图时，先和对方确认。`);
    return {result: 'conflict'};
  }
  return {result: 'candidate', pr, labels};
}

async function advance(api, pr, labels, now) {
  const maintainerOnly = touchesMaintainerPaths(await api.files(pr.number));
  if (await api.behindBy(await api.mainSha(), pr.head.sha) > 0) {
    if (maintainerOnly) {
      await api.notify(pr, 'maintainer-update', '这个 PR 修改了协作规则或检查脚本（`.github/`、`tools/team/`、`unity/Tools/check.sh`），机器人不会自动同步或合并。维护者确认后点 **Update branch**，检查通过再手动合并。');
      return 'needs-maintainer';
    }
    const update = await api.updateBranch(pr);
    if (update.state !== 'updated') return update.state;
    pr = update.pr;
    if (await api.behindBy(await api.mainSha(), pr.head.sha) > 0) return 'main-moved';
  }

  let status = await api.currentStatus(pr.head.sha);
  if (labels.has(LABELS.recheck)) {
    await api.removeLabel(pr.number, LABELS.recheck);
    status = null;
  }
  if (status && RETRYABLE.has(status.description)) status = null;
  if (!status) {
    await api.dispatchChecks(pr);
    return maintainerOnly ? 'maintainer-checks-started' : 'checks-started';
  }
  if (status.state === 'pending') {
    if (now() - Date.parse(status.updated_at) < STALE_PENDING_MS) return maintainerOnly ? 'maintainer-checking' : 'checking';
    await api.dispatchChecks(pr);
    return maintainerOnly ? 'maintainer-checks-started' : 'checks-restarted';
  }
  if (status.state !== 'success') return 'checks-failed';
  if (maintainerOnly) {
    await api.notify(pr, 'maintainer', '检查已通过。这个 PR 修改了协作规则或检查脚本（`.github/`、`tools/team/`、`unity/Tools/check.sh`），需要维护者确认后手动合并。');
    return 'needs-maintainer';
  }
  return await api.merge(pr) ? 'merged' : 'merge-blocked';
}

export async function integrate({github, context, core, sleep, now = Date.now, attempt = 0}) {
  const api = client({github, context, core, sleep});
  const results = [];
  let busy = false;
  let unexpected = 0;
  for (const item of await api.openPulls()) {
    try {
      const checked = await evaluate(api, item.number);
      let result = checked.result;
      if (result === 'candidate') {
        if (busy) {
          result = 'queued';
          if (!await api.currentStatus(checked.pr.head.sha)) await api.status(checked.pr.head.sha, 'pending', TEXT.queued);
        } else {
          result = await advance(api, checked.pr, checked.labels, now);
          busy = BUSY.has(result);
        }
      }
      results.push({number: item.number, result});
      core.info(`#${item.number}: ${result}`);
    } catch (error) {
      unexpected++;
      results.push({number: item.number, result: 'error'});
      core.warning(`#${item.number}: ${error.stack || error.message}`);
    }
  }
  if (attempt < MAX_ATTEMPTS && results.some(item => TRANSIENT.has(item.result))) {
    await api.sleep(20000);
    await api.dispatchMerge(attempt + 1);
  }
  if (unexpected) core.setFailed(`${unexpected} pull request(s) need maintainer attention; see warnings.`);
  return results;
}

export async function report({github, context, core, number, head, result}) {
  if (!Number.isInteger(number) || number < 1 || !/^[0-9a-f]{40}$/.test(head)) throw new Error('Invalid check inputs');
  const api = client({github, context, core});
  if (result === 'cancelled') {
    core.info('Check run was cancelled; the merge bot restarts stale checks automatically.');
    return 'cancelled';
  }
  const state = result === 'success' ? 'success' : 'failure';
  const description = state === 'success' ? TEXT.passed : TEXT.failed;
  await api.status(head, state, description);
  const pr = await api.pull(number);
  if (state === 'failure' && pr.state === 'open' && pr.head.sha === head) {
    await api.notify(pr, 'checks', `自动检查未通过：[查看日志](${api.runUrl})。请对你的 agent 说：「修复 Z 项目 PR #${number} 的自动检查失败，然后重新 ship」。确认只是偶发问题时，给 PR 加 \`recheck\` 标签重新检查。`);
  }
  await api.dispatchMerge(0);
  return state;
}
