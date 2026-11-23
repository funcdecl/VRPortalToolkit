#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
: "${UNITY_EDITOR:?Set UNITY_EDITOR to the Linux Unity Editor executable}"
mkdir -p Logs
export LD_LIBRARY_PATH="$PWD/.tools/unity-compat/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export VRPORTAL_PRESENCE_BUILD="${VRPORTAL_PRESENCE_BUILD:-$PWD/Builds/PresenceLinux}"
"$UNITY_EDITOR" -batchmode -nographics -quit -buildTarget Linux64 -projectPath "$PWD" \
    -executeMethod VRPortalToolkit.Presence.PresenceBuild.Linux -logFile "$PWD/Logs/build-presence.log"
