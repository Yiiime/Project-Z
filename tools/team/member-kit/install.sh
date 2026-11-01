#!/usr/bin/env bash
# Z 协作环境安装器：只写入当前用户目录，不需要管理员密码，也不保存或打印 GitHub token。
set -euo pipefail

z_repo="${Z_REPO:-Yiiime/Project-Z}"
z_repo_url="https://github.com/${z_repo}.git"
z_project="${Z_PROJECT_DIR:-$HOME/Project-Z}"
z_home="${Z_TOOL_HOME:-$HOME/.project-z}"
z_bin="$z_home/bin"
z_log_dir="$z_home/logs"
z_check_only=false
z_skip_login=false
z_skip_dotnet=false
z_no_open=false

z_node_version="22.23.4"
z_node_sha_arm64="c9e1dbc748b33ed2d14f42445191ecaf14c56259c7a9a26d0042c49ec2b66428"
z_node_sha_x64="efcc2b0db6f94bf53eddb038fe2b370277a042bd29e4f19b8f8e23598795b819"
z_gh_version="2.103.3"
z_gh_sha_arm64="d700207eae9ae98fe2b89535c2c087c07823d4628c56364695a5a07ef311c062"
z_gh_sha_x64="a2f86418f4350ae6e9ec60d5000bdf7665728436946a97fc27fe50e6ae752682"
z_dotnet_version="8.0.427"
z_dotnet_sha_arm64="79786b93f249d233b15f1d6338d4798bd71c8143f267c5e84de76c9eb8a1054920e56ea5478f7bddfb74b951cc58aaca11fbd780cf134350b7cb7cdcb42a8c8c"
z_dotnet_sha_x64="a6c6d0a3708b15e20285f4f662195b6fd276e3df2fc91c0a38f30681ff8a23d5e322ada8a92e9b410343449eab635d01b30a4f55aeef6f884651a559cce07707"

z_original_args=("$@")

usage() {
  cat <<'HELP'
用法：bash 安装.command [选项]
  --check        只检查，不安装、不登录、不修改项目
  --project 路径 项目放在哪里，默认 ~/Project-Z
  --skip-login   暂不登录 GitHub（适合先下载工具）
  --skip-dotnet  不安装 .NET 8（交付前的本机快速检查会跳过）
  --no-open      安装完成后不打开协作面板
HELP
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --check) z_check_only=true; shift ;;
    --project) z_project="${2:?--project 需要路径}"; shift 2 ;;
    --skip-login) z_skip_login=true; shift ;;
    --skip-dotnet) z_skip_dotnet=true; shift ;;
    --no-open) z_no_open=true; shift ;;
    -h|--help) usage; exit 0 ;;
    *) printf '未知选项：%s\n' "$1" >&2; usage; exit 2 ;;
  esac
done

mkdir -p "$z_log_dir"
z_log="$z_log_dir/install-$(date +%Y%m%d-%H%M%S).log"
if [[ -z "${Z_INSTALL_LOGGING:-}" ]]; then
  export Z_INSTALL_LOGGING=1
  set +e
  Z_INSTALL_TTY="$([[ -t 1 ]] && echo 1 || true)" bash "${BASH_SOURCE[0]}" "${z_original_args[@]+"${z_original_args[@]}"}" 2>&1 | tee -a "$z_log"
  z_status=${PIPESTATUS[0]}
  exit "$z_status"
fi

ok() { printf '✓ %s\n' "$*"; }
info() { printf '→ %s\n' "$*"; }
warn() { printf '△ %s\n' "$*"; }
fail() {
  printf '\n✗ %s\n' "$*" >&2
  printf '安装记录：%s\n' "$z_log" >&2
  exit 1
}

case "$(uname -m)" in
  arm64) z_arch=arm64 ;;
  x86_64) z_arch=x64 ;;
  *) fail "暂不支持这台电脑的芯片：$(uname -m)" ;;
esac
[[ "$(uname -s)" == Darwin ]] || fail '这个安装包目前只支持 macOS。'
z_macos="$(sw_vers -productVersion)"
[[ "${z_macos%%.*}" -ge 13 ]] || fail "需要 macOS 13 或更新版本，当前是 ${z_macos}。"

printf '\nZ 协作环境安装器\n'
printf '电脑：macOS %s · %s\n项目：%s\n工具：%s\n\n' "$z_macos" "$z_arch" "$z_project" "$z_home"

