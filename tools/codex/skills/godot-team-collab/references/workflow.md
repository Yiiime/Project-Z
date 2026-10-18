# 从任务到 PR 的操作流程

本文供 Agent 按阶段执行；代码块是命令示例，不是要求一次全部运行。`<...>` 是必须替换的占位符，不能原样粘贴。先遵循仓库现有流程，再采用这里的安全默认值。

## 1. 授权与任务边界

- 用户要求开发功能或交付资产，通常已授权为该任务创建本地分支、必要的 Worktree、修改约定文件及运行项目现有检查；不自动包含 Commit、Push、发布 PR。
- 用户明确要求“提交”“推送”“开 PR”时，按完整语义执行；“完成开发并交付 PR”也包含本任务所需的 Commit、普通 Push 和 PR，不要求逐个念出 Git 动词或重复询问。单说“提交”默认指本地 Commit，“检查/准备 PR”只到草稿；另有禁止上传等限制时遵守限制。
- 每次合并（包括 GitHub 合并 PR）、rebase、cherry-pick、reset 回退、删除分支/Worktree、prune、stash/pop，先给出具体对象、影响与恢复方案，获得用户针对该操作的确认。现有确认仍有效，目标或方案改变才重新确认。LFS 历史迁移不由本 Skill 执行，交维护者独立规划。
- 本 Skill 禁止强推，包括 `--force-with-lease`、`+refspec`；禁止直接修改/提交/推送 `main`，禁止绕过保护规则，禁止用 `rm -rf` 删除 Worktree。用户需要例外时，停止这条 Skill 流程，交由维护者处理。
- 禁止用 `reset --hard`、`clean -fd/-fdx`、强制 checkout、`worktree add -B/--force`、整仓 restore 等方式“快速修复”现场。

确定任务负责人、验收行为、允许修改的文件/目录、依赖接口和交付阶段。复用现有 Issue、任务文档或聊天记录记录这些信息，不为每个步骤自动新增文件。

## 2. 先做只读快照

在预计的仓库目录检查，失败时不要自动 `git init` 或切换仓库：

```bash
git rev-parse --show-toplevel
git rev-parse --git-common-dir
git branch --show-current
git rev-parse HEAD
git status --short --branch --untracked-files=all
git status --short --ignored=matching
git diff --stat
git diff --cached --stat
git diff --name-only --diff-filter=U
git worktree list --porcelain
```

记录当前路径、分支/是否 detached HEAD、HEAD SHA、已有已暂存/未暂存/未跟踪文件及其归属。`status --short` 中第一列是暂存区状态，第二列是工作文件状态；不能只看未暂存差异。ignored 输出中的 `!!` 路径也可能含未交付的源模型或导出物，不能因它被忽略就视为可覆盖；目录被折叠时按任务风险查清其中的资产，不逐个扫描全部引擎缓存。

再查看 `git status` 的完整说明，检查是否正在 merge、rebase、cherry-pick、revert、bisect 或有冲突。必要时用 `git rev-parse --git-path MERGE_HEAD` 等取得 Git 状态文件位置，不假设 `.git` 是目录。发现未完成操作先报告，不擅自 continue/abort，也不以另建 Worktree 绕开未厘清的现场。

读取 `README.md`、根及目标路径沿途的 `AGENTS.md`、架构/资产文档、`project.godot`、`.gitignore`、`.gitattributes`、现有检查/导出说明。搜索已有脚本、场景、资源及调用点，列出可以复用的实现；新建模块前说明现有实现为何不能满足任务。

核对远端、身份和配置。读取远端时，用本地进程 API 捕获 `git remote get-url --all origin` 与 `git remote get-url --push --all origin` 的输出，先解析/脱敏，只向会话显示主机、仓库和 URL 数量；不要直接回显原始 URL。发现 URL 嵌有密码、token 或敏感查询参数时停止网络步骤，报告配置位置并由本人改为正常凭据管理，不把值拼进后续命令、日志或文档。

其余检查：

```bash
git config --get user.name
git config --get user.email
git config --get-all remote.origin.push
git config --get remote.origin.mirror
git config --get push.followTags
gh auth status
```

未设置的配置项可能以退出码 1 返回，这是“未设置”，不等于损坏。核对实际 GitHub 主机、组织、仓库及登录账号；`gh` 登录和 Git 的 HTTPS/SSH 凭证可能不同，不能只凭 `gh auth status` 推断 Push 权限。多个 Push URL、镜像远端或与任务不符的目标必须先停下确认。

## 3. 从已获取的 main 固定起点

由协调者执行一次 Fetch，其他 Agent 不同时 Fetch、改仓库配置或创建同名分支。先记录已有 `origin/main` 的 SHA（不存在可跳过），再执行：

