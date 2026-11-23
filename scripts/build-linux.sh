#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd "$(dirname "$0")/.." && pwd)"
export LD_LIBRARY_PATH="$project_dir/.tools/unity-compat/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export VRPORTAL_BUILD_DIR="$project_dir/Builds/Linux"
mkdir -p "$project_dir/Logs" "$VRPORTAL_BUILD_DIR"
"$UNITY_EDITOR" -batchmode -nographics -quit -buildTarget Linux64 \
  -projectPath "$project_dir" -executeMethod DesktopDemoBuild.Build \
  -logFile "$project_dir/Logs/build-linux.log"
