#!/usr/bin/env bash
set -euo pipefail

# Browser for Tools/WebAssets/webdrive.mjs; the wasm-tools workload is what the web publish needs.
sudo apt-get update
sudo apt-get install -y --no-install-recommends chromium libgl1 libx11-6 libxkbcommon0
dotnet workload install wasm-tools --skip-manifest-update
git lfs pull || true
dotnet restore
