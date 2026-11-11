#!/bin/bash
set -euo pipefail
z_story_root="$(cd "$(dirname "$0")" && pwd)"
z_story_node="${Z_MEMORY_NODE:-$HOME/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node}"
if [[ ! -x "$z_story_node" ]]; then z_story_node="$(command -v node)"; fi
exec "$z_story_node" "$z_story_root/Tools/story-playtest/launch.mjs"
