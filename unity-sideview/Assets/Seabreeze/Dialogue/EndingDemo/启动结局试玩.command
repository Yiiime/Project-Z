#!/usr/bin/env bash
set -euo pipefail
z_demo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
z_demo_node="$(command -v node || true)"
if [[ -z "$z_demo_node" && -x "$HOME/.project-z/bin/node" ]]; then z_demo_node="$HOME/.project-z/bin/node"; fi
if [[ -z "$z_demo_node" ]]; then printf '请先安装 Node.js 22。\n'; exit 1; fi
z_demo_port="${Z_ENDING_PORT:-4319}"
if curl --silent --fail "http://127.0.0.1:${z_demo_port}/api/status" | "$z_demo_node" -e 'let s="";process.stdin.on("data",d=>s+=d);process.stdin.on("end",()=>{try{process.exit(JSON.parse(s).app==="project-z-ending-demo"?0:1)}catch{process.exit(1)}})'; then
  open "http://127.0.0.1:${z_demo_port}"
  printf '结局试玩已启动；请在上传故事文件中使用本地文件。\n'
  exit 0
fi
if [[ -z "${Z_MEMORY_CONFIG:-}" && -f "$HOME/Project-Z/unity-sideview/Tools/MemoryLab/.local/model.json" ]]; then
  export Z_MEMORY_CONFIG="$HOME/Project-Z/unity-sideview/Tools/MemoryLab/.local/model.json"
fi
"$z_demo_node" "$z_demo_dir/server.mjs" &
z_demo_pid=$!
trap 'kill "$z_demo_pid" 2>/dev/null || true' EXIT INT TERM
for z_demo_attempt in {1..30}; do
  if curl --silent --fail "http://127.0.0.1:${z_demo_port}/api/status" >/dev/null; then open "http://127.0.0.1:${z_demo_port}"; wait "$z_demo_pid"; exit 0; fi
  sleep .2
done
printf '服务未启动，请查看上方错误。端口 %s 可能已被其他程序占用。\n' "$z_demo_port"
exit 1
