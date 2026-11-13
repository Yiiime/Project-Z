#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
node_bin="${Z_MEMORY_NODE:-$HOME/.local/bin/node}"
if [ ! -x "$node_bin" ]; then node_bin="$(command -v node)"; fi
"$node_bin" Tools/LanPlay/setup-host.mjs
echo "请保持这个窗口开启。按 Control+C 可停止主机服务。"
caffeinate -i "$node_bin" "$PWD/Tools/LanPlay/host.mjs"
