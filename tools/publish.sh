#!/usr/bin/env bash
# 建好远端空仓库后使用：REMOTE_URL=https://github.com/yM7-1/Notes.git bash tools/publish.sh
set -euo pipefail
cd "$(dirname "$0")/.."

: "${REMOTE_URL:?请设置 REMOTE_URL，例如 https://github.com/yM7-1/Notes.git}"

# Tag from the project version (single source of truth: Notes.csproj).
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Notes.csproj | head -1)
[ -n "$VERSION" ] || { echo "!! no <Version> in Notes.csproj" >&2; exit 1; }

git remote add origin "$REMOTE_URL" 2>/dev/null || git remote set-url origin "$REMOTE_URL"
git push -u origin main
git tag "v$VERSION" 2>/dev/null || true
git push origin "v$VERSION"
echo "[publish] done: $REMOTE_URL (tag v$VERSION)"
