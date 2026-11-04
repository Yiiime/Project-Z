#!/bin/zsh -l
set -e
cd "${0:A:h}"
if ! command -v node >/dev/null; then
  print '需要 Node.js 24 或更新版本。'
  exit 1
fi
node -e 'if(Number(process.versions.node.split(".")[0])<24)process.exit(1)' || { print '请使用 Node.js 24 或更新版本。'; exit 1; }
if [[ ! -d node_modules/ws ]]; then npm ci --ignore-scripts; fi
exec node server/index.mjs
