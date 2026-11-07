#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
node_bin="${Z_LIVE_NODE:-$(command -v node || true)}"
if [[ -z "$node_bin" && -x "$HOME/.local/bin/node" ]]; then node_bin="$HOME/.local/bin/node"; fi
[[ -n "$node_bin" ]] || { echo '请先安装 Node 24 LTS。'; exit 1; }
exec "$node_bin" Tools/live-service/scripts/start-native-call.mjs
