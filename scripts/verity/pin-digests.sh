#!/usr/bin/env bash
# Auto-pin the released image digests into .verity/release.env.
#
# Reads the digest manifest (release-digests.json) that release.yml attached to a
# GitHub Release and writes the pinned, by-digest image refs that deploy.sh /
# deploy.compose.yml consume. Never hand-copy digests — always run this.
#
# Usage:
#   scripts/verity/pin-digests.sh [vX.Y.Z]     # defaults to the latest release
set -Eeuo pipefail
umask 077

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${REPO_ROOT}/.verity/release.env"
TAG="${1:-}"

command -v gh >/dev/null 2>&1 || { echo "gh CLI required" >&2; exit 1; }

if [ -z "$TAG" ]; then
  TAG="$(gh release view --json tagName -q .tagName)"
fi
[[ "$TAG" =~ ^v?[0-9]+\.[0-9]+\.[0-9]+([-+][A-Za-z0-9.-]+)?$ ]] || { echo 'Invalid release tag' >&2; exit 1; }
echo "Pinning digests from release ${TAG}"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
gh release download "$TAG" --pattern release-digests.json --dir "$TMP" --clobber

python3 "$REPO_ROOT/scripts/verity/release-config.py" manifest \
  "$TMP/release-digests.json" --tag "$TAG" --output "$OUT"

echo "Wrote ${OUT}:"
cat "$OUT"
