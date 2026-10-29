#!/usr/bin/env bash
set -euo pipefail
z_project_dir="$(cd "$(dirname "$0")" && pwd)"
z_app="$z_project_dir/Builds/Project Z.app"
if [[ ! -d "$z_app" ]]; then
  printf '尚未找到测试版。请先运行：bash Tools/unity.sh build\n'
  read -r -p '按回车关闭窗口。'
  exit 2
fi
open "$z_app"
