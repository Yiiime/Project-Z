#!/usr/bin/env bash
set -euo pipefail
z_ending_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$z_ending_root"
z_ending_node="$(command -v node || true)"
[[ -n "$z_ending_node" ]] || z_ending_node="$HOME/.project-z/bin/node"
z_ending_requested_port="${Z_MEMORY_PORT:-}"
# Reuse a compatible service. If an older preview still owns the default port,
# choose a free loopback port instead of terminating its process.
z_ending_port="$(Z_ENDING_REQUESTED_PORT="$z_ending_requested_port" "$z_ending_node" --input-type=module - <<'JS'
import net from 'node:net';
const requested=process.env.Z_ENDING_REQUESTED_PORT;
const start=Number(requested||4338);
if(!Number.isInteger(start)||start<1024||start>65535)throw Error('记忆服务端口无效');
for(let port=start;port<=Math.min(start+(requested?0:20),65535);port++){
  try{const r=await fetch('http://127.0.0.1:'+port+'/api/health',{signal:AbortSignal.timeout(500)});const s=await r.json();if(s.endingIntegration&&s.endingMemoryUpload&&s.endingRecapVersion===3&&s.endingSourceRefresh>=4&&s.endingPresentationVersion>=1&&s.endingStoryVersion>=2){console.log(port);process.exit(0);}}catch{}
  const free=await new Promise(resolve=>{const s=net.createServer();s.once('error',()=>resolve(false));s.listen(port,'127.0.0.1',()=>s.close(()=>resolve(true)));});
  if(free){console.log(port);process.exit(0);}
}
throw Error('指定端口由旧服务或其他程序占用。请不指定 Z_MEMORY_PORT 重新启动，或选择空闲端口。');
JS
)"
export Z_MEMORY_PORT="$z_ending_port"
if [[ -z "${Z_MEMORY_CONFIG:-}" && -f "$HOME/Project-Z/unity-sideview/Tools/MemoryLab/.local/model.json" ]]; then
  export Z_MEMORY_CONFIG="$HOME/Project-Z/unity-sideview/Tools/MemoryLab/.local/model.json"
fi
if [[ ! -d Tools/MemoryLab/node_modules/ajv ]]; then (cd Tools/MemoryLab && npm ci --ignore-scripts --no-audit --no-fund); fi
if ! curl --silent --fail "http://127.0.0.1:${z_ending_port}/api/health" | "$z_ending_node" -e 'let b="";process.stdin.on("data",d=>b+=d);process.stdin.on("end",()=>{try{const s=JSON.parse(b);process.exit(s.endingIntegration&&s.endingMemoryUpload&&s.endingRecapVersion===3&&s.endingSourceRefresh>=4&&s.endingPresentationVersion>=1&&s.endingStoryVersion>=2?0:1)}catch{process.exit(1)}})'; then
  mkdir -p Tools/MemoryLab/.data
  "$z_ending_node" --input-type=module - <<'JS'
import {spawn} from 'node:child_process';
import {openSync,closeSync} from 'node:fs';
import {resolve} from 'node:path';
const log=openSync('Tools/MemoryLab/.data/ending-game-service.log','a',0o600);
const child=spawn(process.execPath,[resolve('Tools/MemoryLab/server.mjs'),'--port',process.env.Z_MEMORY_PORT,'--db',resolve('Tools/MemoryLab/.data/ending-runtime-v4-recap3-story2.sqlite')],{cwd:process.cwd(),env:process.env,detached:true,stdio:['ignore',log,log]});
child.unref();closeSync(log);
JS
  for z_ending_attempt in {1..40}; do
    if curl --silent --fail "http://127.0.0.1:${z_ending_port}/api/health" >/dev/null; then break; fi
    sleep .2
  done
fi
if ! curl --silent --fail "http://127.0.0.1:${z_ending_port}/api/health" | "$z_ending_node" -e 'let b="";process.stdin.on("data",d=>b+=d);process.stdin.on("end",()=>{try{const s=JSON.parse(b);process.exit(s.endingIntegration&&s.endingMemoryUpload&&s.endingRecapVersion===3&&s.endingSourceRefresh>=4&&s.endingPresentationVersion>=1&&s.endingStoryVersion>=2?0:1)}catch{process.exit(1)}})'; then
  printf '游戏记忆服务尚未启动，请查看 Tools/MemoryLab/.data/ending-game-service.log 后重试。\n'
  exit 1
fi
if [[ -d Builds/IslandEnding/ProjectZ-IslandEnding.app ]]; then
  # macOS asks a game inside Documents for access when Mono loads its native libraries.
  # Run the signed build from a private per-user temporary directory instead.
  z_ending_app="$("$z_ending_node" --input-type=module - <<'JS'
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, renameSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
const name = 'ProjectZ-IslandEnding.app';
const source = resolve('Builds/IslandEnding', name);
try { execFileSync('/usr/bin/codesign', ['--verify', '--deep', '--strict', source], { stdio: 'pipe' }); }
catch {
  console.error('本地游戏构建的签名校验失败，请修复或重新构建试玩包后再启动。');
  process.exit(1);
}
const digest = createHash('sha256').update(readFileSync(join(source, 'Contents/_CodeSignature/CodeResources'))).digest('hex');
const cache = join(tmpdir(), 'ProjectZ-PR58', digest);
const app = join(cache, name);
mkdirSync(cache, { recursive: true, mode: 0o700 });
if (!existsSync(app)) {
  const stage = mkdtempSync(join(cache, '.install-'));
  const copy = join(stage, name);
  try {
    execFileSync('/usr/bin/ditto', [source, copy]);
    execFileSync('/usr/bin/codesign', ['--verify', '--deep', '--strict', copy]);
    try { renameSync(copy, app); }
    catch (error) { if (!['EEXIST', 'ENOTEMPTY'].includes(error.code) || !existsSync(app)) throw error; }
  } finally { rmSync(stage, { recursive: true, force: true }); }
}
execFileSync('/usr/bin/codesign', ['--verify', '--deep', '--strict', app]);
console.log(app);
JS
)"
  "$z_ending_node" --input-type=module - <<'JS'
import {mkdirSync,writeFileSync,renameSync} from 'node:fs';
import {homedir} from 'node:os';
import {join} from 'node:path';
const directory=join(homedir(),'.project-z','ending-demo');mkdirSync(directory,{recursive:true});
const path=join(directory,'game-service.json');
writeFileSync(path+'.tmp',JSON.stringify({serviceUrl:'http://127.0.0.1:'+process.env.Z_MEMORY_PORT}),{mode:0o600});renameSync(path+'.tmp',path);
JS
  open -n "$z_ending_app" --args -memoryService "http://127.0.0.1:${z_ending_port}"
  printf '完整游戏已打开。进入“第七晚结局”即可看三幕回忆；最后一幕结束后切黑播放“我想走”。只有更换真正的七日经历时才使用“上传 / 更换记忆文件”。\n'
else
  printf '记忆服务已启动。请在 Unity 打开 Street 场景并 Play；点击“第七晚结局”。\n'
  printf '独立构建菜单：Seabreeze → Ending Demo → Build full game with ending entry。\n'
fi
