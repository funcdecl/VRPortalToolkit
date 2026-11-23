#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd "$(dirname "$0")/.." && pwd)"
output="$project_dir/Artifacts/Linux"
if [ "$#" -gt 0 ]; then output="$1"; fi
mkdir -p "$output"
bash "$project_dir/scripts/run-linux.sh" -portal-smoke-test "$output" \
  -logFile "$output/player.log"
cat "$output/smoke-result.json"
