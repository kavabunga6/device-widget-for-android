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
if [[ "$(uname -s)" != "Linux" ]]; then
  echo "Linux packages must be built on Linux." >&2
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

machine="$(readelf -h "$binary" | awk -F: '/Machine:/ { gsub(/^[[:space:]]+/, "", $2); print $2; exit }')"
if [[ "$rid" == "linux-x64" && "$machine" != *"X86-64"* ]]; then
  echo "Unexpected ELF architecture: $machine" >&2
  exit 1
fi
if [[ "$rid" == "linux-arm64" && "$machine" != *"AArch64"* ]]; then
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

sha256sum "$archive"
echo "$archive"