sha_ok() {
  local file="$1" expected="$2" algorithm="$3" actual
  actual="$(shasum -a "$algorithm" "$file" | awk '{print $1}')"
  [[ "$actual" == "$expected" ]]
}

download_verified() {
  local url="$1" file="$2" expected="$3" algorithm="$4"
  if [[ -f "$file" ]] && sha_ok "$file" "$expected" "$algorithm"; then return; fi
  rm -f "$file"
  info "下载 $(basename "$file")"
  local progress=(--progress-bar)
  [[ "${Z_INSTALL_TTY:-}" == 1 ]] || progress=(--silent --show-error)
  curl -fL --retry 3 --connect-timeout 20 "${progress[@]}" "$url" -o "$file.part" || fail "下载失败：$url"
  mv "$file.part" "$file"
  sha_ok "$file" "$expected" "$algorithm" || { rm -f "$file"; fail "下载文件校验失败：$(basename "$file")"; }
}

node_ok() {
  [[ -x "$z_bin/node" ]] && "$z_bin/node" -e 'const [a,b]=process.versions.node.split(".").map(Number); process.exit(a>20||(a===20&&b>=19)?0:1)'
}

system_node_ok() {
  local candidate
  candidate="$(command -v node 2>/dev/null || true)"
  [[ -n "$candidate" && "$candidate" != "$z_bin/node" ]] && "$candidate" -e 'const [a,b]=process.versions.node.split(".").map(Number); process.exit(a>20||(a===20&&b>=19)?0:1)' 2>/dev/null && printf '%s' "$candidate"
}

install_node() {
  if node_ok; then ok "Node.js $("$z_bin/node" -v)"; return; fi
  local existing
  existing="$(system_node_ok || true)"
  if [[ -n "$existing" ]]; then
    $z_check_only || { mkdir -p "$z_bin"; ln -sfn "$existing" "$z_bin/node"; }
    ok "Node.js $("$existing" -v)（使用电脑上已有版本）"
    return
  fi
  $z_check_only && { warn 'Node.js 22 还没有安装'; return; }
  local name="node-v${z_node_version}-darwin-${z_arch}" sha
  [[ "$z_arch" == arm64 ]] && sha="$z_node_sha_arm64" || sha="$z_node_sha_x64"
  mkdir -p "$z_home/downloads" "$z_home/node" "$z_bin"
  download_verified "https://nodejs.org/dist/v${z_node_version}/${name}.tar.gz" "$z_home/downloads/${name}.tar.gz" "$sha" 256
  rm -rf "$z_home/node/$name"
  tar -xzf "$z_home/downloads/${name}.tar.gz" -C "$z_home/node"
  ln -sfn "$z_home/node/$name/bin/node" "$z_bin/node"
  ok "Node.js $("$z_bin/node" -v)"
}

install_gh() {
  if [[ -x "$z_bin/gh" ]]; then ok "GitHub CLI $("$z_bin/gh" --version | head -1 | awk '{print $3}')"; return; fi
  local existing
  existing="$(command -v gh 2>/dev/null || true)"
  if [[ -n "$existing" ]]; then
    if "$existing" --version >/dev/null 2>&1; then
      $z_check_only || { mkdir -p "$z_bin"; ln -sfn "$existing" "$z_bin/gh"; }
      ok "GitHub CLI（使用电脑上已有版本）"
      return
    fi
  fi
  $z_check_only && { warn 'GitHub CLI 还没有安装'; return; }
  local arch name sha
  [[ "$z_arch" == arm64 ]] && arch=arm64 || arch=amd64
  [[ "$z_arch" == arm64 ]] && sha="$z_gh_sha_arm64" || sha="$z_gh_sha_x64"
  name="gh_${z_gh_version}_macOS_${arch}"
  mkdir -p "$z_home/downloads" "$z_home/gh" "$z_bin"
  download_verified "https://github.com/cli/cli/releases/download/v${z_gh_version}/${name}.zip" "$z_home/downloads/${name}.zip" "$sha" 256
  rm -rf "$z_home/gh/$name"
  ditto -x -k "$z_home/downloads/${name}.zip" "$z_home/gh"
  ln -sfn "$z_home/gh/$name/bin/gh" "$z_bin/gh"
  ok "GitHub CLI ${z_gh_version}"
}

