#!/bin/zsh
project_dir="${0:A:h}"
demo_app="$project_dir/../../outputs/three-echo-godot/ThreeEcho.app"
if [[ -d "$demo_app" ]]; then
    exec open "$demo_app"
fi
exec /Applications/Godot.app/Contents/MacOS/Godot --path "$project_dir"
