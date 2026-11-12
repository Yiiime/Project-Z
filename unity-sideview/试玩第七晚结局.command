#!/usr/bin/env bash
set -euo pipefail
z_ending_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$z_ending_root"
z_ending_node="$(command -v node || true)"
[[ -n "$z_ending_node" ]] || z_ending_node="$HOME/.project-z/bin/node"
z_ending_port="${Z_MEMORY_PORT:-4338}"
export Z_MEMORY_PORT="$z_ending_port"
if [[ -z "${Z_MEMORY_CONFIG:-}" && -f "$HOME/Project-Z/unity-sideview/Tools/MemoryLab/.local/model.json" ]]; then
  export Z_MEMORY_CONFIG="$HOME/Project-Z/unity-sideview/Tools/MemoryLab/.local/model.json"
fi
if [[ ! -d Tools/MemoryLab/node_modules/ajv ]]; then (cd Tools/MemoryLab && npm ci --ignore-scripts --no-audit --no-fund); fi
if ! curl --silent --fail "http://127.0.0.1:${z_ending_port}/api/health" | "$z_ending_node" -e 'let b="";process.stdin.on("data",d=>b+=d);process.stdin.on("end",()=>{try{process.exit(JSON.parse(b).endingIntegration?0:1)}catch{process.exit(1)}})'; then
  mkdir -p Tools/MemoryLab/.data
  "$z_ending_node" --input-type=module - <<'JS'
import {spawn} from 'node:child_process';
import {openSync,closeSync} from 'node:fs';
import {resolve} from 'node:path';
const log=openSync('Tools/MemoryLab/.data/ending-game-service.log','a',0o600);
const child=spawn(process.execPath,[resolve('Tools/MemoryLab/server.mjs'),'--port',process.env.Z_MEMORY_PORT],{cwd:process.cwd(),env:process.env,detached:true,stdio:['ignore',log,log]});
child.unref();closeSync(log);
JS
  for z_ending_attempt in {1..40}; do
    if curl --silent --fail "http://127.0.0.1:${z_ending_port}/api/health" >/dev/null; then break; fi
    sleep .2
  done
fi
if ! curl --silent --fail "http://127.0.0.1:${z_ending_port}/api/health" | "$z_ending_node" -e 'let b="";process.stdin.on("data",d=>b+=d);process.stdin.on("end",()=>{try{process.exit(JSON.parse(b).endingIntegration?0:1)}catch{process.exit(1)}})'; then
  printf '游戏记忆服务尚未启动，请查看 Tools/MemoryLab/.data/ending-game-service.log 后重试。\n'
  exit 1
fi
if [[ -d Builds/IslandEnding/ProjectZ-IslandEnding.app ]]; then
  open Builds/IslandEnding/ProjectZ-IslandEnding.app --args -memoryService "http://127.0.0.1:${z_ending_port}"
  printf '完整游戏已打开。点击左下角“第七晚结局”，或在手机和暂停菜单中进入。\n'
else
  printf '记忆服务已启动。请在 Unity 打开 Street 场景并 Play；点击“第七晚结局”。\n'
  printf '独立构建菜单：Seabreeze → Ending Demo → Build full game with ending entry。\n'
fi
