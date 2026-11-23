#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd "$(dirname "$0")/.." && pwd)"
exec "$project_dir/Builds/Linux/VRPortalDemo.x86_64" -force-vulkan \
  -screen-fullscreen 0 -screen-width 1280 -screen-height 800 "$@"
