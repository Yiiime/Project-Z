import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

const here = path.dirname(fileURLToPath(import.meta.url)), project = path.resolve(here, '../..');
const local = path.resolve(process.env.Z_LAN_HOST_ROOT || path.join(here, '.local'));
const source = path.join(project, 'Builds/Day01-Playtest/ProjectZ-Day01.app');
const destination = path.resolve(process.argv[2] || path.join(project, 'Builds/Day01-LAN'));
if (!fs.existsSync(source)) throw Error('请先构建 Day01 试玩应用。');
if (destination === path.dirname(source)) throw Error('局域网包必须与当前本地试玩包分开。');
if (fs.existsSync(path.join(destination, '.local'))) throw Error('该包已有玩家数据，请选择新的输出目录，避免将存档或身份打包。');
fs.mkdirSync(path.join(destination, 'Tools/LanPlay'), {recursive: true});
fs.mkdirSync(path.join(destination, 'Runtime'), {recursive: true});
// Recreate only this generated app; macOS ditto preserves framework symlinks and
// code-signing metadata (Node's recursive copy can rewrite framework aliases).
fs.rmSync(path.join(destination, 'ProjectZ-Day01.app'), {recursive: true, force: true});
execFileSync('/usr/bin/ditto', [source, path.join(destination, 'ProjectZ-Day01.app')]);
for (const file of ['protocol.mjs', 'relay.mjs', 'start-client.mjs']) fs.copyFileSync(path.join(here, file), path.join(destination, 'Tools/LanPlay', file));
for (const arch of ['arm64', 'x64']) {
  const binary = process.env['Z_LAN_NODE_' + arch.toUpperCase()] || path.join(local, 'runtimes/node-' + arch);
  if (!fs.existsSync(binary)) throw Error('缺少 ' + arch + ' Node 运行时，请设置 Z_LAN_NODE_' + arch.toUpperCase());
  fs.copyFileSync(binary, path.join(destination, 'Runtime/node-' + arch)); fs.chmodSync(path.join(destination, 'Runtime/node-' + arch), 0o755);
}
// Keep no obsolete architecture-specific default from an earlier package pass.
fs.rmSync(path.join(destination, 'Runtime/node'), {force: true});
fs.copyFileSync(path.join(local, 'runtimes/NODE-ARM64-LICENSE.txt'), path.join(destination, 'Runtime/NODE-LICENSE.txt'));
fs.copyFileSync(path.join(local, 'runtimes/NODE-X64-LICENSE.txt'), path.join(destination, 'Runtime/NODE-X64-LICENSE.txt'));
fs.copyFileSync(path.join(local, 'client-config.json'), path.join(destination, 'connection-config.json'));
fs.chmodSync(path.join(destination, 'connection-config.json'), 0o600);
const launcher = path.join(destination, '开始试玩.command');
fs.writeFileSync(launcher, '#!/bin/bash\nset -euo pipefail\ncd "$(dirname "$0")"\ncase "$(uname -m)" in\n  arm64) node_bin=./Runtime/node-arm64 ;;\n  x86_64) node_bin=./Runtime/node-x64 ;;\n  *) echo "此包支持 Apple Silicon 和 Intel Mac。"; exit 1 ;;\nesac\nif ! "$node_bin" Tools/LanPlay/start-client.mjs "$@"; then\n  echo "连接未完成。确认主机在线、同一局域网后重新双击即可。"\n  read -r -p "按回车关闭…"\n  exit 1\nfi\n'); fs.chmodSync(launcher, 0o755);
const config = JSON.parse(fs.readFileSync(path.join(destination, 'connection-config.json')));
fs.writeFileSync(path.join(destination, '试玩说明.txt'), `适用于 macOS 13.5 及以上，Apple Silicon 或 Intel Mac。解压整个文件夹，双击「开始试玩.command」。不需要安装 Node，也不用配置模型或语音 API。\n电脑需与主机在可互访的同一局域网；主机地址：${config.serverUrl}。主机须运行「启动局域网主机.command」、保持开机联网且不合盖。\nA/D 移动，E 交谈，P 拍照，Tab 手机，Esc 返回。每台电脑第一次启动时自动建立独立身份。\n本机进度保存在 .local/lan-client/saves；对应角色记忆保存在主机。不要删除 .local 或把运行后的文件夹转发给别人；分发请使用原始压缩包。\n包内只有游戏邀请和公开证书，没有模型/语音密钥或证书私钥。所有模型和语音费用由主机账户承担。\n这是 Day0–1 开发草稿，视频1与烟花仍是占位。主机IP变化后需更新 connection-config.json。首次打开时如 macOS 提示未知开发者，请用系统提供的“仍要打开”确认；不需关闭系统安全功能。\n`);
fs.writeFileSync(path.join(destination, 'source-revision.txt'), execFileSync('git', ['rev-parse', 'HEAD'], {cwd: project}));
if (fs.existsSync(path.join(here, 'verification.json'))) fs.copyFileSync(path.join(here, 'verification.json'), path.join(destination, '验收记录.json'));
execFileSync('/usr/bin/codesign', ['--verify', '--deep', '--strict', path.join(destination, 'ProjectZ-Day01.app')]);
console.log('局域网玩家包已生成：' + destination);
