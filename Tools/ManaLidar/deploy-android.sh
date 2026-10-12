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

# Build a standalone signed apk and update it in place: `-t:Run` uninstalls first, which loses the home-screen
# shortcut and the camera permission every deploy.
dotnet build ManaLidar.Android -c "$config" -t:SignAndroidPackage --nologo -v:q -p:EmbedAssembliesIntoApk=true \
  -p:AndroidSdkDirectory="$sdk" -p:JavaSdkDirectory="$JAVA_HOME"
apk="$(ls -t Artifacts/Bin/ManaLidar.Android/"$config"/net10.0-android/android-arm64/*-Signed.apk | head -1)"
"$adb" -s "$device" install -r "$apk"
"$adb" -s "$device" shell monkey -p com.manarenderer.manalidar -c android.intent.category.LAUNCHER 1 >/dev/null
