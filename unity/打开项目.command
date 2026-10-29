#!/usr/bin/env bash
cd "$(dirname "$0")"
bash Tools/unity.sh open
z_exit=$?
if [[ "$z_exit" != 0 ]]; then read -r -p '按回车关闭窗口。'; fi
exit "$z_exit"
