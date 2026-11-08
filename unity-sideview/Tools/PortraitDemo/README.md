# Z 岛内对话试玩

在解压后的 macOS 试玩目录双击 `直接试玩Z.command`。启动器会打开街上的 Z 对话，使用本次试玩的临时存档和记忆数据库。输入「你笑一下」，观察 Z 的回复、笑眼和口型，再等她回到待机。玩家可以提出请求，具体表情由 Z 的回复决定。关闭游戏时，启动器也会关闭本次聊天服务。

启动器优先使用环境变量 `Z_MEMORY_CONFIG` 指定的模型配置，默认读取 `~/Library/Application Support/Project Z/MemoryLab/model.json`。新机器首次启动会在浏览器打开本机配置页。真实聊天需要该机器自己的模型凭据。

**复现构建。** 用 Git LFS 检出模型。准备 Unity 6000.3.27f1、npm、可独立运行的 macOS Node.js（22.13 或更高）。从游戏仓库根目录运行，目标目录应为空。随包 Node 需匹配试玩机器的架构。

```sh
bash unity-sideview/Tools/native-rtc/build-macos.sh
"$UNITY_EDITOR" -batchmode -quit -buildTarget StandaloneOSX \
  -projectPath "$PWD/unity-sideview" \
  -executeMethod Seabreeze.Editor.MemoryPlaytestBuild.Build \
  -memoryBuild "$PWD/unity-sideview/Builds/ZDialogue/ProjectZSevenMemory.app" \
  -memoryNode "$NODE_STANDALONE" -zPortraitDemo \
  -logFile "$TMPDIR/z-dialogue-build.log"
codesign --force --sign - "$PWD/unity-sideview/Builds/ZDialogue/ProjectZSevenMemory.app"
codesign --verify --strict "$PWD/unity-sideview/Builds/ZDialogue/ProjectZSevenMemory.app"
```

**复核真实聊天。** 在已配置模型的机器上，在试玩目录运行下行命令。它会通过游戏的输入框发送台词，并把说话、表情保持、恢复待机的画面与检测结果写入指定目录。

```sh
./直接试玩Z.command -zRealSmileCheck "$TMPDIR/z-real-smile"
```
