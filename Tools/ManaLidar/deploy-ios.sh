#!/usr/bin/env bash
# Builds ManaLidar and installs + launches it on the connected iPhone (Mac only).
# Usage: Tools/ManaLidar/deploy-ios.sh [Debug|Release]    DEVICE=<udid or name> picks one when several are plugged in.
# Installs with devicectl: `dotnet build -t:Run` hangs in mlaunch's install step here.
set -euo pipefail
cd "$(dirname "$0")/../.."
config="${1:-Debug}"
bundle_id="com.manarenderer.manalidar"

device="${DEVICE:-}"
if [ -z "$device" ]; then
  json="$(mktemp)"
  xcrun devicectl list devices --json-output "$json" -q >/dev/null 2>&1 || true
  device="$(python3 - "$json" <<'PY'
import json, sys
devices = json.load(open(sys.argv[1]))["result"]["devices"]
# Wi-Fi phones read "disconnected" until used (the tunnel opens on demand), so paired is enough; USB ones sort first.
ready = [d for d in devices
         if d["hardwareProperties"]["deviceType"] == "iPhone"
         and d["connectionProperties"].get("pairingState") == "paired"]
ready.sort(key=lambda d: d["connectionProperties"].get("transportType") != "wired")
print(ready[0]["hardwareProperties"]["udid"] if ready else "")
PY
)"
  rm -f "$json"
fi
if [ -z "$device" ]; then
  echo "No iPhone found: plug it in, unlock it and trust this Mac (or set DEVICE=<udid>)." >&2
  exit 1
fi
echo "Device: $device"

dotnet build ManaLidar -c "$config" --nologo -v:q
app="Artifacts/Bin/ManaLidar/$config/net10.0-ios/ios-arm64/ManaLidar.app"
xcrun devicectl device install app --device "$device" "$app"
xcrun devicectl device process launch --device "$device" "$bundle_id" || echo "Installed; unlock the phone and open ManaLidar." >&2
