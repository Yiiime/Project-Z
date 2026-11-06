#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
export PATH="$HOME/.local/bin:/opt/homebrew/bin:/usr/local/bin:$PATH"
export Z_LIVE_OFFICE=1
exec caffeinate -i node server/index.mjs
