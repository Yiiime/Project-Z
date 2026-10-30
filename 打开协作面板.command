#!/usr/bin/env bash
cd "$(dirname "$0")"
export PATH="/opt/homebrew/bin:/usr/local/bin:/usr/local/share/dotnet:$PATH"
if ! command -v node >/dev/null 2>&1; then
  printf '需要先安装 Node.js 22 LTS：https://nodejs.org/\n'
  open 'https://nodejs.org/' >/dev/null 2>&1 || true
  read -r -p '安装后按回车关闭窗口。'
  exit 2
fi
node tools/team/panel/server.mjs
z_exit=$?
if [[ "$z_exit" != 0 ]]; then read -r -p '面板已停止，按回车关闭窗口。'; fi
exit "$z_exit"