dotnet_ok() { [[ -x "$z_home/dotnet/dotnet" ]] && "$z_home/dotnet/dotnet" --list-sdks 2>/dev/null | grep -q '^8\.'; }

install_dotnet() {
  if dotnet_ok; then ok '.NET 8 SDK'; return; fi
  if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^8\.'; then ok '.NET 8 SDK（使用电脑上已有版本）'; return; fi
  if $z_skip_dotnet; then warn '已跳过 .NET 8；GitHub 仍会在合并前检查。'; return; fi
  $z_check_only && { warn '.NET 8 SDK 还没有安装（建议安装）'; return; }
  local file sha
  file="dotnet-sdk-${z_dotnet_version}-osx-${z_arch}.tar.gz"
  [[ "$z_arch" == arm64 ]] && sha="$z_dotnet_sha_arm64" || sha="$z_dotnet_sha_x64"
  mkdir -p "$z_home/downloads"
  download_verified "https://builds.dotnet.microsoft.com/dotnet/Sdk/${z_dotnet_version}/${file}" "$z_home/downloads/$file" "$sha" 512
  rm -rf "$z_home/dotnet"
  mkdir -p "$z_home/dotnet"
  tar -xzf "$z_home/downloads/$file" -C "$z_home/dotnet"
  ln -sfn "$z_home/dotnet/dotnet" "$z_bin/dotnet"
  ok ".NET ${z_dotnet_version} SDK"
}

need_git() {
  if xcode-select -p >/dev/null 2>&1 && /usr/bin/git --version >/dev/null 2>&1; then ok "$(/usr/bin/git --version)"; return; fi
  $z_check_only && { warn 'Git 还没有安装'; return; }
  warn 'macOS 需要先安装开发者命令行工具（Git）。'
  xcode-select --install >/dev/null 2>&1 || true
  fail '请在弹出的窗口中点“安装”。安装完成后，重新双击“安装.command”。'
}

gh_logged_in() { "$z_bin/gh" auth status --hostname github.com >/dev/null 2>&1; }

login_github() {
  if gh_logged_in; then ok "GitHub 已登录：$("$z_bin/gh" api user --jq .login)"; return; fi
  if $z_check_only || $z_skip_login; then warn 'GitHub 还没有登录'; return; fi
  printf '\n下一步登录 GitHub。\n'
  printf '1. 终端会显示一个一次性代码（例如 ABCD-1234），并自动复制。\n'
  printf '2. 浏览器打开后粘贴代码，点 Continue / Authorize。\n'
  printf '3. 回到这个窗口等待完成。\n\n'
  if [[ -t 0 ]]; then read -r -p '准备好后按回车继续…' _; fi
  printf '\n' | "$z_bin/gh" auth login --hostname github.com --git-protocol https --web --clipboard --skip-ssh-key || fail 'GitHub 登录没有完成，可以重新运行安装器。'
  ok "GitHub 已登录：$("$z_bin/gh" api user --jq .login)"
}

configure_git() {
  $z_check_only && return 0
  gh_logged_in || return 0
  "$z_bin/gh" auth setup-git --hostname github.com >/dev/null
  local name email login id
  name="$(/usr/bin/git config --global user.name || true)"
  email="$(/usr/bin/git config --global user.email || true)"
  if [[ -z "$name" || -z "$email" ]]; then
    login="$("$z_bin/gh" api user --jq .login)"
    id="$("$z_bin/gh" api user --jq .id)"
    [[ -n "$name" ]] || /usr/bin/git config --global user.name "$login"
    [[ -n "$email" ]] || /usr/bin/git config --global user.email "${id}+${login}@users.noreply.github.com"
  fi
  ok "Git 署名：$(/usr/bin/git config --global user.name) <$(/usr/bin/git config --global user.email)>"
}

check_access() {
  gh_logged_in || return 0
  local push
  push="$("$z_bin/gh" api "repos/$z_repo" --jq '.permissions.push' 2>/dev/null || true)"
  if [[ "$push" == true ]]; then ok "有 $z_repo 写权限"; return; fi
  if $z_check_only; then warn "还没有 $z_repo 写权限"; return; fi
  printf '\n这个 GitHub 账号还不能写入 %s。\n' "$z_repo"
  printf '请把 GitHub 用户名发给维护者：%s\n' "$("$z_bin/gh" api user --jq .login)"
  printf '收到邀请后，在 GitHub 通知中接受，再重新运行安装器。\n'
  open "https://github.com/notifications" >/dev/null 2>&1 || true
  fail '等待仓库邀请。'
}

