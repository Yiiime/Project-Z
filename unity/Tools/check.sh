#!/usr/bin/env bash
set -euo pipefail
z_project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$z_project_dir"
z_dotnet="${Z_DOTNET:-$z_project_dir/../.tooling/project-z/dotnet/dotnet}"
if [[ ! -x "$z_dotnet" ]]; then z_dotnet="$(command -v dotnet || true)"; fi
if [[ -z "$z_dotnet" ]]; then printf '需要 .NET 8 SDK；可用 Z_DOTNET 指定 dotnet 路径。\n'; exit 2; fi
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$z_project_dir/TestResults/dotnet-home}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
export MSBUILDDISABLENODEREUSE=1
mkdir -p TestResults
"$z_dotnet" run --project Tests/CoreSpecs/CoreSpecs.csproj -p:UseSharedCompilation=false 2>&1 | tee TestResults/core.txt
node --test Tests/bridge.test.mjs 2>&1 | tee TestResults/bridge.txt
"$z_dotnet" run --project Tools/SyntaxCheck/SyntaxCheck.csproj -p:UseSharedCompilation=false -- Assets 2>&1 | tee TestResults/syntax.txt
node Tools/check-source.mjs
