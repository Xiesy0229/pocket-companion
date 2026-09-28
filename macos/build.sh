#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
app="dist/Pocket Companion.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" dist/macos-build
cp macos/Info.plist "$app/Contents/Info.plist"
cp assets/duo.png "$app/Contents/Resources/duo.png"
for arch in arm64 x86_64; do
  xcrun swiftc -swift-version 5 -O -framework AppKit -target "$arch-apple-macos12.0" macos/main.swift -o "dist/macos-build/PocketCompanion-$arch"
done
xcrun lipo -create dist/macos-build/PocketCompanion-arm64 dist/macos-build/PocketCompanion-x86_64 -output "$app/Contents/MacOS/PocketCompanion"
codesign --force --sign - "$app"
codesign --verify --deep --strict "$app"
"$app/Contents/MacOS/PocketCompanion" --self-test
ditto -c -k --sequesterRsrc --keepParent "$app" dist/PocketCompanion-macOS-universal-v0.2.0.zip
echo "Ready: dist/PocketCompanion-macOS-universal-v0.2.0.zip"
