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
scrcpy_version="4.0"
scrcpy_asset_arch="x86_64"
scrcpy_sha256="b83169f856d7022ed0e4428d98acea18dde2d63f49611b52ea137577ce4efe6b"
expected_architecture="x86_64"
if [[ "$rid" == "osx-arm64" ]]; then
  scrcpy_asset_arch="aarch64"
  scrcpy_sha256="f5167fe047fe4a2ae2c2ea8634c7145a4d64d0b6005f24bb45639a965b8c60d4"
  expected_architecture="arm64"
fi
scrcpy_asset="scrcpy-macos-$scrcpy_asset_arch-v$scrcpy_version.tar.gz"
scrcpy_url="https://github.com/Genymobile/scrcpy/releases/download/v$scrcpy_version/$scrcpy_asset"
scrcpy_archive="$work_root/$scrcpy_asset"
scrcpy_extract="$work_root/scrcpy-extract"

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

mkdir -p "$macos" "$resources" "$iconset" "$scrcpy_extract"
cp "$publish_directory/DeviceWidget" "$macos/DeviceWidget"
cp "$publish_directory/LICENSE" "$resources/LICENSE"
cp "$publish_directory/THIRD_PARTY_NOTICES.md" "$resources/THIRD_PARTY_NOTICES.md"
cp "$publish_directory/SOURCE_OFFER.md" "$resources/SOURCE_OFFER.md"
cp -R "$publish_directory/licenses" "$resources/licenses"
chmod 755 "$macos/DeviceWidget"

if [[ -n "${SCRCPY_MACOS_ARCHIVE:-}" ]]; then
  cp "$SCRCPY_MACOS_ARCHIVE" "$scrcpy_archive"
else
  curl --fail --location --retry 3 "$scrcpy_url" --output "$scrcpy_archive"
fi
actual_scrcpy_sha256="$(shasum -a 256 "$scrcpy_archive" | awk '{ print $1 }')"
if [[ "$actual_scrcpy_sha256" != "$scrcpy_sha256" ]]; then
  echo "Unexpected scrcpy $scrcpy_version SHA-256: $actual_scrcpy_sha256" >&2
  exit 1
fi
tar -xzf "$scrcpy_archive" -C "$scrcpy_extract"
bundled_tools="$resources/tools/scrcpy-$scrcpy_version"
mkdir -p "$bundled_tools"
cp -R "$scrcpy_extract/scrcpy-macos-$scrcpy_asset_arch-v$scrcpy_version/." "$bundled_tools"
lipo "$bundled_tools/adb" -thin "$expected_architecture" -output "$bundled_tools/adb.thin"
mv "$bundled_tools/adb.thin" "$bundled_tools/adb"
chmod 755 "$bundled_tools/adb" "$bundled_tools/scrcpy"
if [[ " $(lipo -archs "$bundled_tools/adb") " != *" $expected_architecture "* ]]; then
  echo "Unexpected bundled adb architecture." >&2
  exit 1
fi
if [[ " $(lipo -archs "$bundled_tools/scrcpy") " != *" $expected_architecture "* ]]; then
  echo "Unexpected bundled scrcpy architecture." >&2
  exit 1
fi
if [[ "$(uname -m)" == "$expected_architecture" ]]; then
  "$bundled_tools/adb" version
  "$bundled_tools/scrcpy" --version
fi

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
if ! grep -Fxq "Device Widget.app/Contents/Resources/tools/scrcpy-$scrcpy_version/adb" <<<"$archive_entries"; then
  echo "Bundled adb is missing from archive." >&2
  exit 1
fi
if ! grep -Fxq "Device Widget.app/Contents/Resources/tools/scrcpy-$scrcpy_version/scrcpy" <<<"$archive_entries"; then
  echo "Bundled scrcpy is missing from archive." >&2
  exit 1
fi
adb_archive_mode="$(tar -tvzf "$archive" | awk '$0 ~ "Resources/tools/scrcpy-4.0/adb$" { print $1; exit }')"
if [[ "$adb_archive_mode" != *x* ]]; then
  echo "Bundled adb executable permission was not preserved: $adb_archive_mode" >&2
  exit 1
fi
scrcpy_archive_mode="$(tar -tvzf "$archive" | awk '$0 ~ "Resources/tools/scrcpy-4.0/scrcpy$" { print $1; exit }')"
if [[ "$scrcpy_archive_mode" != *x* ]]; then
  echo "Bundled scrcpy executable permission was not preserved: $scrcpy_archive_mode" >&2
  exit 1
fi

shasum -a 256 "$archive"
echo "$archive"