clone_project() {
  if [[ -d "$z_project/.git" ]]; then
    local origin
    origin="$(/usr/bin/git -C "$z_project" remote get-url origin 2>/dev/null || true)"
    [[ "$origin" == *"$z_repo"* ]] || fail "$z_project 已存在，但不是 ${z_repo}。可用 --project 换一个位置。"
    ok "项目已在：$z_project"
    if ! $z_check_only && [[ "$(/usr/bin/git -C "$z_project" branch --show-current)" == main && -z "$(/usr/bin/git -C "$z_project" status --porcelain)" ]]; then
      /usr/bin/git -C "$z_project" pull --ff-only --quiet origin main && ok '项目已更新到最新 main'
    fi
    return
  fi
  [[ ! -e "$z_project" ]] || fail "$z_project 已存在但不是 Git 项目。可用 --project 换一个位置。"
  $z_check_only && { warn "项目还没有下载到 $z_project"; return; }
  gh_logged_in || { warn '登录 GitHub 后才能下载项目。'; return; }
  info "下载项目到 ${z_project}（约 270 MB，首次需要几分钟）"
  local clone_progress=()
  [[ "${Z_INSTALL_TTY:-}" == 1 ]] && clone_progress=(--progress)
  /usr/bin/git clone "${clone_progress[@]+"${clone_progress[@]}"}" "$z_repo_url" "$z_project" || fail '项目下载失败。确认网络和仓库权限后重试。'
  ok "项目已下载：$z_project"
}

write_launchers() {
  $z_check_only && return 0
  mkdir -p "$z_bin"
  cat > "$z_bin/z-env" <<ENV
# Generated by Project Z installer.
export PATH="$z_bin:\$PATH"
export Z_GH="$z_bin/gh"
export Z_PROJECT_DIR="$z_project"
[[ -x "$z_home/dotnet/dotnet" ]] && export Z_DOTNET="$z_home/dotnet/dotnet"
ENV
  cat > "$z_bin/z" <<'CLI'
#!/usr/bin/env bash
source "$HOME/.project-z/bin/z-env"
cd "$Z_PROJECT_DIR" || { printf '找不到项目目录：%s\n' "$Z_PROJECT_DIR"; exit 2; }
exec tools/team/z "$@"
CLI
  chmod +x "$z_bin/z"
  local launcher="$HOME/Desktop/打开 Z 协作面板.command"
  cat > "$launcher" <<CMD
#!/usr/bin/env bash
source "$z_bin/z-env"
cd "$z_project" || { printf '找不到项目：%s\n' "$z_project"; read -r -p '按回车关闭。'; exit 2; }
exec "$z_bin/node" tools/team/panel/server.mjs
CMD
  chmod +x "$launcher"
  ok "桌面已生成：打开 Z 协作面板.command"
}

doctor_project() {
  $z_check_only && return 0
  [[ -d "$z_project/.git" ]] && gh_logged_in || return 0
  info '检查协作面板所需环境'
  (source "$z_bin/z-env" && cd "$z_project" && tools/team/z doctor) || fail '项目检查没有通过，请按上面的提示处理。'
}

need_git
install_node
install_gh
install_dotnet
login_github
configure_git
check_access
clone_project
write_launchers
doctor_project

printf '\n'
if $z_check_only; then
  ok '检查完成。'
elif ! gh_logged_in || [[ ! -d "$z_project/.git" ]]; then
  warn '工具已准备好，但还没有完成 GitHub 登录和项目下载。'
  printf '下一步：再次运行安装，不要加 --skip-login。\n'
else
  ok '安装完成。以后每天双击桌面上的“打开 Z 协作面板.command”。'
  printf '还需要：Codex（https://chatgpt.com/download/）；改场景或试玩时安装 Unity 6000.3.27f1。\n'
  if ! $z_no_open && [[ -d "$z_project/.git" ]] && gh_logged_in; then
    info '正在打开协作面板。保持这个窗口打开；关闭窗口会停止面板。'
    source "$z_bin/z-env"
    cd "$z_project"
    exec "$z_bin/node" tools/team/panel/server.mjs
  fi
fi
