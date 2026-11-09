#!/usr/bin/env bash
# Builds wgpu-native for iOS (device + simulator) as wgpu_native.xcframework, once, into a cache outside the repo
# (Mac only; ManaEditor.iOS runs it before building when the framework is missing). Prints the xcframework's path.
#
# Silk.NET.WebGPU.Native.WGPU ships desktop builds only. Its bindings are written against one wgpu-native commit,
# so the phone's library is built from that same commit: Silk.NET 2.23's binaries are 33133da4 (May 2024) -- the
# only commit whose exports match theirs exactly. Change it together with the Silk.NET version.
set -euo pipefail
commit="33133da4ec5a0174cb21539ef2d3346f75200411"
cache="${MANA_WGPU_CACHE:-$HOME/Library/Caches/ManaRenderer/wgpu-native-${commit:0:8}}"
out="$cache/wgpu_native.xcframework"
if [ -d "$out" ]; then
  echo "$out"
  exit 0
fi

[ -f "$HOME/.cargo/env" ] && source "$HOME/.cargo/env"
if ! command -v cargo >/dev/null; then
  echo "Rust is needed once to build wgpu for iOS: curl --proto '=https' -sSf https://sh.rustup.rs | sh" >&2
  exit 1
fi
rustup target add aarch64-apple-ios aarch64-apple-ios-sim >&2

src="$cache/src"
if [ ! -d "$src/.git" ]; then
  git clone -q https://github.com/gfx-rs/wgpu-native.git "$src"
fi
git -C "$src" fetch -q origin
git -C "$src" checkout -q "$commit"
git -C "$src" submodule update --init -q

export IPHONEOS_DEPLOYMENT_TARGET=17.0
(cd "$src" && cargo build --release --target aarch64-apple-ios >&2 && cargo build --release --target aarch64-apple-ios-sim >&2)

# A dylib inside a .framework: the only shape iOS lets an app carry its own dynamic library in.
frameworks=()
for target in aarch64-apple-ios aarch64-apple-ios-sim; do
  platform=iPhoneOS
  [ "$target" = aarch64-apple-ios-sim ] && platform=iPhoneSimulator
  fw="$cache/$target/wgpu_native.framework"
  rm -rf "$fw" && mkdir -p "$fw"
  cp "$src/target/$target/release/libwgpu_native.dylib" "$fw/wgpu_native"
  install_name_tool -id @rpath/wgpu_native.framework/wgpu_native "$fw/wgpu_native"
  cat > "$fw/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleExecutable</key><string>wgpu_native</string>
  <key>CFBundleIdentifier</key><string>io.github.gfx-rs.wgpu-native</string>
  <key>CFBundleName</key><string>wgpu_native</string>
  <key>CFBundlePackageType</key><string>FMWK</string>
  <key>CFBundleShortVersionString</key><string>0.19.4</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>CFBundleSupportedPlatforms</key><array><string>$platform</string></array>
  <key>MinimumOSVersion</key><string>$IPHONEOS_DEPLOYMENT_TARGET</string>
</dict></plist>
PLIST
  frameworks+=(-framework "$fw")
done
xcodebuild -create-xcframework "${frameworks[@]}" -output "$out" >&2
echo "$out"
