#!/usr/bin/env bash
# One-time repository setup. Run as the repository owner after the team workflow is on main.
set -euo pipefail

z_repo="${Z_REPO:-seanlee111/Z-from-Moon}"
z_gh="${Z_GH:-$(command -v gh || true)}"
z_context='Project Z / Ready'
[[ -n "$z_gh" && -x "$z_gh" ]] || { printf '需要 GitHub CLI（gh）。\n' >&2; exit 2; }
"$z_gh" auth status >/dev/null || { printf '请先运行 gh auth login。\n' >&2; exit 2; }

admin="$("$z_gh" api "repos/$z_repo" --jq '.permissions.admin')"
[[ "$admin" == true ]] || { printf '当前 GitHub 账号不是 %s 的管理员，无法修改仓库规则。\n' "$z_repo" >&2; exit 3; }
for workflow in project-checks.yml team-merge.yml; do
  "$z_gh" api "repos/$z_repo/contents/.github/workflows/$workflow?ref=main" --silent || {
    printf 'main 上还没有 %s。请先合并团队协作 PR，再运行本脚本。\n' "$workflow" >&2
    exit 4
  }
done

printf '→ 设置合并方式：只保留 squash，合并后删除任务分支\n'
"$z_gh" api -X PATCH "repos/$z_repo" \
  -F allow_squash_merge=true -F allow_merge_commit=false -F allow_rebase_merge=false \
  -F allow_update_branch=true -F delete_branch_on_merge=true \
  -f squash_merge_commit_title=PR_TITLE -f squash_merge_commit_message=PR_BODY --silent

printf '→ 打开 GitHub Actions\n'
"$z_gh" api -X PUT "repos/$z_repo/actions/permissions" -F enabled=true -f allowed_actions=all --silent

printf '→ 创建标签 hold / recheck\n'
"$z_gh" label create hold --repo "$z_repo" --color B60205 --description '暂停自动合并' --force
"$z_gh" label create recheck --repo "$z_repo" --color 0E8A16 --description '重新运行自动检查' --force

ruleset_file="$(mktemp)"
trap 'rm -f "$ruleset_file"' EXIT
cat > "$ruleset_file" <<JSON
{
  "name": "Protect main",
  "target": "branch",
  "enforcement": "active",
  "conditions": {"ref_name": {"include": ["~DEFAULT_BRANCH"], "exclude": []}},
  "rules": [
    {"type": "deletion"},
    {"type": "non_fast_forward"},
    {"type": "pull_request", "parameters": {
      "required_approving_review_count": 0,
      "dismiss_stale_reviews_on_push": false,
      "require_code_owner_review": false,
      "require_last_push_approval": false,
      "required_review_thread_resolution": false,
      "allowed_merge_methods": ["squash"]
    }},
    {"type": "required_status_checks", "parameters": {
      "strict_required_status_checks_policy": true,
      "do_not_enforce_on_create": false,
      "required_status_checks": [{"context": "$z_context"}]
    }}
  ]
}
JSON

ruleset_id="$("$z_gh" api "repos/$z_repo/rulesets" --jq '.[] | select(.name == "Protect main") | .id' | head -n 1)"
if [[ -n "$ruleset_id" ]]; then
  printf '→ 更新 main 保护规则\n'
  "$z_gh" api -X PUT "repos/$z_repo/rulesets/$ruleset_id" --input "$ruleset_file" --silent
else
  printf '→ 创建 main 保护规则\n'
  "$z_gh" api -X POST "repos/$z_repo/rulesets" --input "$ruleset_file" --silent
fi

for username in "$@"; do
  printf '→ 邀请协作者 %s\n' "$username"
  "$z_gh" api -X PUT "repos/$z_repo/collaborators/$username" --silent
done

printf '\n✓ 设置完成：%s\n' "https://github.com/$z_repo/rules"
"$z_gh" api "repos/$z_repo" --jq '"squash=\(.allow_squash_merge) merge=\(.allow_merge_commit) rebase=\(.allow_rebase_merge) delete_branch=\(.delete_branch_on_merge) update_branch=\(.allow_update_branch)"'
