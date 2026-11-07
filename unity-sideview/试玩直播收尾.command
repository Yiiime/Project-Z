#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
project_root="$PWD"
service_root="$project_root/Tools/live-service"
app_path="$project_root/Builds/macOS/Seabreeze.app"
export Z_UNITY_PORT="${Z_UNITY_PORT:-4187}"
export Z_MEMORY_SAVE_ROOT="$project_root/Builds/LiveSession/saves"
mkdir -p "$Z_MEMORY_SAVE_ROOT" "$project_root/Builds/LiveSession"
node_bin="${Z_LIVE_NODE:-}"
if [ -z "$node_bin" ]; then
  node_bin="$(command -v node || true)"
fi
if [ -z "$node_bin" ] && [ -x "$HOME/.local/bin/node" ]; then node_bin="$HOME/.local/bin/node"; fi
if [ -z "$node_bin" ] || [ ! -d "$app_path" ]; then
  echo "需要 Node 24+ 和 Builds/macOS/Seabreeze.app。请先完成 Unity macOS 构建。"
  exit 1
fi
export Z_LIVE_PREVIEW_ROOT="$project_root"
if ! "$node_bin" --input-type=module - <<'JS'
import fs from 'node:fs';
const root=process.env.Z_LIVE_PREVIEW_ROOT;
try {
 const token=fs.readFileSync(root+'/Tools/live-service/.runtime/local-token','utf8').trim();
 const r=await fetch(`http://127.0.0.1:${process.env.Z_UNITY_PORT}/health`,{headers:{Authorization:'Bearer '+token},signal:AbortSignal.timeout(1500)});
 if(!r.ok)process.exit(1);
}catch{process.exit(1);}
JS
then
  "$node_bin" --input-type=module - <<'JS'
import fs from 'node:fs';
import {spawn} from 'node:child_process';
const root=process.env.Z_LIVE_PREVIEW_ROOT;
const log=fs.openSync(root+'/Builds/LiveSession/service.log','a',0o600);
const child=spawn(process.execPath,['server/index.mjs'],{cwd:root+'/Tools/live-service',env:process.env,detached:true,stdio:['ignore',log,log]});
child.unref();fs.closeSync(log);
JS
fi
"$node_bin" --input-type=module - <<'JS'
import fs from 'node:fs';
import {setTimeout} from 'node:timers/promises';
const root=process.env.Z_LIVE_PREVIEW_ROOT;
let ready=false;
for(let i=0;i<20;i++){
 try{
  const token=fs.readFileSync(root+'/Tools/live-service/.runtime/local-token','utf8').trim();
  const baseUrl=`http://127.0.0.1:${process.env.Z_UNITY_PORT}`;
  const r=await fetch(baseUrl+'/health',{headers:{Authorization:'Bearer '+token},signal:AbortSignal.timeout(1000)});
  if(r.ok){fs.writeFileSync(root+'/Builds/macOS/Seabreeze.app/Contents/Resources/zlive-connection.json',JSON.stringify({baseUrl,token}),{mode:0o600});ready=true;break;}
 }catch{}
 await setTimeout(500);
}
if(!ready){console.error('直播服务未启动，请查看 Builds/LiveSession/service.log。');process.exit(1);}
JS
/usr/bin/codesign --force --deep --sign - "$app_path"
echo "按 Tab 打开直播；输入框旁点击“结束直播”。本试玩使用独立存档。"
app_executable="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$app_path/Contents/Info.plist")"
[ -x "$app_path/Contents/MacOS/$app_executable" ] || { echo "试玩程序不可执行。"; exit 1; }
if [ "${Z_LIVE_PREVIEW_CHECK_ONLY:-0}" = "1" ]; then echo "试玩启动检查通过。"; exit 0; fi
"$app_path/Contents/MacOS/$app_executable" -memoryDisabled -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "$project_root/Builds/LiveSession/player.log"
