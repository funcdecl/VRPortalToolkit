#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$project_dir/.tools/downloads" "$project_dir/.tools/unity-compat"
curl -fL --retry 2 \
  https://archive.ubuntu.com/ubuntu/pool/main/o/openssl/libssl1.1_1.1.1f-1ubuntu2.24_amd64.deb \
  -o "$project_dir/.tools/downloads/libssl1.1.deb"
dpkg-deb -x "$project_dir/.tools/downloads/libssl1.1.deb" "$project_dir/.tools/unity-compat"
printf 'Unity compiler compatibility libraries installed.\n'
