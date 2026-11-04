# Z Live2D R5 独立 Demo

这是已验收的 Z Live2D R5 独立 Unity Demo 交付目录。工程版本固定为 Unity `6000.6.7f1`、Cubism SDK for Unity `5-r.5`、URP `17.6.0`。

入口场景：`Assets/ZDialogueDemo/Scenes/ZDialogueDemo.unity`。

本目录不包含 Live2D SDK 源码或二进制。接收者需要从 [Live2D 官方 Unity SDK 下载页](https://www.live2d.com/en/sdk/download/unity/) 选择 **URP** 版本并下载 `CubismSdkForUnity-5-r.5.unitypackage`，接受适用的 [Live2D Open Software License](https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html) 和 [Live2D Proprietary Software License](https://www.live2d.com/eula/live2d-proprietary-software-license-agreement_en.html)，再按下面步骤安装。不要直接选择页面上的最新版本：本交付固定使用 `5-r.5`，并会核对下面这次实际使用原包的 SHA-256：

```text
c9ac920b3a7359dc9ebe4ec0e9ad3c50367d615bdaeba3fc028141e90d45a0a1
```

这是本轮实际使用原包的记录值，用于防止误装其他版本，不代表官网发布的校验值。

## 还原

1. 准备 Python 3.9 或更新版本。使用 Unity Hub 安装 Unity `6000.6.7f1`，并准备 macOS 平台模块（目标平台可按需要调整）。
2. 在本目录外保存官方 `CubismSdkForUnity-5-r.5.unitypackage`。不要把原包提交到 Git。
3. 在本目录执行安装器。安装器只接受 SHA-256 完全匹配的官方原包，会拒绝覆盖已有 `Assets/Live2D`，并用路径白名单解包。解包阶段不执行包内代码，随后由 Unity 执行官方 SDK 的编辑器导入逻辑：

   ```sh
   python3 tools/install_r5_sdk.py \
     --unity "/Applications/Unity/Unity-6000.6.7f1/Unity.app/Contents/MacOS/Unity" \
     --unitypackage "/绝对路径/CubismSdkForUnity-5-r.5.unitypackage"
   ```

   安装器先把官方包解到 `Assets/Live2D`，再运行 `tools/apply_sdk_compatibility.py`，最后让 Unity 正常索引工程。三处补丁及其原始/目标哈希在 `sdk-compatibility/manifest.json` 中固定。补丁只适配 Unity 6 编辑器接口，不修改 Cubism 运行时。

4. 安装器在 Unity 首次索引后会执行 `tools/repair_model_guid.py`，把生成的 `Model.asset` 的 GUID 恢复为交付中的原 GUID `0fe9b85c5293e4882a9add0f7b43250b`，同时把 `Model.prefab` 和 `Model.model3.json.meta` 内对该 GUID 的引用同步回去，再自动运行 R5 迁移入口。打开工程，确认场景 `Assets/ZDialogueDemo/Scenes/ZDialogueDemo.unity`，点击 Play。首次导入时 Unity 可能需要等待资源索引完成。

交付刻意不提交 `Assets/ZDialogueDemo/Models/Z/Model.asset`。这是 Cubism 导入器生成的约 60 MB 缓存，超过本仓库约定的 50 MiB 单文件阈值；同目录的 `Model.asset.meta` 已保留，GUID 不变。导入 `Model.model3.json` 后，Cubism 5-r.5 会在本地重新生成该资产。若 Unity 没有自动刷新，可在 Project 窗口右键 `Model.model3.json`，选择 Reimport，再执行下面的迁移入口。

如果需要手动重跑迁移入口（例如 Unity 索引中断后），在工程根目录执行：

```sh
"/Applications/Unity/Unity-6000.6.7f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -projectPath "$PWD" \
  -executeMethod ZDialogueDemo.R5Migration.ZR5Migration.Migrate \
  -quit -logFile "$PWD/migration.log"
```

迁移入口会设置 URP、重新导入模型、重接场景引用并写入 `Assets/ZR5Migration/r5-migration-report.json`。它不访问旧 R4 工程，也不需要本机绝对路径。

## 构建与验收

独立 macOS 构建可用 Unity Editor 方法完成：

```sh
Z_DEMO_BUILD_PATH="$PWD/builds/ZDialogueDemoR5.app" \
  "/Applications/Unity/Unity-6000.6.7f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -projectPath "$PWD" \
  -executeMethod ZDialogueDemo.EditorTools.ZDialogueDemoStandaloneBuilder.BuildMacDemoPreserveScene \
  -quit -logFile "$PWD/build.log"
```

构建后运行 20 项独立表情预览：

```sh
mkdir -p /绝对路径/r5-emotion-qa
"$PWD/builds/ZDialogueDemoR5.app/Contents/MacOS/Z Dialogue Demo R5" \
  -screen-fullscreen 0 -screen-width 1280 -screen-height 900 \
  --z-emotion-verify=/绝对路径/r5-emotion-qa --z-emotion-quit
```

已验收基线是 20/20 动作通过、1,479 次参数采样、截图和表情切换均通过；验收范围仅是当前 Mac 的独立 Demo。正式横版游戏工程（当前仓库的 `unity-sideview/`，Unity `6000.3.27f1` / URP `17.3`）没有在本轮接入验收，不能把本目录直接覆盖到主工程。聊天服务沿用仓库的服务端协议，本目录不携带数据库、`.env` 或真实聊天记录。

## 交付边界

纳入：`Assets/ZDialogueDemo`（场景、模型原始文件、除 60 MB `Model.asset` 外的必要导入产物、运行时/Editor 代码和 R5 Shader）、`Assets/ZR5Migration`、根 Animator 资产、URP/ProjectSettings、Packages 锁定文件、兼容补丁及安装脚本。`Model.asset` 由 Unity 导入器在本地重建，`Model.asset.meta` 保留原 GUID。

排除：`Assets/Live2D` SDK、Unity `Library`/`Temp`/`Logs`/`Obj`/`UserSettings`、构建 App、SDK 原包、旧 R4 工程、服务端凭据与数据库、完整预览截图和其他人物/PSD。SDK 许可说明保留在 `third-party/Cubism/`；本 Demo 没有随包分发字体，界面使用 Unity 内置字体和运行时系统字体（PingFang/Noto 等），接收者需按目标系统的字体授权使用。

对话服务接入见 [`server/README.md`](server/README.md)；脱敏支持层和验收画面清单见 [`verification/SUPPORT-MANIFEST.md`](verification/SUPPORT-MANIFEST.md)、[`verification/r5-summary.json`](verification/r5-summary.json)。

## 本次交付验证（2026-11-05）

从全新目录运行随附安装器，未使用旧工程或旧 Library。安装、GUID 恢复、迁移、macOS 构建均成功。最终窗口模式运行通过 20/20 项预览、1,859 次参数采样，报告为 `passed=true`、`partial=false`；服务测试 24/24 通过，没有调用真实 LLM。详见 [本次还原报告](verification/restore-validation.json)。上面的 1,479 次采样是历史基线，两次验收分别记录。

提交范围与逐文件 SHA-256 见 [上传清单](UPLOAD-MANIFEST.json)。清单不包含自身的哈希。正式横版工程接入、其他平台和真实 LLM 仍需单独验收。
