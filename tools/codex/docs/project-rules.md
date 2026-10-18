# 项目规范建议：Skill 与项目各管什么

这是待项目负责人采用的建议，不会自动修改工程。优先保留项目已有结构、命名和 GitHub Flow；不要为了匹配此文档重排目录。下面所有示例路径、账号和版本都必须按真实项目填写。

## 职责边界

| 放在哪里 | 应包含什么 |
| --- | --- |
| 本 Skill | 通用环境检查、先读项目再改动、分支/Worktree/交付步骤、保护修改、授权与冲突处理 |
| 项目 `AGENTS.md` | 项目版本和入口、架构导航、禁止重复实现的模块、文件职责、项目必须执行的验证、保护分支与关键安全规则 |
| 资产规范 / 架构文档 | 大量细则：命名、单位/朝向/原点、三角面与纹理预算、动画、碰撞、源文件/导出关系、模块接口；由 AGENTS.md 链接 |
| `.gitignore` / `.gitattributes` | 实际缓存排除、文本换行策略、LFS 跟踪和锁定属性；文本说明不能替代配置 |
| `.github/CODEOWNERS` | 路径到真实审核人的映射；不能代替编辑排期或 LFS 锁 |
| GitHub Rulesets / branch protection | 服务器端限制直接 push、强推、删除、合并条件；Skill 本身无法强制实施 |
| 现有 Issue / 任务看板 / PR | 本次任务负责人、分支、Worktree、修改范围、资产锁、依赖和验收进度 |

关键安全约束也要简写进 AGENTS.md，因为有人未安装或未触发 Skill。避免复制整份 Skill，减少两处流程漂移。

## 六人团队的最小协作安排

确定一名集成人和一名备份，负责 `main` 的 PR 顺序及共享文件；代码、场景、资产各指定负责人和备份，可以由同一个人兼任，不据此假定团队职业构成。每条任务至少登记「负责人 / 路径 / 分支 / 状态 / 依赖」，复用已有看板，不再创造一个调度系统。

`project.godot`、入口总场景、大型关卡、Autoload、Input Map、导出配置和公共资源属于容易争用的位置。按任务指定写入者，其他成员通过独立子场景/资源提出改动；共享接口先约定再并行。二进制源文件与其 GLB 等导出物由同一负责人维护。

