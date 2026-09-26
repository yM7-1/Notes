#!/usr/bin/env bash
# 建好远端空仓库后使用：REMOTE_URL=https://github.com/yM7-1/Notes.git bash tools/publish.sh
set -euo pipefail
cd "$(dirname "$0")/.."

: "${REMOTE_URL:?请设置 REMOTE_URL，例如 https://github.com/yM7-1/Notes.git}"

git remote add origin "$REMOTE_URL" 2>/dev/null || git remote set-url origin "$REMOTE_URL"
git push -u origin main
git tag v0.1.0 2>/dev/null || true
git push origin v0.1.0
echo "[publish] done: $REMOTE_URL"
