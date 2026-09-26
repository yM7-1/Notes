#!/usr/bin/env bash
# Fetch Slay the Spire 2 reference assemblies for the min-version compile
# check (0.107.1) and the beta audit (0.111.0).
#
# Source: Book.StS2.RefLib on NuGet (reference-only stubs, published with
# MegaCrit's permission). Unlike refasmer-generated stubs these keep private
# members, so patch-target audits can run against them.
#
# Layout: .refs/sts2-refs/<game-version>/{sts2,GodotSharp,0Harmony}.dll
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${STS2_REFS_DIR:-$ROOT/.refs/sts2-refs}"
BASE="https://api.nuget.org/v3-flatcontainer"

fetch() {
  local pkg="$1" pkg_version="$2" dir_name="$3"
  local dir="$OUT/$dir_name"
  local tmp
  tmp="$(mktemp -d)"

  if [ -f "$dir/sts2.dll" ]; then
    echo "==> $dir_name already present"
  else
    echo "==> fetching $pkg $pkg_version"
    curl -fsSL "$BASE/$pkg/$pkg_version/$pkg.$pkg_version.nupkg" -o "$tmp/pkg.nupkg"
    python3 - "$tmp/pkg.nupkg" "$dir" <<'PY'
import os
import shutil
import sys
import zipfile

nupkg, dest = sys.argv[1], sys.argv[2]
os.makedirs(dest, exist_ok=True)
with zipfile.ZipFile(nupkg) as z:
    for name in z.namelist():
        if name.startswith("ref/") and name.endswith(".dll"):
            with z.open(name) as src, open(os.path.join(dest, os.path.basename(name)), "wb") as out:
                shutil.copyfileobj(src, out)
PY
    echo "    -> $dir"
  fi
  rm -rf "$tmp"
}

fetch book.sts2.reflib 0.107.1 0.107.1
fetch book.sts2.reflib 0.111.0-beta 0.111.0
