#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
exec "${VRPORTAL_PRESENCE_BUILD:-$PWD/Builds/PresenceLinux}/VRPortalPresence.x86_64" \
    -force-vulkan -screen-fullscreen 0 -screen-width 1280 -screen-height 800 "$@"
