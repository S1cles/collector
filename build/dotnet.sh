#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$repo/.cache/nuget"
exec podman run --rm \
  -v "$repo":/src:Z \
  -v "$repo/.cache/nuget":/root/.nuget/packages:Z \
  -w /src \
  -e DOTNET_NOLOGO=1 \
  mcr.microsoft.com/dotnet/sdk:10.0 dotnet "$@"
