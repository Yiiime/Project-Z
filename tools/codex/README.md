# Godot 团队协作 Skill

面向 6 人 Godot 团队，让程序、美术、策划与 Coding Agent 用自然语言完成「理解项目 → 开任务分支 → 独立开发 → 检查变更 → Commit / Push / PR」。底层采用 GitHub Flow、Git Worktree 和 Git LFS，无自定义 Git 工具，无必需 MCP，也没有自动执行的脚本。

**成员只需说清目标、范围和希望交付到哪一步。** Skill 会读取真实项目规范，保护已有改动；合并和清理需要对具体方案确认。Skill 不是权限系统，团队仍需配置 GitHub 分支保护。

## 安装

### 仓库与发布版本

本套工具位于团队项目仓库 [seanlee111/Z-from-Moon](https://github.com/seanlee111/Z-from-Moon) 的 `tools/codex/`。其中 `skills/godot-team-collab/` 是完整可安装 Skill。首次发布标签为 `godot-team-collab-v1.0.0`；标签只标记协作 Skill 版本，不代表游戏版本。游戏代码、美术资产与其他项目文档可以继续在同一仓库按项目约定维护。

建议统一固定 tag / commit，避免六个人安装不断变化的 `main`；记录 tag 实际指向的完整 commit SHA，方便跨聊天核对更新与回滚。私有仓库需要成员有读取权限，用本人现有认证访问，不共享 token。

### 成员从 GitHub 安装（推荐）

在 Codex 直接复制以下内容。这是 **Codex 提示词，不是终端命令**，链接固定到首次发布版本：

```text
$skill-installer 请从
https://github.com/seanlee111/Z-from-Moon/tree/godot-team-collab-v1.0.0/tools/codex/skills/godot-team-collab
安装 godot-team-collab。先核对来源和版本，再使用现有安装器。
目标指定为我的用户目录下 .agents/skills；不要依赖安装器的默认目标。
先检查该目录及 CODEX_HOME/skills 是否已有同名 Skill；有则停止并报告，不覆盖或重复安装。
完成后告诉我实际安装路径、来源链接、版本与解析到的完整 commit SHA，并确认 Codex 能发现它。
```

当前官方本地发现目录包括用户级 `$HOME/.agents/skills` 和项目级 `.agents/skills`。本次检查的内置 `skill-installer` 默认使用 `$CODEX_HOME/skills`（通常 `~/.codex/skills`），因此这里显式指定目标；安装器有 `--dest` 参数，发现目标已存在会拒绝覆盖。不要把同一个名字同时装在多个扫描目录。依据：[Codex 官方 Skill 规范](https://learn.chatgpt.com/docs/build-skills)、[OpenAI 安装器](https://github.com/openai/skills/tree/main/skills/.system/skill-installer)。

技能变更会自动被发现；若未出现，重启 Codex。CLI / IDE 可用 `/skills` 或输入 `$godot-team-collab` 选择，桌面界面可通过技能选择器选择。先发一次只读请求验证：

```text
使用 $godot-team-collab，只检查我的环境和这个 Godot 仓库，列出缺项，不修改配置、项目或远端。
```

安装成功不等于 GitHub、Git LFS 或 Godot 已配置完成。若 Codex 中没有内置安装器，可采用下面的本地复制方式，不要下载来历不明的替代安装脚本。

### 从本地文件安装 / 跟随项目分发

从本仓库完整复制 `tools/codex/skills/godot-team-collab/` 到以下 **二选一** 的位置：

- 个人使用：`$HOME/.agents/skills/godot-team-collab/`。
- 全团队随游戏仓库同步：`<游戏仓库>/.agents/skills/godot-team-collab/`，由负责人通过正常功能分支和 PR 引入。

目标最终应是 `…/godot-team-collab/SKILL.md`，不要多嵌套一层。本地可以直接让 Codex 复制：

```text
请把 Z-from-Moon/tools/codex/skills/godot-team-collab 文件夹完整复制到用户级
.agents/skills/godot-team-collab，先检查目标及其他扫描位置没有同名副本。
不要覆盖既有内容，也不要把整个分发仓库复制进去。
```

若是项目级安装，必须已将 Skill 提交入库，其他 Worktree 才会从其基准提交带上它。不要为安装本 Skill 顺便改 Godot 工程。发布只提供文件与说明，不会自动安装 Skill。`tools/codex/skills/` 是发布源目录，不是 `.agents/skills/` 自动发现目录；克隆本仓库不等于安装。

当前官方推荐用 Plugin 做更广泛的可安装分发，独立 Skill 仍支持本地和仓库使用。本包按团队当前需求采用 GitHub 文件夹 + 现有安装器；未来需要目录上架、集中插件管理或组合连接器时再包装为 Plugin。[官方分发说明](https://learn.chatgpt.com/docs/build-skills)

后续修改在任务分支完成并经 PR 审阅；采用新的 Skill 版本标签，不移动已发布标签。无需为每次游戏代码或资产提交重新发布 Skill。

## 更新与卸载

**更新不是对已安装目录执行 `git pull`。** 安装器通常复制文件，不会给单个 Skill 建独立 Git 工作区，也不会直接覆盖已有目标。

个人安装可用下面的请求。将来源版本与实际安装路径填完整：

```text
请将 godot-team-collab 从当前已记录版本更新到负责人给出的新 tag / commit。
先在技能扫描目录外获取新版本，检查 diff、来源及本地是否有定制修改。
列出实际安装路径、新版本和备份路径，等我确认这次替换。
确认后把旧版移到用户目录的 codex-skill-backups 中，再安装新版并验证发现情况。
不要覆盖丢失本地修改，不要把旧版留在任何 skills 扫描目录里。
```

旧版备份应包含版本信息，放在例如 `~/codex-skill-backups/godot-team-collab/<日期-旧版本>/`。更新失败时先检查新目标是否已部分创建；需要回滚时确认后隔离新副本、恢复旧副本，禁止盲删。项目级安装由负责人通过 PR 更新，其余成员按项目正常同步流程获取，不由六个人分别修改版本。

卸载时让 Codex 定位这个 Skill 的**唯一实际目录**，说明是否为共享项目文件，并确认具体对象；随后将该目录移到扫描范围外的备份目录。不要删除整个 `.agents/skills`、`.codex`，不要影响其他 Skill。重启或刷新后检查不再发现；如仍出现，应查另一份同名副本。项目级卸载属于仓库变更，应走 PR。纯临时停用也可用 Codex 的 `[[skills.config]]`，指定此 Skill 的绝对 `SKILL.md` 路径并设置 `enabled = false`；更改配置后重启。[官方停用说明](https://learn.chatgpt.com/docs/build-skills)

## 每天怎么用

| 你要做什么 | 可以直接对 Codex 说 |
| --- | --- |
| 新成员入组 | `使用 $godot-team-collab 检查环境，告诉我下一项需要本人完成的操作。` |
| 开始开发 | `使用 $godot-team-collab 实现这个功能，先找可复用代码，从最新 main 开任务分支，只开发并验证，先不提交或上传。` |
| 交付变更 | `使用 $godot-team-collab 检查本任务变更，提交、推送功能分支并创建 Draft PR；不要合并。` |
| 看不懂冲突 | `使用 $godot-team-collab 解释冲突和两边意图，先保留现场，给出可审阅的处理方案。` |
| 多 Agent 开发 | `使用 $godot-team-collab 给两个任务分配不同 Worktree、分支和文件范围，保留我的当前目录。` |

给非程序成员的五个词：**分支**是这次任务的版本线；**Worktree**是可同时打开的另一个工作文件夹；**Commit**是本地存档；**Push**才上传到 GitHub；**PR**是请团队检查并决定是否纳入 `main` 的变更单。PR 创建成功不代表已经合并或游戏验收通过。

开发结束时先看 Codex 给出的改动摘要与验证结果，再在 GitHub 检查 PR 的 Files changed 和游戏截图/运行说明。美术还需检查模型、材质、动画和尺度；程序运行通过不能代替视觉验收。合并由维护者完成，清理分支和工作目录另行确认。

## 文件与职责

| 文件 | 读者 / 用途 |
| --- | --- |
| [SKILL.md](skills/godot-team-collab/SKILL.md) | Agent 的触发条件、项目理解、权限边界与总流程 |
| [环境引导](skills/godot-team-collab/references/environment.md) | 入组与工具异常；安装和登录由本人授权 |
| [Git 工作流](skills/godot-team-collab/references/workflow.md) | 分支、Worktree、提交与 PR 的准确操作和失败处理 |
| [资产与冲突](skills/godot-team-collab/references/assets-conflicts.md) | Godot 文本资源、Blender/GLB、LFS、负责人和锁 |
| [三个示例](docs/examples.md) | 程序、美术、双 Agent 的可复制请求和验收方式 |
| [项目规范建议](docs/project-rules.md) | 负责人把项目事实写入 AGENTS.md、资产规范和 GitHub 规则 |

通用流程属于 Skill；Godot 版本、项目入口、模块位置、六人的职责、资产规格和验收命令属于项目 `AGENTS.md` 及其引用文档。模板只是建议，本包没有为任何现有游戏创建或修改这些配置。

## 维护与验证范围

格式基于 2026-10-18 查阅的官方规范：`SKILL.md` 的 YAML 必填 `name`、`description`；复杂操作按需加载 `references/`；`agents/openai.yaml` 提供可选 UI 元数据。正常自动发现开启，不要求专属 MCP。[Codex Build skills](https://learn.chatgpt.com/docs/build-skills)

本包不含测试脚本、Git hooks、CI、真实模型或游戏项目。维护者可复用 Codex 自带 `skill-creator` 的 `quick_validate.py` 做格式检查；格式正确不证明实际 GitHub 权限、LFS 锁或游戏运行正确。上线团队前，应在授权的练习仓库走一次程序和美术流程，再推广到真实工程。
