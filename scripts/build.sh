#!/usr/bin/env bash
# Cross-platform build script (Linux/macOS/CI): build + test + publish win-x64 single-file EXE.
# Usage: bash scripts/build.sh [Release|Debug]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG="${1:-Release}"
OUT="$ROOT/dist/win-x64"

# Locate dotnet (support custom installs like ~/.cache/dotnet)
if command -v dotnet >/dev/null 2>&1; then
  DOTNET=dotnet
elif [ -x "$HOME/.cache/dotnet/dotnet" ]; then
  DOTNET="$HOME/.cache/dotnet/dotnet"
elif [ -x "$HOME/.dotnet/dotnet" ]; then
  DOTNET="$HOME/.dotnet/dotnet"
else
  echo "ERROR: .NET 8 SDK not found. Install from https://dotnet.microsoft.com/download/dotnet/8.0" >&2
  exit 1
fi

export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

echo "==> Using: $($DOTNET --version)  |  Config: $CONFIG"

echo "==> Restore"
"$DOTNET" restore "$ROOT/WindowsCustomizer.sln"

echo "==> Build"
"$DOTNET" build "$ROOT/WindowsCustomizer.sln" -c "$CONFIG" --no-restore

echo "==> Test"
"$DOTNET" test "$ROOT/tests/WindowsCustomizer.Core.Tests/WindowsCustomizer.Core.Tests.csproj" -c "$CONFIG" --no-build --logger "console;verbosity=normal"

echo "==> Publish (single-file, win-x64, framework-dependent)"
"$DOTNET" publish "$ROOT/src/WindowsCustomizer.App/WindowsCustomizer.App.csproj" \
  -c "$CONFIG" -r win-x64 --self-contained false \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None \
  -o "$OUT"

echo ""
echo "Done. Output: $OUT/WindowsCustomizer.exe"
echo "(Requires .NET 8 Desktop Runtime on the target machine. For a self-contained build run:)"
echo "  $DOTNET publish $ROOT/src/WindowsCustomizer.App -c $CONFIG -r win-x64 --self-contained true -p:PublishSingleFile=true -o $ROOT/dist/win-x64-sc"
