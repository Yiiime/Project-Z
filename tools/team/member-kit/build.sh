#!/usr/bin/env bash
# Builds the zip sent to team members. Output: dist/Z协作安装包-<version>.zip
set -euo pipefail
z_kit="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
z_root="$(cd "$z_kit/../../.." && pwd)"
z_version="${1:-$(date +%Y.%m.%d)}"
z_name="Z协作安装包-${z_version}"
z_out="${Z_KIT_OUT:-$z_root/dist}"
z_stage="$(mktemp -d)"
trap 'rm -rf "$z_stage"' EXIT
mkdir -p "$z_stage/$z_name" "$z_out"
for z_file in README.md AGENT.md install.sh 安装.command; do
  cat "$z_kit/$z_file" > "$z_stage/$z_name/$z_file"
done
chmod +x "$z_stage/$z_name/install.sh" "$z_stage/$z_name/安装.command"
printf '%s\n' "$z_version" > "$z_stage/$z_name/VERSION"
rm -f "$z_out/$z_name.zip"
COPYFILE_DISABLE=1 xattr -cr "$z_stage/$z_name"
(cd "$z_stage" && ditto -c -k --norsrc --noextattr --noqtn --noacl --keepParent "$z_name" "$z_out/$z_name.zip")
cp "$z_out/$z_name.zip" "$z_out/Project-Z-member-kit-${z_version}.zip"
shasum -a 256 "$z_out/$z_name.zip" "$z_out/Project-Z-member-kit-${z_version}.zip"
