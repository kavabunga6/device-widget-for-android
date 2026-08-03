#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "Usage: $0 <version> <osx-x64|osx-arm64> <output-directory>" >&2
  exit 2
fi

version="$1"
rid="$2"
output_directory="$3"

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$ ]]; then
  echo "Invalid version: $version" >&2
  exit 2
fi
if [[ "$rid" != "osx-x64" && "$rid" != "osx-arm64" ]]; then
  echo "Unsupported runtime identifier: $rid" >&2
  exit 2
fi
if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "macOS packages must be built on macOS." >&2
  exit 2
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_directory="$(mkdir -p "$output_directory" && cd "$output_directory" && pwd)"
export AVALONIA_TELEMETRY_OPTOUT=1
work_root="$(mktemp -d "${TMPDIR:-/tmp}/device-widget-macos.XXXXXX")"
trap 'rm -rf "$work_root"' EXIT

publish_directory="$work_root/publish"
app="$work_root/Device Widget.app"
contents="$app/Contents"
macos="$contents/MacOS"
resources="$contents/Resources"
icon_source="$repo_root/src/AndroidWidget.Desktop/Assets/AppIcon.png"
iconset="$work_root/AppIcon.iconset"
archive="$output_directory/DeviceWidget-for-Android-$version-$rid.tar.gz"

dotnet publish "$repo_root/src/AndroidWidget.Desktop/AndroidWidget.Desktop.csproj" \
  --configuration Release \
  --runtime "$rid" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:Version="$version" \
  --output "$publish_directory"

mkdir -p "$macos" "$resources" "$iconset"
cp "$publish_directory/DeviceWidget" "$macos/DeviceWidget"
cp "$publish_directory/LICENSE" "$resources/LICENSE"
cp "$publish_directory/THIRD_PARTY_NOTICES.md" "$resources/THIRD_PARTY_NOTICES.md"
cp "$publish_directory/SOURCE_OFFER.md" "$resources/SOURCE_OFFER.md"
cp -R "$publish_directory/licenses" "$resources/licenses"
chmod 755 "$macos/DeviceWidget"

while read -r points pixels suffix; do
  sips -z "$pixels" "$pixels" "$icon_source" \
    --out "$iconset/icon_${points}x${points}${suffix:-}.png" >/dev/null
done <<'SIZES'
16 16
16 32 @2x
32 32
32 64 @2x
128 128
128 256 @2x
256 256
256 512 @2x
512 512
512 1024 @2x
SIZES
iconutil --convert icns "$iconset" --output "$resources/AppIcon.icns"

cat >"$contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleDisplayName</key><string>Device Widget for Android</string>
  <key>CFBundleExecutable</key><string>DeviceWidget</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundleIdentifier</key><string>dev.devicewidget.desktop</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>Device Widget</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

plutil -lint "$contents/Info.plist"
codesign --force --deep --sign - --timestamp=none "$app"
codesign --verify --deep --strict --verbose=2 "$app"

binary_architectures="$(lipo -archs "$macos/DeviceWidget")"
expected_architecture="x86_64"
if [[ "$rid" == "osx-arm64" ]]; then
  expected_architecture="arm64"
fi
if [[ " $binary_architectures " != *" $expected_architecture "* ]]; then
  echo "Unexpected Mach-O architecture: $binary_architectures" >&2
  exit 1
fi

COPYFILE_DISABLE=1 tar -czf "$archive" -C "$work_root" "Device Widget.app"
archive_mode="$(tar -tvzf "$archive" | awk '$0 ~ /\/DeviceWidget$/ { print $1; exit }')"
if [[ "$archive_mode" != *x* ]]; then
  echo "Executable permission was not preserved in archive: $archive_mode" >&2
  exit 1
fi
archive_entries="$(tar -tzf "$archive")"
if ! grep -Fxq "Device Widget.app/Contents/Resources/AppIcon.icns" <<<"$archive_entries"; then
  echo "AppIcon.icns is missing from archive." >&2
  exit 1
fi
if ! grep -Fxq "Device Widget.app/Contents/Info.plist" <<<"$archive_entries"; then
  echo "Info.plist is missing from archive." >&2
  exit 1
fi

shasum -a 256 "$archive"
echo "$archive"
