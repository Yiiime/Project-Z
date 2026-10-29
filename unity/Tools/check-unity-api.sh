#!/usr/bin/env bash
set -euo pipefail
z_project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
z_app="${Z_UNITY_APP:-$z_project_dir/../.tooling/project-z/unity-6000.3.27f1/Unity.app}"
z_dotnet="${Z_DOTNET:-$z_project_dir/../.tooling/project-z/dotnet/dotnet}"
z_managed="$z_app/Contents/Resources/Scripting/Managed"
z_templates="$z_app/Contents/Resources/PackageManager/ProjectTemplates/libcache/com.unity.template.3d-cross-platform-17.0.14/ScriptAssemblies"
if [[ ! -x "$z_dotnet" || ! -f "$z_templates/Unity.RenderPipelines.Universal.Runtime.dll" ]]; then
  printf '需要已准备的 .NET SDK 和 Unity 6000.3.27f1 完整编辑器；可设置 Z_DOTNET / Z_UNITY_APP。\n'
  exit 2
fi
export DOTNET_CLI_HOME="$z_project_dir/../.tooling/project-z/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
export MSBUILDDISABLENODEREUSE=1
mkdir -p "$z_project_dir/TestResults"
"$z_dotnet" build "$z_project_dir/Tools/RuntimeCompile/RuntimeCompile.csproj" -p:UseSharedCompilation=false \
  "-p:UnityManaged=$z_managed" "-p:UnityTemplateAssemblies=$z_templates" > "$z_project_dir/TestResults/unity-api.txt"
cat "$z_project_dir/TestResults/unity-api.txt"
printf '此项检查使用真实 Unity 程序集编译源码；不执行编辑器导入、渲染或构建。\n'
