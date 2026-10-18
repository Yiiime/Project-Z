# 环境检查与成员入组

先判断操作系统、当前 shell、已有应用与团队版本约束。下面是诊断和安装说明，不是需要一次运行的脚本。已有可用工具就复用；检查失败应区分「未安装」「不在 PATH」「未登录」「网络失败」「版本不匹配」。不因工具缺失自动安装、升级或改全局配置。

## 最小检查

| 项目 | 只读检查 | 成功意味着什么 / 还要核对什么 |
| --- | --- | --- |
| Git | `git --version`；仓库内 `git status --short --branch` | 可运行；不代表仓库/分支正确 |
| 提交身份 | `git config --show-origin --get user.name` 和 `git config --show-origin --get user.email` | 有身份来源；确认是本人，不在报告公开邮箱 |
| Git LFS | `git lfs version`；`git config --get filter.lfs.process` | 工具与过滤器存在；仍需检查仓库属性、hooks 和服务端权限 |
| GitHub CLI | `gh --version`；`gh auth status --hostname github.com` | CLI 与当前账号认证状态；报告通过/失败和必要账号即可，不复制认证输出 |
| 目标仓库访问 | 已确认仓库后 `gh repo view OWNER/REPO --json nameWithOwner,viewerPermission,defaultBranchRef` | 识别是否有读写权限及实际默认分支；不能据此声称分支保护已生效 |
| Git 传输认证 | 已确认无凭证嵌入 URL 的远端后 `git ls-remote origin refs/heads/main` | Git 能读该远端；不同于 `gh` API 登录，也不等于有 push 权限 |
| Codex | CLI 用户运行 `codex --version`、`codex login status`；应用用户查看已安装版本及登录状态 | 桌面应用能工作时，CLI 缺失不阻断普通 Skill 使用 |
| Godot | 使用实际可执行文件运行 `--version` | 与项目锁定版本、Standard/.NET 版本、导出模板匹配；不启动工程做「只读」检查 |
| Blender | 需要编辑 `.blend` 或 Godot 直导时查看应用版本 / `blender --version` | 已装 GUI 但 PATH 不含命令很常见；GLB 使用者未必需要 Blender |

macOS/Linux 用 `command -v git` 查可执行路径；PowerShell 用 `Get-Command git`。Godot 命令可能是 `godot`、`godot4` 或应用内二进制，不把命令名当成固定接口。macOS 常见示例是 `/Applications/Godot.app/Contents/MacOS/Godot`，应先确认存在。

`gh auth status` 可能显示脱敏 token 信息；不要加 `--show-token`，不要运行 `gh auth token`，也不读取/打印 Codex 或 GitHub 凭证文件。远端 URL 或 LFS endpoint 可能含凭证，发现时只报告配置位置，不原样输出。

报告只需一张「通过 / 缺失 / 需本人操作 / 未验证」表和一个最先需要解决的问题。API 或网络超时不能诊断为未登录。

## 安装或修复指引

按需给本人一个步骤；选择团队支持的工具版本，不给整个团队自动升级到最新版。

| 工具 | 官方入口与简明操作 |
| --- | --- |
| Git | 从 [Git 安装入口](https://git-scm.com/downloads) 选系统；Windows 可使用 Git for Windows 的 Git Bash 执行本文 POSIX 示例 |
| Git LFS | 按 [Git LFS 安装说明](https://docs.github.com/en/repositories/working-with-files/managing-large-files/installing-git-large-file-storage) 安装；进入已确认的本地仓库后配置过滤器和 hook |
| GitHub CLI | [GitHub CLI 安装](https://cli.github.com/)；本人执行 `gh auth login --hostname github.com` 并完成浏览器认证，再检查状态 |
| Codex | 用 [Codex 官方安装说明](https://developers.openai.com/codex/cli/) 或已安装的桌面应用；CLI 可用官方包 `npm install -g @openai/codex`，需已有 Node/npm，属于全局安装；本人完成登录 |
| Godot | 从 [Godot 下载](https://godotengine.org/download/) 或 [历史版本](https://godotengine.org/download/archive/) 取团队指定版本；C# 项目需 .NET 版和匹配 SDK；导出时才需要匹配导出模板 |
| Blender | 从 [Blender 下载](https://www.blender.org/download/) 或团队固定版本安装；GLB 资产消费者不应为此无条件安装 Blender |

macOS 已有 Homebrew 时，`brew install git git-lfs gh` 可安装三个 CLI；这是会改变机器状态的可选安装操作，不是环境探测。Windows/Linux 按官方安装页选择对应安装包或包管理器，避免复制未经核实的包名和版本号。

在仓库内配置本人提交身份时，优先仓库级 `git config --local user.name "本人选择的署名"` 与 `git config --local user.email "本人选择的提交邮箱"`；它会写本仓库配置。全局身份只有本人明确要全机器使用时才配置，不能拿其他成员或 Agent 的身份代填。

LFS 首次初始化可用 `git lfs install --local`，会修改仓库 Git 配置并安装 hook。先查看已有 hooks、`core.hooksPath` 以及 LFS 配置；已有自定义 hook 不覆盖、不使用 `--force`，按 LFS 的提示和团队 hook 管理方式整合。Worktree 共用多数 Git 配置和 hooks，指定一人初始化，不让每个 Agent 并发重装。初始化不等于 `.gitattributes` 已配置，后者另见 [资产说明](assets-conflicts.md)。

## 首次获取游戏项目

仓库地址、目标目录和克隆授权明确后才执行。以下仅展示形式，`OWNER/REPO` 与路径必须替换为真实值：

```bash
git clone https://github.com/OWNER/REPO.git game
cd game
git lfs pull
```

`clone` 创建新目录并下载历史；目标目录已有内容时停止。LFS 客户端应先安装；`git lfs pull` 下载当前 checkout 对应的 LFS 对象并可能更新工作区，必须在新克隆/干净工作区运行，不用于覆盖美术尚未提交的资源。私有仓库由本人用已有 Git 凭证访问，不把 token 填进 URL。下载失败时保留错误原因，Git clone 成功不等于大文件已下载完整。

先读项目规范再用指定版本打开真实 `project.godot`。首次 Godot 导入会写 `.godot/` 缓存并可能生成 `.import` / `.uid` 等文件；这不是只读环境检查。若导入产生大批意外修改，先停下来检查引擎版本、LFS 下载和项目设置。

## 官方依据

核对日期：2026-10-18。未来安装前复核工具页面和团队固定版本。

- [Codex Build skills](https://learn.chatgpt.com/docs/build-skills)：独立 Skill、安装与发现方式。
- [GitHub CLI auth status](https://cli.github.com/manual/gh_auth_status)、[auth login](https://cli.github.com/manual/gh_auth_login)：认证诊断与本人登录。
- [Git LFS install](https://github.com/git-lfs/git-lfs/blob/main/docs/man/git-lfs-install.adoc)：过滤器、hook 与 `--local`。
- [Godot 命令行](https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html)：版本、项目路径与 headless 参数。
