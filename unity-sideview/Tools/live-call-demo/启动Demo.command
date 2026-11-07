#!/bin/bash
set -e
cd "$(dirname "$0")"
if ! command -v node >/dev/null 2>&1; then
  echo '请先安装 Node.js 24 LTS，再运行此文件。'
  exit 1
fi
if [ ! -d node_modules ]; then npm ci; fi
node scripts/start-local.mjs
