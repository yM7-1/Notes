#!/usr/bin/env bash
set -euo pipefail

DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export DOTNET_ROOT
export PATH="$DOTNET_ROOT:$PATH"

STEAM_ROOT="${STEAM_ROOT:-/mnt/d/Steam}"
STS2_DATA_DIR="${STS2_DATA_DIR:-$STEAM_ROOT/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64}"
export STS2_DATA_DIR

cd "$(dirname "$0")/.."

echo "==> version sync (csproj / manifest / workshop manifest / changenote)"
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Notes.csproj | head -1)
[ -n "$VERSION" ] || { echo "!! no <Version> in Notes.csproj" >&2; exit 1; }
for file in Notes.json packaging/workshop/content/Notes/Notes.json; do
  file_version=$(sed -n 's|.*"version": *"\([^"]*\)".*|\1|p' "$file" | head -1)
  if [ "$file_version" != "$VERSION" ]; then
    echo "!! version drift: $file has '$file_version', Notes.csproj has '$VERSION'" >&2
    exit 1
  fi
done
if ! grep -q "v$VERSION" packaging/workshop/Notes_workshop.vdf; then
  echo "!! workshop changenote does not mention v$VERSION" >&2
  exit 1
fi
echo "    v$VERSION ok"

REF_DIR="${STS2_REFS_DIR:-$PWD/.refs/sts2-refs}"
REF_MIN="$REF_DIR/0.107.1"

if [ ! -f "$REF_MIN/sts2.dll" ]; then
  echo "!! min-version reference assemblies missing at $REF_MIN" >&2
  echo "   run: bash tools/fetch-refs.sh" >&2
  exit 1
fi

echo "==> build (installed game: $STS2_DATA_DIR)"
dotnet build Notes.csproj -c Release -p:CopyModOnBuild=false \
  -p:Sts2UseLocalGame=true \
  -p:SteamRoot="$STEAM_ROOT" \
  -p:Sts2DataDir="$STS2_DATA_DIR" \
  -p:RitsuLibReferenceTarget=0.111.0 \
  -v minimal -nologo

echo "==> test (core)"
dotnet test tests/Notes.Tests/Notes.Tests.csproj -c Release \
  -v minimal -nologo

echo "==> build (min game version 0.107.1)"
dotnet build Notes.csproj -c Release -p:CopyModOnBuild=false \
  -p:SteamRoot="$STEAM_ROOT" \
  -p:Sts2DataDir="$REF_MIN" \
  -p:RitsuLibReferenceTarget=0.107.1 \
  -v minimal -nologo

echo "==> check done"
