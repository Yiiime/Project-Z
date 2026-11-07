#!/bin/sh
set -eu
cd "$(dirname "$0")"
z_memory_node="${Z_MEMORY_NODE:-node}"
if ! "$z_memory_node" -e 'require("node:sqlite")' >/dev/null 2>&1; then
  z_memory_node="$HOME/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node"
fi
if ! "$z_memory_node" -e 'require("node:sqlite")' >/dev/null 2>&1; then
  printf '%s\n' '需要 Node.js 22.13 或更新版本。可设置 Z_MEMORY_NODE 指向已安装的 Node。' >&2
  exit 1
fi
if [ ! -d node_modules/ajv ]; then npm ci --ignore-scripts --no-audit --no-fund; fi
if [ "${1:-}" = "--background" ]; then exec "$z_memory_node" launcher.mjs; fi
exec "$z_memory_node" server.mjs
