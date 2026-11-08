#!/bin/sh
set -eu
cd "$(dirname "$0")"
if [ -x Runtime/node ]; then
  exec Runtime/node portrait-playtest.mjs "$@"
fi
exec "${Z_MEMORY_NODE:-node}" portrait-playtest.mjs "$@"
