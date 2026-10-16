#!/bin/zsh
set -eu
project_dir="$(cd "$(dirname "$0")" && pwd)"
godot_bin="${GODOT_BIN:-/Applications/Godot.app/Contents/MacOS/Godot}"
if [[ ! -x "$godot_bin" ]]; then
  print "找不到 Godot。请安装 Godot 4.4 或更新版本，或设置 GODOT_BIN。"
  exit 1
fi
exec "$godot_bin" --path "$project_dir"
