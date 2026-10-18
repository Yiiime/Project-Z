# Godot 资产、LFS 与冲突处理

按需读取：涉及场景、资源导入、模型、美术交付、LFS 或已有冲突时使用。命令均为操作模板；先替换实际路径并确认当前工作目录。本文件不授权合并、变基、删除或覆盖。

## 先识别项目版本与资产约定

先读取项目 `AGENTS.md`、`README.md`、资产规范、`.gitignore`、`.gitattributes`；核对固定的 Godot / Blender 版本、Godot 工程根目录、源文件位置、资产负责人和验收场景。不因本 Skill 的示例而改写现有目录或迁移资产。

| 文件或版本 | 处理规则 |
| --- | --- |
| Godot 4.1+ 的 `.godot/` | 生成缓存，不提交；其中 `.godot/imported/` 是导入产物。不要为例行检查删除缓存。 |
| 源资源旁的 `<asset>.import` | 提交，包含该资源的导入配置与元数据。`*.import` 文件与缓存目录不是一回事，不能整体忽略。 |
| Godot 4.4+ 的 `<script>.gd.uid`、着色器等 `.uid` 文件 | 提交；移动或删除源文件时一起处理对应 `.uid`。不要为消除警告随意重建 UID。 |
| Godot 4.1+ 的 `export_presets.cfg` | 通常可以提交；仍检查项目自定义字段和机器绝对路径。 |
| `.godot/export_credentials.cfg` | 含密码、密钥等导出凭证，不提交，也不将内容贴到聊天或 PR。 |
| Godot 3.x / 4.0 | 不套用 4.1+ 的完整忽略清单；旧版 `export_presets.cfg` 可能含凭证。先查对应版本文档再处理。Godot 3 的 `.import/` 缓存目录与资源旁 `*.import` 文件同样需要区分。 |

