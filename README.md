# Z-from-Moon

团队共同维护的 Godot 游戏项目仓库，用于协作开发程序代码、场景和美术资产，以及维护 Codex 协作工具。

目前首次发布的是团队协作 Skill 和使用教程；尚未加入游戏工程或美术资产。具体游戏目录、Godot 版本、资产规范与职责分工，待接入真实工程时按项目现状确定，不以本文示例替代实际约定。

## 团队协作 Skill

- [安装、更新、卸载与日常使用](tools/codex/README.md)
- [三个使用示例：程序、美术、双 Agent 并行](tools/codex/docs/examples.md)
- [项目规范建议与 AGENTS.md 模板](tools/codex/docs/project-rules.md)
- [Skill 操作规则](tools/codex/skills/godot-team-collab/SKILL.md)

在 Codex 中复制以下提示即可安装首次发布版本；这不是终端命令：

```text
$skill-installer 请从
https://github.com/seanlee111/Z-from-Moon/tree/godot-team-collab-v1.0.0/tools/codex/skills/godot-team-collab
安装 godot-team-collab。先核对来源和版本，再使用现有安装器。
目标指定为我的用户目录下 .agents/skills；不要依赖安装器的默认目标。
先检查该目录及 CODEX_HOME/skills 是否已有同名 Skill；有则停止，不覆盖或重复安装。
完成后告诉我实际安装路径、来源链接、版本与完整 commit SHA，并确认 Codex 能发现它。
```

安装完成后，在游戏项目中使用：

```text
使用 $godot-team-collab，只检查我的环境和当前仓库，读取已有项目说明，列出缺项，不修改项目、配置或远端。
```

`tools/codex/` 是 Skill 发布源目录，克隆本仓库不会自动安装 Skill，也不会更改个人 Codex 配置。仓库级安装是另一项明确操作，见安装教程。

## 仓库内容与后续协作

```text
README.md
tools/codex/                         # Codex 协作工具与教程
  README.md
  docs/
  skills/godot-team-collab/
```

游戏代码、场景和资产之后可以继续放在此仓库，不需要再建一个仓库。接入时保留真实工程结构，把入口、版本、已有验证方式、资产目录与负责人写入项目 `AGENTS.md` 和资产规范。本次没有创建游戏目录、启用模板规则或配置 LFS。

后续变更采用功能分支和 PR；人类与 Agent 并行时各用独立 Worktree，并明确文件负责人。二进制模型按项目规范使用 Git LFS 和资产交接，源文件与导出物由同一负责人维护。Skill 不会自动配置服务器端权限；管理员应另行配置并验证 `main` 保护规则。

`godot-team-collab-v1.0.0` 是协作 Skill 的发布标签，不是游戏发行版本。团队安装时固定该标签或完整提交 SHA；升级时发布新标签，不移动旧标签。首次空仓库发布建立 `main`，后续更新通过 PR 审阅。
