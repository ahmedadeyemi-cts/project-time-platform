#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
API_PATCH="$REPO_DIR/deployment/rocky-linux/patches/role_enforcement_api.py"
UI_PATCH="$REPO_DIR/deployment/rocky-linux/patches/role_user_switcher_ui.py"
DIST_DIR="$REPO_DIR/src/frontend/project-time-web/dist"

if [ ! -f "$API_PATCH" ]; then
  echo "ERROR: Missing $API_PATCH"
  exit 1
fi

if [ ! -f "$UI_PATCH" ]; then
  echo "ERROR: Missing $UI_PATCH"
  exit 1
fi

python3 "$API_PATCH"
python3 "$UI_PATCH"

if [ -d "$DIST_DIR" ]; then
  echo "==> Removing stale frontend dist"
  rm -rf "$DIST_DIR"
fi

echo "==> Role enforcement and user switcher patch applied"
echo "==> Expected API version after redeploy: 0.5.8"
