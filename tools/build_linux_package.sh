#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "Usage: $0 <version> <linux-x64|linux-arm64> <output-directory>" >&2
  exit 2
fi

version="$1"
rid="$2"
output_directory="$3"

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$ ]]; then
  echo "Invalid version: $version" >&2
  exit 2
fi
if [[ "$rid" != "linux-x64" && "$rid" != "linux-arm64" ]]; then
  echo "Unsupported runtime identifier: $rid" >&2
  exit 2
fi
host_os="$(uname -s)"
if [[ "$host_os" != "Linux" && "$host_os" != "Darwin" ]]; then
  echo "Linux packages must be built on Linux or cross-published from macOS." >&2
  exit 2
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_directory="$(mkdir -p "$output_directory" && cd "$output_directory" && pwd)"
export AVALONIA_TELEMETRY_OPTOUT=1
work_root="$(mktemp -d "${TMPDIR:-/tmp}/device-widget-linux.XXXXXX")"
trap 'rm -rf "$work_root"' EXIT

publish_directory="$work_root/publish"
package_directory="$work_root/DeviceWidget"
binary="$package_directory/DeviceWidget"
archive="$output_directory/DeviceWidget-for-Android-$version-$rid.tar.gz"
scrcpy_version="4.0"
scrcpy_asset="scrcpy-linux-x86_64-v$scrcpy_version.tar.gz"
scrcpy_url="https://github.com/Genymobile/scrcpy/releases/download/v$scrcpy_version/$scrcpy_asset"
scrcpy_sha256="7daf05af5d575862e62b068cf6852d6068faf7ef3178f3735e3953e778fbf0ab"

sha256_file() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{ print $1 }'
  else
    shasum -a 256 "$1" | awk '{ print $1 }'
  fi
}

binary_description() {
  file -b "$1"
}

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

cp -R "$publish_directory" "$package_directory"
find "$package_directory" -type f -name '*.pdb' -delete
chmod 755 "$binary"

if [[ "$rid" == "linux-x64" ]]; then
  scrcpy_archive="$work_root/$scrcpy_asset"
  scrcpy_extract="$work_root/scrcpy-extract"
  if [[ -n "${SCRCPY_LINUX_ARCHIVE:-}" ]]; then
    cp "$SCRCPY_LINUX_ARCHIVE" "$scrcpy_archive"
  else
    curl --fail --location --retry 3 "$scrcpy_url" --output "$scrcpy_archive"
  fi
  actual_scrcpy_sha256="$(sha256_file "$scrcpy_archive")"
  if [[ "$actual_scrcpy_sha256" != "$scrcpy_sha256" ]]; then
    echo "Unexpected scrcpy $scrcpy_version SHA-256: $actual_scrcpy_sha256" >&2
    exit 1
  fi
  mkdir -p "$scrcpy_extract" "$package_directory/tools/scrcpy-$scrcpy_version"
  tar -xzf "$scrcpy_archive" -C "$scrcpy_extract"
  cp -R "$scrcpy_extract/scrcpy-linux-x86_64-v$scrcpy_version/." \
    "$package_directory/tools/scrcpy-$scrcpy_version"
  chmod 755 "$package_directory/tools/scrcpy-$scrcpy_version/adb" \
    "$package_directory/tools/scrcpy-$scrcpy_version/scrcpy"
  bundled_adb_machine="$(binary_description "$package_directory/tools/scrcpy-$scrcpy_version/adb")"
  bundled_scrcpy_machine="$(binary_description "$package_directory/tools/scrcpy-$scrcpy_version/scrcpy")"
  if [[ "$bundled_adb_machine" != *"x86-64"* || "$bundled_scrcpy_machine" != *"x86-64"* ]]; then
    echo "Unexpected bundled Linux tool architecture: adb=$bundled_adb_machine scrcpy=$bundled_scrcpy_machine" >&2
    exit 1
  fi
fi

machine="$(binary_description "$binary")"
if [[ "$rid" == "linux-x64" && "$machine" != *"x86-64"* ]]; then
  echo "Unexpected ELF architecture: $machine" >&2
  exit 1
fi
if [[ "$rid" == "linux-arm64" && "$machine" != *"ARM aarch64"* && "$machine" != *"ARM64"* ]]; then
  echo "Unexpected ELF architecture: $machine" >&2
  exit 1
fi

tar -czf "$archive" -C "$work_root" DeviceWidget
archive_mode="$(tar -tvzf "$archive" | awk '$NF == "DeviceWidget/DeviceWidget" { print $1; exit }')"
if [[ "$archive_mode" != *x* ]]; then
  echo "Executable permission was not preserved in archive: $archive_mode" >&2
  exit 1
fi

archive_entries="$(tar -tzf "$archive")"
for required in LICENSE THIRD_PARTY_NOTICES.md SOURCE_OFFER.md; do
  if ! grep -Fxq "DeviceWidget/$required" <<<"$archive_entries"; then
    echo "$required is missing from archive." >&2
    exit 1
  fi
done
if [[ "$rid" == "linux-x64" ]]; then
  for tool in adb scrcpy scrcpy-server; do
    if ! grep -Fxq "DeviceWidget/tools/scrcpy-$scrcpy_version/$tool" <<<"$archive_entries"; then
      echo "Bundled $tool is missing from archive." >&2
      exit 1
    fi
  done
fi

echo "$(sha256_file "$archive")  $archive"
echo "$archive"
