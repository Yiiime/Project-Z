#!/bin/zsh
set -e
GAME_DIR="$(cd -- "$(dirname -- "$0")" && pwd)"
if command -v godot >/dev/null 2>&1; then
  exec godot --path "$GAME_DIR"
elif [[ -x /Applications/Godot.app/Contents/MacOS/Godot ]]; then
  exec /Applications/Godot.app/Contents/MacOS/Godot --path "$GAME_DIR"
else
  print '未找到 Godot。请使用启动试玩.command，或用 Godot 4.7.2 打开此目录的 project.godot。'
  read
fi
