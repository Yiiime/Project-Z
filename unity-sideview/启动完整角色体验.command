#!/bin/sh
set -eu
cd "$(dirname "$0")"
node Tools/start-unified-characters.mjs "$@"
