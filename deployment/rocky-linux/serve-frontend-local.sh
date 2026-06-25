#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="/opt/project-time-platform"
REPO_DIR="$APP_ROOT/app/project-time-platform"
SERVER_SCRIPT="$REPO_DIR/deployment/rocky-linux/serve-frontend-local.py"
DIST_DIR="$REPO_DIR/src/frontend/project-time-web/dist"

if [ ! -d "$DIST_DIR" ]; then
  echo "ERROR: Missing frontend build directory: $DIST_DIR"
  echo "Run ./deployment/rocky-linux/build-frontend.sh first."
  exit 1
fi

if [ ! -f "$SERVER_SCRIPT" ]; then
  echo "ERROR: Missing $SERVER_SCRIPT"
  exit 1
fi

python3 "$SERVER_SCRIPT" --host 127.0.0.1 --port 5173
