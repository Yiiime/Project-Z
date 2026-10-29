#!/usr/bin/env bash
set -euo pipefail
z_project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
z_version="$(sed -n 's/^m_EditorVersion: //p' "$z_project_dir/ProjectSettings/ProjectVersion.txt")"
z_editor="${Z_UNITY_EDITOR:-}"
if [[ -z "$z_editor" ]]; then
  for z_candidate in "/Applications/Unity/Hub/Editor/$z_version/Unity.app/Contents/MacOS/Unity" "/Applications/Unity/Unity.app/Contents/MacOS/Unity" "$z_project_dir/../.tooling/project-z/unity-$z_version/Unity.app/Contents/MacOS/Unity"; do
    if [[ -x "$z_candidate" ]]; then z_editor="$z_candidate"; break; fi
  done
fi
if [[ -z "$z_editor" || ! -x "$z_editor" ]]; then
  printf '未找到 Unity %s。请用 Unity Hub 安装 Apple Silicon 版本并完成授权。\n' "$z_version"
  printf '工程目录：%s\n也可用 Z_UNITY_EDITOR 指定编辑器可执行文件。\n' "$z_project_dir"
  exit 2
fi
z_action="${1:-open}"
case "$z_action" in
  open) exec "$z_editor" -projectPath "$z_project_dir" ;;
  playmode)
    mkdir -p "$z_project_dir/TestResults"
    "$z_editor" -batchmode -projectPath "$z_project_dir" -runTests -testPlatform PlayMode \
      -testResults "$z_project_dir/TestResults/playmode.xml" -logFile "$z_project_dir/TestResults/unity-playmode.log" \
      -z-save-root "$z_project_dir/TestResults/playmode-saves"
    python3 "$z_project_dir/Tools/verify-test-results.py" "$z_project_dir/TestResults/playmode.xml"
    exit 0
    ;;
  prepare) z_method=ProjectZ.Editor.ProjectSetup.Prepare ;;
  art) z_method=ProjectZ.Editor.CoastalArtSetup.Rebuild ;;
  inspect-art) z_method=ProjectZ.Editor.CoastalArtSetup.Inspect ;;
  verify) z_method=ProjectZ.Editor.ProjectSetup.Verify ;;
  build) z_method=ProjectZ.Editor.ProjectSetup.BuildMac ;;
  *) printf '用法：bash Tools/unity.sh [open|prepare|verify|build|playmode|art|inspect-art]\n'; exit 2 ;;
esac
mkdir -p "$z_project_dir/TestResults"
exec "$z_editor" -batchmode -nographics -quit -projectPath "$z_project_dir" \
  -executeMethod "$z_method" -logFile "$z_project_dir/TestResults/unity-$z_action.log"
