#!/bin/zsh
set -e
GAME_DIR="$(cd -- "$(dirname -- "$0")" && pwd)"
exec python3 "$GAME_DIR/serve_demo.py"
