#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
csproj="$repo/src/SpectraCollector.App/SpectraCollector.App.csproj"

version="$(sed -n 's|.*<Version>\([^<]*\)</Version>.*|\1|p' "$csproj" | head -n 1)"
[[ -n "$version" ]] || { echo "No <Version> in $csproj" >&2; exit 1; }

"$repo/build/dotnet.sh" test
"$repo/build/dotnet.sh" publish src/SpectraCollector.App -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish/app
cp "$repo/publish/app/SpectraCollector.exe" "$repo/publish/SpectraCollector.exe"

podman run --rm -v "$repo":/w:Z -w /w docker.io/library/debian:trixie-slim bash -c \
  'export DEBIAN_FRONTEND=noninteractive; apt-get update -qq && apt-get install -y -qq --no-install-recommends nsis >/dev/null &&
   makensis -V2 -DVERSION="$0" -DAPPEXE=/w/publish/app/SpectraCollector.exe \
     -DICON=/w/src/SpectraCollector.App/Assets/SpectraCollector.ico \
     -DOUTFILE=/w/publish/SpectraCollector-Setup.exe /w/installer/SpectraCollector.nsi' "$version"

ls -l "$repo/publish/SpectraCollector.exe" "$repo/publish/SpectraCollector-Setup.exe"
