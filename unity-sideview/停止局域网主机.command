#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
node_bin="${Z_MEMORY_NODE:-$HOME/.local/bin/node}"
if [ ! -x "$node_bin" ]; then node_bin="$(command -v node)"; fi
"$node_bin" Tools/LanPlay/stop-host.mjs
