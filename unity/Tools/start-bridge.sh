#!/usr/bin/env bash
set -euo pipefail
z_tools_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ -z "${Z_MODEL_ID:-}" ]]; then read -r -p '模型 ID：' Z_MODEL_ID; export Z_MODEL_ID; fi
if [[ -z "${Z_MODEL_API_KEY:-}" ]]; then read -r -s -p 'API Key（不会显示或保存）：' Z_MODEL_API_KEY; printf '\n'; export Z_MODEL_API_KEY; fi
exec node "$z_tools_dir/character-bridge.mjs"