```bash
git fetch --no-tags --no-prune --no-recurse-submodules --refmap= origin refs/heads/main:refs/remotes/origin/main
git rev-parse --verify 'refs/remotes/origin/main^{commit}'
```

该命令只明确请求远端 `main` 并更新本地 `origin/main`：`--no-tags` 不额外获取标签，`--no-prune` 不清理引用，`--no-recurse-submodules` 不递归处理子模块，空 `--refmap=` 避免继承意外的远端映射。它不会更新本地 `main` 或合并工作文件。

将成功获取的完整提交 SHA 记为本次 `BASE_SHA`，记录获取时间；后续命令用这个固定 SHA。若之前的 `origin/main` 存在，用 `git merge-base --is-ancestor <旧SHA> <BASE_SHA>` 核对是否发生历史回退/改写；非祖先关系先交维护者确认。Fetch 即使没有 `+`，对远端跟踪引用也不提供“绝不回退”的保证。

Fetch 失败时停止“从最新 main 开始”流程。可继续阅读本地代码，但不能把缓存的 `origin/main` 称为最新；只有用户明确接受离线旧起点才继续，并记录该限制。这里的“最新”仅指本次成功 Fetch 的快照，远端可能随后前进。

### A. 单人在干净工作目录开发

只有已确认整个工作目录干净、无其他写入者，并且切换目标不会覆盖有价值的 ignored 文件，才在当前目录切换。普通 `git status` 干净不包含后一项保证；不能查清时选独立 Worktree：

```bash
git switch --no-track -c feature/<负责人>-<任务> <BASE_SHA>
```

`-c` 创建新分支；`--no-track` 不让功能分支错误跟踪 `origin/main`。本地 `main` 无需 checkout 或 pull。分支已存在时先核查所有者及用途，不改用 `-C` 或覆盖它。

### B. 原目录有修改，或多个参与者并行

保留原目录全部 staged、unstaged、untracked 内容，用仓库外尚不存在的独立路径：

```bash
git worktree add --no-track -b feature/<负责人>-<任务> ../game-<负责人>-<任务> <BASE_SHA>
```

先确认目标路径不存在、分支名未占用；失败时停下诊断，不自动加 `--force`。新 Worktree 不包含原目录未提交的修改。任务确实依赖这些修改时，先和作者确定交接方式，不擅自复制、stash、提交或 cherry-pick。

创建后进入新路径，重新核对仓库、分支、HEAD 和状态，让 Codex、Godot、Blender 都打开该路径对应的文件。不要让两个编辑器后台保存同一份场景或资产。

### C. Codex 已创建 Worktree / detached HEAD

先识别并复用现有 Codex 管理的任务目录。`git branch --show-current` 为空可能是正常的 detached HEAD，不等于工作区损坏。不要为满足模板另建一份并遗弃已有修改。

若该目录已承载当前任务，核对 HEAD、已有任务提交和改动归属；需要交付分支时，可在已确认的当前 HEAD 上执行 `git switch --no-track -c feature/<负责人>-<任务>`，保留当前内容。若起点不是要求的最新 main，明确告知偏差，准备同步方案并按高风险流程确认；禁止 reset 掉任务进展以伪装成新起点。Codex 管理的目录优先由其原有管理能力处理，不擅自移动或删除。

