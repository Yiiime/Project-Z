#!/bin/sh
set -eu
cd "$(dirname "$0")"
export Z_MEMORY_PORT=4436
export Z_MEMORY_SAVE_ROOT="$PWD/Tools/MemoryLab/.local/tts-playtest-saves"
app="$PWD/Builds/TtsPlaytest/ProjectZTts.app"
if [ ! -d "$app" ]; then
  printf '%s\n' '尚未构建本分支的语音试玩包：Builds/TtsPlaytest/ProjectZTts.app'
  exit 1
fi
sh Tools/MemoryLab/start.sh --background
open -n "$app" --args -memoryService "http://127.0.0.1:$Z_MEMORY_PORT"
