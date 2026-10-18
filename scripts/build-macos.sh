#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
DOTNET_BIN="${DOTNET_BIN:-dotnet}"
task_rid="${1:-osx-arm64}"
case "$task_rid" in osx-arm64|osx-x64) ;; *) echo "Use osx-arm64 or osx-x64" >&2; exit 1;; esac
task_publish_root="${MACOS_OUTPUT_ROOT:-$PWD/publish}"
task_version="$(tr -d '\r\n' < version.txt)"
task_output="$task_publish_root/$task_rid"
task_bundle="$task_publish_root/GithubLauncher-Mac.app"
"$DOTNET_BIN" publish GithubLauncher.csproj -c Release -r "$task_rid" --self-contained true \
    -p:PublishSingleFile=false -p:PublishTrimmed=false -o "$task_output"
# A previous build can contain native libraries for a different architecture.
rm -rf "$task_bundle"
mkdir -p "$task_bundle/Contents/MacOS" "$task_bundle/Contents/Resources"
cp -R "$task_output/" "$task_bundle/Contents/MacOS/"
chmod +x "$task_bundle/Contents/MacOS/GithubLauncher"
task_iconset="$task_publish_root/GithubLauncher.iconset"
mkdir -p "$task_iconset"
for task_size in 16 32 128 256 512; do
    sips -z "$task_size" "$task_size" Assets/app.png --out "$task_iconset/icon_${task_size}x${task_size}.png" >/dev/null
    task_retina_size=$((task_size * 2))
    sips -z "$task_retina_size" "$task_retina_size" Assets/app.png --out "$task_iconset/icon_${task_size}x${task_size}@2x.png" >/dev/null
done
# A distinct resource name prevents Finder/Dock from reusing an older icon.
task_icon_digest="$(shasum -a 256 Assets/app.png)"
task_icon_name="GithubLauncher-${task_icon_digest:0:12}.icns"
iconutil -c icns "$task_iconset" -o "$task_bundle/Contents/Resources/$task_icon_name"
rm -rf "$task_iconset"
task_sdl="${SDL2_LIBRARY:-/opt/homebrew/lib/libSDL2.dylib}"
task_arch="${task_rid#osx-}"
if [ "$task_arch" = "x64" ]; then task_arch="x86_64"; fi
if [ -f "$task_sdl" ] && lipo "$task_sdl" -verify_arch "$task_arch" 2>/dev/null; then
    if ! cmp -s "$task_sdl" "$task_bundle/Contents/MacOS/libSDL2.dylib"; then
        cp -f -L "$task_sdl" "$task_bundle/Contents/MacOS/libSDL2.dylib"
    fi
    task_sdl_license="${SDL2_LICENSE:-/opt/homebrew/opt/sdl2/LICENSE.txt}"
    if [ -f "$task_sdl_license" ]; then
        cp "$task_sdl_license" "$task_bundle/Contents/Resources/SDL2-LICENSE.txt"
    fi
fi
cat > "$task_bundle/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>GithubLauncher</string>
<key>CFBundleIdentifier</key><string>io.gitopsgarage.githublauncher</string>
<key>CFBundleName</key><string>Github Launcher Mac — GitOpsGarage</string>
<key>CFBundleDisplayName</key><string>GitHub Launcher Mac</string>
<key>CFBundleIconFile</key><string>GithubLauncher.icns</string>
<key>CFBundleVersion</key><string>3</string>
<key>CFBundleShortVersionString</key><string>1.0.0</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>LSMinimumSystemVersion</key><string>12.0</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
/usr/libexec/PlistBuddy -c "Set :CFBundleIconFile $task_icon_name" "$task_bundle/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString ${task_version%%-*}" "$task_bundle/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :LSMinimumSystemVersion ${MACOS_MIN_VERSION:-12.0}" "$task_bundle/Contents/Info.plist"
cp LICENSE "$task_bundle/Contents/Resources/LICENSE.txt"
cp lib/GitHubLauncher.Core/LICENSE "$task_bundle/Contents/Resources/GitHubLauncher.Core-LICENSE.txt"
cp CHANGELOG.md MACOS.md "$task_bundle/Contents/Resources/"
echo "Built: $task_bundle"