Worktree 只隔离本地文件、HEAD 与 index，多数仓库配置和引用仍共享。同一台机器统一由集成人协调 fetch、配置、分支删除和清理；各成员自己的独立 clone 则有独立本地 Git 元数据。共享 GitHub 身份的 Agent 仍需任务级所有权，LFS 的账号锁无法区分它们。[Git Worktree](https://git-scm.com/docs/git-worktree)

## 可摘录进 AGENTS.md 的模板

将下列内容合入已有文件，保留已有约定；先替换所有 `待填写`，不要原样作为生效规范提交。模板嵌在此文档中，不是本分发仓库的执行指令。

```markdown
# 项目协作约定

## 项目事实
- Godot 版本及 Standard/.NET：待填写；不自动升级工程。
- project.godot 路径与入口场景：待填写。
- 架构与可复用模块：待填写文档路径、核心模块和接口。
- 资产规范：待填写文档路径；源文件目录与导出目录：待填写。
- 已有检查/运行命令及工作目录：待填写；人工验收场景：待填写。
- 主分支：main；可信上游仓库及工作 remote：待填写。

## 任务与所有权
- 开工先读 README、本文件、目标子目录规范和资产/架构文档，先找已有实现。
- 一个任务一个分支、一个写入者、一个工作目录；并行写入使用独立 Worktree。
- 任务负责人和路径占用在待填写的现有看板登记，登记不等于已获得别人授权。
- 集成人/备份、代码/场景/资产负责人：待填写真实人或团队。
- project.godot、入口场景、公共接口：由待填写的负责人协调变更。
- 同一 .blend 与导出的 .glb 属于同一任务；按资产规范锁定/认领，禁止并行抢改。

## 安全与交付
- 不在 main 或其他受保护分支开发或直接 push；不强推、跳过 hooks 或保护规则。
- 不覆盖、stash、回退或提交他人未提交内容；预先 staged 的内容也属于既有修改。
- 先确认 Git 根目录、分支、remote、工作区状态，再按任务显式暂存路径。
- Commit / Push / PR 执行到用户已授权的阶段；合并、历史改写和清理需具体方案确认。
- 不自动采用 ours/theirs 解决资源冲突；二进制由负责人决定并重新导出。
- 交付写清行为、范围、检查结果、未验证事项、资产源/导出对应关系。
- 游戏运行、视觉效果、玩法与设备验收分别记录，不用 headless 导入替代人工验收。
```

Codex 会从项目根目录向当前目录读取指令；目标子目录也可能有 `AGENTS.override.md`。应让模块规范靠近对应目录，并在修改前主动检查。不要把复杂资产规范全部塞进一个巨大 AGENTS.md。[官方 AGENTS.md 机制](https://learn.chatgpt.com/docs/agent-configuration/agents-md)

## Git 文件配置建议

以下 Godot 忽略规则适用于 **4.1+**，项目根可位于子目录，因此使用递归目录规则；采用前仍核查已有配置。Godot 3.x / 4.0 的缓存和导出凭证另按对应版本文档处理。

```gitignore
# Godot 4.1+ 的缓存及本地编辑器数据
**/.godot/

# 系统文件与 Blender 本地轮换备份（不删除磁盘上的备份）
.DS_Store
Thumbs.db
*.blend1
*.blend2
```

不要加入 `*.import`、`*.uid`、`*.glb` 或全部 `*.cfg` 的笼统忽略。资源旁 `.import` 保存导入设置；Godot 4.4+ 的脚本/着色器 `.uid` 需要版本控制。`export_presets.cfg` 在 4.1+ 可追踪，但仍检查里面是否有项目自行填写的敏感内容；不提交 `.godot/export_credentials.cfg`。`.gitignore` 不会自动移除已经追踪的缓存，处理已追踪内容是另一个明确范围的维护变更。[Godot 版本控制指南](https://docs.godotengine.org/en/stable/tutorials/best_practices/version_control_systems.html)

新项目可考虑以下 `.gitattributes`，已有项目只增补确有必要的规则，不顺手全仓库重新标准化：

```gitattributes
*.gd text eol=lf
*.tscn text eol=lf
*.tres text eol=lf
*.uid text eol=lf
*.import text eol=lf
*.godot text eol=lf

*.blend filter=lfs diff=lfs merge=lfs -text lockable
*.glb filter=lfs diff=lfs merge=lfs -text lockable
```

这是团队建议，不是 Godot 必需配置。是否把 PNG、音频等其他资源放入 LFS，按文件大小、频繁改动程度、存储与下载成本决定；不要把 `.gd/.tscn/.tres` 放入 LFS 来掩盖冲突。`lockable` 不会自动获取远端锁，也不是服务器强制互斥。锁服务和客户端检查需要验证；不支持时使用负责人串行编辑，明确记录限制。

LFS 规则要和资源一起进入 PR；已追踪的二进制不会因新增规则自动迁移旧历史。需要转存当前文件或迁移历史时先做独立方案、检查配额、安排协作窗口并确认，不在普通功能任务中执行 `git lfs migrate`。版本库里的 LFS 指针依赖独立对象存储，Git 历史和 GitHub 普通源码 ZIP 不保证包含全部可用资产。[GitHub 配置 LFS](https://docs.github.com/en/repositories/working-with-files/managing-large-files/configuring-git-large-file-storage)

## GitHub 设置建议

由仓库管理员实际配置并读回验证，不由 Skill 在入组时擅自修改。对于小团队，建议保护 `main`：要求 PR、至少一名非作者批准、关键路径 Code Owner 审阅、已有必要检查通过；阻止强推与删除，并核查管理员/bypass 例外。不配置尚未存在的 CI 检查名，也不要宣称仓库一定支持所有规则，先核对 GitHub 方案及仓库权限。[受保护分支官方说明](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches)

CODEOWNERS 示例，路径和账号都必须替换；不存在或没有必要权限的团队不能充当有效 owner：

```text
/game/scripts/       @ORG/game-code
/game/scenes/        @ORG/game-scenes
/art/source/         @ORG/game-art
/game/assets/models/ @ORG/game-art
/game/project.godot  @ORG/game-integrators
/.gitattributes      @ORG/game-integrators
/.github/            @ORG/game-integrators
```

CODEOWNERS 用于审核，不会锁定编辑，也不会单独强制批准。需要结合分支规则；同一行有多个 owner 通常不是「每个人都必须批准」。注意规则顺序会影响匹配，采用前核对实际路径。[GitHub CODEOWNERS](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-code-owners)

复用项目已有 PR 模板；没有时，可在后续获授权的仓库规范 PR 中添加这几个栏目：行为变化与验收方法、涉及路径、检查结果/未验证事项、截图或录屏、源文件与导出参数、资产负责人/锁与依赖、是否需要先合并另一 PR。不要要求每个小补丁填写无关长表。

## 推荐落地顺序

1. 负责人填好项目版本、入口、职责和现有验证方式；把必要规范通过 PR 写入游戏仓库。
2. 管理员配置并验证 `main` 保护、审阅人和已存在的检查；确认 Git LFS 服务、容量和锁行为。
3. 成员安装同一 Skill 版本；在授权的练习仓库走通 [三个示例](examples.md)，再开始真实任务。

这些步骤是团队后续采纳建议；本次文件交付没有执行任何仓库配置、安装、迁移或实际 PR 操作。
