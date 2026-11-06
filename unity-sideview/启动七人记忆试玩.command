#!/bin/sh
set -eu
cd "$(dirname "$0")"
sh Tools/MemoryLab/start.sh --background
if [ -d Builds/SevenMemory/ProjectZSevenMemory.app ]; then
  open Builds/SevenMemory/ProjectZSevenMemory.app --args -memoryService "http://127.0.0.1:${Z_MEMORY_PORT:-4330}"
else
  printf '%s\n' '记忆服务已启动。请在 Unity 打开 Street 场景并点击 Play，或先构建七人试玩版本。'
fi
