#!/usr/bin/env bash
cd "$(dirname "$0")"
bash ./install.sh "$@"
z_exit=$?
if [[ "$z_exit" != 0 ]]; then
  printf '\n安装没有完成。按上面的提示处理后，可以再次双击“安装.command”。\n'
  read -r -p '按回车关闭窗口。' _
fi
exit "$z_exit"
