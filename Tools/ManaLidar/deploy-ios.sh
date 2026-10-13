#!/usr/bin/env bash
# Builds an iOS app -- ManaLidar, or APP=ManaVoxel.iOS -- and installs + launches it on the connected iPhone or iPad
# (Mac only). Usage: Tools/ManaLidar/deploy-ios.sh [Debug|Release]    DEVICE=<udid or name> picks one when several are paired.
# Installs with devicectl: `dotnet build -t:Run` hangs in mlaunch's install step here.
set -euo pipefail
cd "$(dirname "$0")/../.."
config="${1:-Debug}"
app_project="${APP:-ManaLidar}"
case "$app_project" in
  ManaLidar) bundle_id="com.manarenderer.manalidar" ;;
  ManaVoxel.iOS) bundle_id="com.manarenderer.manavoxel" ;;
  *) echo "Unknown app $app_project (ManaLidar or ManaVoxel.iOS)." >&2; exit 1 ;;
esac

device="${DEVICE:-}"
if [ -z "$device" ]; then
  json="$(mktemp)"
  xcrun devicectl list devices --json-output "$json" -q >/dev/null 2>&1 || true
  device="$(python3 - "$json" <<'PY'
import json, sys
devices = json.load(open(sys.argv[1]))["result"]["devices"]
# Wi-Fi phones read "disconnected" until used (the tunnel opens on demand), so paired is enough; USB ones sort first.
ready = [d for d in devices
         if d["hardwareProperties"]["deviceType"] in ("iPhone", "iPad")
         and d["connectionProperties"].get("pairingState") == "paired"]
ready.sort(key=lambda d: d["connectionProperties"].get("transportType") != "wired")
print(ready[0]["hardwareProperties"]["udid"] if ready else "")
PY
)"
  rm -f "$json"
fi
if [ -z "$device" ]; then
  echo "No iPhone or iPad found: plug it in, unlock it and trust this Mac (or set DEVICE=<udid>)." >&2
  exit 1
fi
echo "Device: $device"

# Fresh build processes each time: MSBuild's reused workers belong to the SSH session that started them, where the
# signing keychain this session unlocked still reads as locked (codesign: errSecInternalComponent).
dotnet build-server shutdown >/dev/null 2>&1 || true
# The iOS SDK's incremental AOT can keep stale code, and Mono aborts at launch on the mismatch: a changed assembly
# changes what the trimmer keeps of .NET itself, so even the framework's cached code goes stale. Compiled fresh
# each build until the cache can be kept safely.
rm -rf "Artifacts/Obj/$app_project/$config/net10.0-ios/ios-arm64/nativelibraries"
dotnet build "$app_project" -c "$config" --nologo -v:q -nodeReuse:false
app="$(ls -d "Artifacts/Bin/$app_project/$config/net10.0-ios/ios-arm64/"*.app | head -1)"
xcrun devicectl device install app --device "$device" "$app"
if [ -n "${CONSOLE:-}" ]; then
  # The app's own output (Console.WriteLine, crashes) for CONSOLE seconds, then the app is left running.
  log="$(mktemp)"
  xcrun devicectl device process launch --device "$device" --console --terminate-existing "$bundle_id" >"$log" 2>&1 &
  watcher=$!
  sleep "$CONSOLE"
  kill "$watcher" 2>/dev/null || true
  cat "$log"
  rm -f "$log"
else
  xcrun devicectl device process launch --device "$device" "$bundle_id" || echo "Installed; unlock the device and open the app." >&2
fi