这些是官方版本行为，见 [Godot 版本控制说明](https://docs.godotengine.org/en/stable/tutorials/best_practices/version_control_systems.html)、[导入过程](https://docs.godotengine.org/en/stable/tutorials/assets_pipeline/import_process.html)、[4.4 UID 变更](https://godotengine.org/article/uid-changes-coming-to-godot-4-4/)、[导出配置](https://docs.godotengine.org/en/stable/tutorials/export/exporting_projects.html) 和 [Godot 3.6 版本控制说明](https://docs.godotengine.org/en/3.6/tutorials/best_practices/version_control_systems.html)。固定团队使用的版本；不要顺便升级引擎或批量重存整个工程。

## 文本与二进制分别处理

- `.gd`、`.tscn`、`.tres`、`project.godot`、`.import`、`.uid` 通常保留普通 Git 文本差异。不要为避免冲突一律改成 LFS。
- `.tscn` / `.tres` 即使没有 Git 冲突，也可能存在节点路径、信号、资源引用、动画轨道等语义冲突。检查 `ExtResource` / `SubResource` 的引用对应关系、资源 UID、父子路径和节点唯一标识（若该引擎版本生成它）；不要机械拼接或批量重编号。格式依据见 [TSCN 官方说明](https://docs.godotengine.org/en/stable/engine_details/file_formats/tscn.html)。
- `.blend`、`.glb` 等二进制不能由 Git 自动完成内容级合并。LFS 降低 Git 历史的资产负担，不会让二进制变得可合并。发生冲突时由资产负责人比较双方文件，在 Blender 等源工具中整合，再重新导出；不得用 LFS 指针文本合并冒充模型合并。
- **团队建议**：共享主场景、`project.godot`、公共材质、骨骼和单个二进制资产同一时段只安排一位写入负责人。将独立玩法拆成独立子场景可减少冲突；Worktree 隔离目录，不隔离最终资产冲突。

## GLB 交付：先遵守已有管线

官方支持 glTF 2.0，含 `.gltf` 与 `.glb`。直接导入 `.blend` 会调用本机 Blender，因此打开这类工程的成员及 CI 需要可用的 Blender；这与“只交付导出的 GLB”是两种管线。见 [Godot 支持的 3D 格式](https://docs.godotengine.org/en/stable/tutorials/assets_pipeline/importing_3d_scenes/available_formats.html)。

**六人团队的建议默认值**：美术维护 `.blend` 源文件，Godot 使用导出的 `.glb`；源文件可以放在工程根目录之外但仍位于 Git 仓库内，具体位置写入项目规范。已有 `.blend` 直导管线则保持一致，不额外制造重复 GLB。

对于采用“源 Blend + 导出 GLB”的项目：

1. 确认该资产及相关材质、纹理、骨骼的负责人和锁状态；固定源文件与导出文件的对应路径。只读检查通过不等于已获编辑权。
2. 保存 Blender 源文件，用项目约定的 Blender 版本和导出设置生成 GLB；源文件及实际导出结果须属于同一次交付。若仅收到 GLB，明确写出源文件未提供，不能声称可复现导出。
3. 在该功能分支的 Godot Worktree 中导入；检查模型方向、尺度、原点、材质、贴图、骨骼、动画名称及播放、碰撞约定和约定的面数/大小预算。外部贴图和依赖文件也须存在。
4. 将修改的 `.blend`、`.glb`、必要贴图、资源旁 `.import`、相关场景和实际生成的 `.uid` 一起纳入同一交付；逐路径检查，不使用无差别 `git add .`。不提交 `.godot/` 导入缓存或 Blender 临时备份。
5. PR 说明源文件/导出路径、工具版本、主要变化、预览与验收场景、已验证和未验证项。导入成功不等于画面、动画和游戏内交互验收通过。

若导入命令或编辑器导致范围外重写，停止暂存这些文件并报告，不能直接丢弃；先查版本或导入设置差异。

## 检查 Git LFS，再编辑资产

以下为在仓库根目录运行的只读检查，示例路径须替换：

```sh
git lfs version
git check-attr filter diff merge text lockable -- "art_source/robot.blend" "game/assets/robot.glb"
git lfs ls-files
git lfs status
git lfs locks
```

`check-attr` 显示该路径实际生效的属性，比只浏览根目录 `.gitattributes` 更可靠；LFS 路径通常应显示 `filter: lfs`、`diff: lfs`、`merge: lfs`、`text: unset`。`lockable: set` 表示采用锁定编辑；`unspecified` 不代表已锁定或服务器支持锁。`ls-files` 显示已跟踪的 LFS 对象，`status` 查看待提交/上传情况，`locks` 查询远端锁。全部成功仍不能证明远端已收到本次资产。

若项目已采用 LFS，遵守既有配置。若尚未采用，先提交方案给维护者确认，建议按实际资产类型配置：

```gitattributes
*.blend filter=lfs diff=lfs merge=lfs -text lockable
*.glb filter=lfs diff=lfs merge=lfs -text lockable
```

这只是模板，不能直接覆盖现有 `.gitattributes`；是否启用 `lockable` 取决于服务器支持和团队约定。`git lfs install` 配置客户端过滤器/钩子，`git lfs track` 才写入跟踪模式；添加规则不会自动改写既有历史。**本 Skill 不执行 LFS 历史迁移、为迁移改写仓库历史或强制推送。** 已入库的大文件需要迁移时，交给维护者另立方案。见 [Git LFS track 手册](https://github.com/git-lfs/git-lfs/blob/main/docs/man/git-lfs-track.adoc) 与 [Git LFS 安装手册](https://github.com/git-lfs/git-lfs/blob/main/docs/man/git-lfs-install.adoc)。

如果 GLB/Blend 实际是 `version https://git-lfs.github.com/spec/v1` 开头的短文本，说明拿到的是 LFS 指针；不能作为正常模型验收。先确认 LFS 安装、授权、远端对象和工作区已有修改，再按项目流程获取缺失对象，不把下载失败解释成模型损坏。

## 锁的完整生命周期

锁是远端状态变化。项目已约定锁流程且本次任务授权编辑指定资产时，编辑前取得自己的锁；否则先确认负责人和采用的流程。下例中的远端必须与检查过的实际 LFS 目标一致：

```sh
git lfs lock --remote=origin "art_source/robot.blend"
git lfs lock --remote=origin "game/assets/robot.glb"
git lfs locks --remote=origin
```

逐条检查退出状态，并读回锁的路径、ID 和 owner。新文件是否允许锁定取决于客户端/服务器能力，失败时不要假定锁已建立。两把锁只成功一把时停止编辑并报告已取得的锁；不要静默继续，也不要擅自解锁清理。`lfs.url` 等配置可能改变实际 LFS 服务器，不能仅凭 Git 远端名称推定锁目标。依据见 [lock 手册](https://github.com/git-lfs/git-lfs/blob/main/docs/man/git-lfs-lock.adoc)。

保持锁直到本次交付通过验收，并已由获授权的人完成合并或明确交接；不要因为 Commit、Push 或创建 PR 成功就提前解锁。释放前核对本地修改已交付、PR 状态、锁 owner 和接手人；说明具体路径并取得清理确认后，才可释放自己的锁：

```sh
git lfs unlock --remote=origin "art_source/robot.blend"
git lfs unlock --remote=origin "game/assets/robot.glb"
git lfs locks --remote=origin
```

禁止 `unlock --force` 或释放他人的锁。若网络超时、授权不足、服务器不支持或返回不明结果，先查询当前锁状态再决定下一步；不能把失败当作成功、自动关闭验证或重复抢锁。默认锁以仓库中的路径协调，不能把不同分支当成独立锁空间。多个 Agent 共用一个 GitHub 身份时，锁也无法替代任务级文件负责人。见 [Git LFS 锁 API](https://github.com/git-lfs/git-lfs/blob/main/docs/api/locking.md)。

`lockable` 文件的只读行为及 Push 前锁检查依赖客户端配置、钩子和服务端能力。检查项目约定的 `lfs.<url>.locksverify`，不假设锁是不可绕过的服务端保护；更不能自动设为 `false` 来通过 Push。详见 [Git LFS 配置手册](https://github.com/git-lfs/git-lfs/blob/main/docs/man/git-lfs-config.adoc)。

## 已有冲突的安全处理

先报告当前操作类型、冲突路径、负责人、双方意图和建议保留的行为。可用 `git status`、`git diff --name-only --diff-filter=U`、`git ls-files -u` 只读检查已有冲突；不要为了“测一下”而发起 merge/rebase。任何合并、变基、继续/中止已有合并或变基，以及清理，都需说明具体目标、影响和保留方案并取得确认。

| 现象 | 处理方式 |
| --- | --- |
| `.gd` 同段代码冲突 | 理解双方需求，给出合并后的行为，确认后逐段修复；执行项目已有的相关验证。禁止统一选 `ours` / `theirs`。 |
| `.tscn` / `.tres` 文本冲突 | 由场景负责人检查节点、资源、信号和动画引用；确认方案后修复，并用固定 Godot 版本打开相关场景验证。消除冲突标记不是验收完成。 |
| `.blend` / `.glb` 二进制冲突 | 停止自动解决，报告双方版本和负责人；由负责人决定在源工具整合或保留哪份，经明确确认后处理并重新导出。 |
| `.import` / `.uid` 冲突或缺失 | 与源文件及引用一并核对；保留同一资源的正确身份和导入设置。不要删除后重生成来掩盖冲突。 |
| 同一文件一边删除、一边修改，或双方重命名 | 先确认产品意图与依赖引用；不能默认删除、覆盖或保留任意一方。 |
| 全文件换行/引擎序列化变化 | 检查 `.gitattributes`、编辑器版本及保存操作；不要未经确认批量格式化、回滚或归一化整个仓库。 |
| Push 被拒绝、分支已前进 | 只读获取并解释远端状态；禁止 force Push。任何后续 merge/rebase 需单独确认。 |

Git 的 `ours` / `theirs` 是相对当前操作的术语，rebase 中尤其容易误读；必须用实际分支、提交和内容说明取舍。不得执行自动“接受全部本地/远端”、`reset --hard`、`clean` 或覆盖未提交修改来解除冲突。

## 应写入项目 AGENTS.md 的决定

本 Skill 提供通用流程；项目必须明确：引擎及 Blender 版本、工程与源资产路径、命名/坐标/尺度/面数/纹理预算、直导 Blend 或导出 GLB 管线、LFS 类型与锁规则、文件负责人和共享文件协调方式、验收场景及现有验证命令、PR 必需的美术验收人。把这些决定留在项目内，使未安装本 Skill 的成员和 Agent 也能遵守。
