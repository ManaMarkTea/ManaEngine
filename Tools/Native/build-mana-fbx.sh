#!/bin/sh
# Builds ManaEditor's FBX reader (ufbx + mana_fbx.c) for macOS arm64 into ManaEditor/Native/Fbx/runtimes.
# Windows' is built by build-mana-fbx.ps1. Commit the result: the editor loads it from there, no compiler needed.
set -eu
root="$(cd "$(dirname "$0")/../.." && pwd)"
source="$root/ManaEditor/Native/Fbx"
output="$source/runtimes/osx-arm64"
mkdir -p "$output"
clang -O2 -arch arm64 -mmacosx-version-min=12.0 -fvisibility=hidden -shared \
    -o "$output/libmana_fbx.dylib" "$source/mana_fbx.c" "$source/ufbx/ufbx.c" -lm
ls -l "$output/libmana_fbx.dylib"
