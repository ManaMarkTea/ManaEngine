#!/usr/bin/env bash
# Builds ManaLidar.Android and installs + launches it on the connected Android phone (a Pixel with ARCore).
# Usage: Tools/ManaLidar/deploy-android.sh [Debug|Release]    DEVICE=<adb serial> picks one when several are attached.
set -euo pipefail
cd "$(dirname "$0")/../.."
config="${1:-Debug}"
sdk="${ANDROID_HOME:-$HOME/Library/Android/sdk}"
adb="$sdk/platform-tools/adb"
export JAVA_HOME="${JAVA_HOME:-/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home}"

device="${DEVICE:-$("$adb" devices | awk 'NR > 1 && $2 == "device" { print $1; exit }')}"
if [ -z "$device" ]; then
  echo "No Android device found: plug it in, enable USB debugging and accept the prompt (or set DEVICE=<serial>)." >&2
  exit 1
fi
echo "Device: $device"

# -t:Run installs (fast deployment needs it) and starts the activity.
dotnet build ManaLidar.Android -c "$config" -t:Run --nologo -v:q \
  -p:AndroidSdkDirectory="$sdk" -p:JavaSdkDirectory="$JAVA_HOME" -p:AdbTarget="-s $device"