应用管理的临时 Worktree 可能随归档或数量限制回收，Skill 无法拦截应用自身行为。需长期保留时使用原生 Git 创建的独立目录或应用的永久 Worktree；不擅自改用户回收设置，也不把归档当作无副作用操作。[Codex Worktree 生命周期](https://learn.chatgpt.com/docs/environments/git-worktrees)

## 4. 并行协作的最小规则

一个任务一个分支，一个 Worktree 同一时刻只有一个写入者。可以并行只读评审；需要共同编辑时，先交接写权限。

| 范围 | 规则 |
| --- | --- |
| 分支/工作目录 | 每个 Agent 独立；所有者、绝对路径、分支名、BASE_SHA 明确记录 |
| `.gd`、`.tscn`、`.tres` | 按文件或明确模块分工；同一场景不能依赖“不同节点就不会冲突” |
| `project.godot`、Autoload、输入映射、公共接口 | 指定一名整合者；其他任务提出修改需求，由整合者落地 |
| `.blend`、`.glb` 等二进制 | 按资产负责人串行修改，遵守资产锁/交接规则 |
| Fetch、配置、共享引用 | 协调者串行操作，Agent 不各自改 remote、清分支或 stash/pop |

Worktree 隔离工作文件、HEAD 和 index；Git 对象、普通分支/远端引用、`refs/stash` 及默认仓库配置共享。因此它不是权限沙箱，不能阻止 Agent 越界读写别的目录，也不能自动避免最终合并冲突。每个 Agent 必须遵守被分配的绝对路径及文件所有权。

分别导入/运行各自的 Godot 工程，遵循项目规定的引擎版本及缓存忽略规则。各任务先独立提交/PR；若 B 依赖 A 尚未合入的接口，先冻结接口约定或等待 A 合入，不能宣称两个任务完全独立。需要依赖分支或整合分支时显式调整计划。

## 5. 提交前检查与 Commit

先验证任务验收条件，再按项目现有命令执行适当检查。Godot 导入成功、脚本解析成功、实际运行场景分别报告；没有运行就写“未运行”，不把静态检查称为游戏验收。

```bash
git status --short --untracked-files=all
git diff --stat
git diff --check
git diff --cached --stat
git diff --cached --check
git log --oneline <BASE_SHA>..HEAD
git diff --stat <BASE_SHA>...HEAD
```

检查场景引用、资源路径和 UID、非预期导入变更、生成文件、秘密信息、异常大文件与 LFS 规则；按任务范围逐文件阅读实际差异。`<BASE_SHA>...HEAD` 只含已提交分支差异，不能替代工作区和暂存区检查。

**已有 staged 内容不得混入本次提交。** 与开始时快照对比：如果暂存区含别人的文件，或同一文件已有归属不清的已暂存片段，先停下交接；不擅自 unstage、全量 commit 或使用技巧绕过归属核实。

取得 Commit 授权且范围明确后，按具体路径暂存：

```bash
git add -- path/to/changed.gd path/to/changed.tscn
git diff --cached --name-status
git diff --cached
git diff --cached --check
git commit -m "feat: 描述玩家可感知的变化"
git show --stat --oneline HEAD
git status --short --branch
```

`--` 后是文件路径，避免被解释为选项。不使用 `git add .`、`git add -A` 或 `git commit -a`。若选中文件本身混有他人未提交改动，整文件 `git add` 同样不安全，先确定片段归属。LFS 大二进制不要把完整内容输出；核对路径、指针/对象状态及导入结果。

Commit 只写本地快照。记录提交 SHA、检查结果及仍然保留的用户修改。提交失败时先解释原因，不跳过 Hook、不自动 amend，不创建空提交充数。

## 6. Push 与 PR

仅在交付授权覆盖该阶段时执行。缺少授权也应先完成可评审的本地改动、检查和 PR 草稿，再针对具体目标询问；禁止用“是否继续”重复询问已明确授权的事项。

重新核对当前分支不是 `main`、待推提交、目标仓库及唯一 Push URL。检查 `remote.origin.push`、`remote.origin.mirror`、`push.followTags`：显式 refspec 避免默认映射误推，单次关闭 mirror/followTags 避免扩大范围，不修改用户持久配置。遇到镜像仓库或多目标 Push 配置先停止，由维护者明确安全目标。

```bash
git branch --show-current
git log --oneline <BASE_SHA>..HEAD
git -c remote.origin.mirror=false -c push.followTags=false push --set-upstream origin HEAD:refs/heads/feature/<负责人>-<任务>
```

执行前必须确认目标分支名与当前任务分支一致。`HEAD:refs/heads/...` 明确只推当前提交到指定功能分支；`--set-upstream` 在成功后将其跟踪目标设为同名远端功能分支。不得省略目标、使用 `--all/--mirror/--tags` 或添加任何强推选项。LFS 的 pre-push Hook 必须正常执行；缺对象、权限或配额失败要报告，不绕过上传。

推送后用 `git ls-remote <已核实的唯一PushURL> refs/heads/feature/<负责人>-<任务>` 核实远端 SHA 等于计划交付的 SHA；URL 不得含凭证。不能默认用 `ls-remote origin` 代替，因为它查询 fetch URL，而该 URL 可能与 push URL 不同。PR 的 head 仓库也必须对应实际 push 目标。认证、超时或响应不明不等于失败；先查询远端再决定是否重试，避免重复创建分支/PR。

使用仓库既有 PR 模板。正文说明问题、变更行为、涉及文件/资产、验证步骤与结果、剩余限制及审阅负责人；美术交付包含资产预览、导入/替换方法、源文件与导出关系。把完整正文写入任务临时目录的独立文件，不把临时文件加入游戏仓库；用 `--body-file` 保留真实换行并避免 Shell 解释正文。

先查询已有 PR，避免重复：

```bash
gh pr list --repo <OWNER/REPO> --head feature/<负责人>-<任务> --base main --state all
```

逐一核对 head 仓库、base、状态与提交：匹配的 open PR 复用并按本次授权更新，不覆盖他人内容；closed 且未合并的 PR 不能称为已交付，先说明关闭原因并确认是否重开或新建；merged PR 若已包含全部本次提交则报告已有结果，有新的任务提交则另行确定交付分支与新 PR，不能把旧链接当作新交付。仅在确认需要新建时执行：

```bash
gh pr create --repo <OWNER/REPO> --base main --head feature/<负责人>-<任务> --title "描述最终行为" --body-file <正文文件绝对路径>
```

这里假设团队在同一 GitHub 仓库有功能分支权限；fork 情形先明确源仓库/目标仓库，不自动创建 fork。显式 `--head` 避免 `gh` 自动推送或建 fork，显式 `--base main` 避免默认基分支歧义。**`gh pr create --dry-run` 仍可能 Push，不可用作纯只读预览。** 未完成的内容可按用户意图创建 Draft PR。

创建后读取 PR URL、base/head、提交和检查状态；报告已知事实。CI 仍在运行就写“运行中”，创建 PR 不代表通过评审或已合并。超时先按 head/base 查询是否已创建，找到则复用，不盲目重试。

## 7. 失败、冲突与收尾

| 情况 | 处理方式 |
| --- | --- |
| Push 被 non-fast-forward 拒绝 | 远端功能分支含本地没有的提交；先 Fetch 该分支并查看分叉，确认是否有人共用分支，提出保留双方内容的同步方案。未经确认不 merge/rebase/pull；绝不强推 |
| GitHub 要求更新分支 | 先完成当前内容保全，展示将整合的分支与提交、冲突风险；确认后按项目策略 merge 或 rebase。已公开分支避免需要强推的 rebase 方案 |
| 文本冲突 | `git status` 与 `git diff --name-only --diff-filter=U` 确认文件；解释共同基线和双方意图，结合语义解决。不要整批选 ours/theirs；rebase 的 ours/theirs 含义尤其不能凭直觉使用 |
| `.gd` / `.tscn` / `.tres` 冲突 | 文本可读不代表语义正确；核对节点路径、信号、资源 ID/UID 和引用，解析后实际打开相关场景验证 |
| `.blend` / `.glb` 二进制冲突 | 保留双方提交版本，交资产负责人选版本、在源文件合成或重导出；不拼接字节，不擅自覆盖。LFS 只改变存储，不提供语义合并 |
| LFS 锁失败或服务不支持 | 核实 `git lfs locks --verify` 与服务器能力；锁是协作机制而非不可绕过的权限屏障。不要禁用检查或抢占他人锁；转为明确负责人串行交付 |
| Worktree/分支名称占用 | 查看所有者与正在运行的任务，复用必须有明确交接；不强制覆盖、不删目录重来 |

合并和清理是独立阶段。准备具体方案后获取确认：哪个 PR、目标分支、合并方法、检查状态；清理哪个绝对路径及分支、是否还有 Agent/编辑器运行、所有提交是否保留、是否存在 ignored 文件或本地导出物。即使 `git status` 干净，ignored 文件也可能有价值。确认后使用正常 `git worktree remove <路径>`、非强制分支删除等原生能力；拒绝清理时先调查，不升级强制参数。Codex 管理的 Worktree 通过其既有管理流程清理。

交付时用非程序成员能理解的话说明：“改动已保存在本机 / 已上传到功能分支 / 已发起 PR / 等待负责人合并”的准确阶段，附提交 SHA、PR 链接、打开的 Godot 路径与最短验收步骤。不要把 Push 说成已进入主版本。

## 官方依据

- [Git Worktree：隔离与共享、禁止重复 checkout 的保护、清理语义](https://git-scm.com/docs/git-worktree)
- [Git switch：创建分支及 --no-track](https://git-scm.com/docs/git-switch)
- [Git fetch：refspec、refmap、远端引用更新](https://git-scm.com/docs/git-fetch)
- [Git status：暂存区与工作区状态](https://git-scm.com/docs/git-status)
- [Git push：显式 refspec、Push URL、upstream 与非快进保护](https://git-scm.com/docs/git-push)
- [GitHub：处理 non-fast-forward](https://docs.github.com/en/get-started/using-git/dealing-with-non-fast-forward-errors)
- [GitHub CLI：gh pr create，--head、--body-file、--dry-run](https://cli.github.com/manual/gh_pr_create)
- [Git LFS：lock](https://github.com/git-lfs/git-lfs/blob/main/docs/man/git-lfs-lock.adoc)、[locks --verify](https://github.com/git-lfs/git-lfs/blob/main/docs/man/git-lfs-locks.adoc)、[locksverify 配置](https://github.com/git-lfs/git-lfs/blob/main/docs/man/git-lfs-config.adoc)
