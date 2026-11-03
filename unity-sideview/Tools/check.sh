#!/usr/bin/env bash
set -euo pipefail
z_project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$z_project_dir"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
node Tools/check-source.mjs
z_dotnet="${Z_DOTNET:-$(command -v dotnet || true)}"
[[ -n "$z_dotnet" ]] || { printf '需要 .NET 8 SDK。\n'; exit 2; }
"$z_dotnet" run --project ../unity/Tools/SyntaxCheck/SyntaxCheck.csproj -p:UseSharedCompilation=false -- Assets
