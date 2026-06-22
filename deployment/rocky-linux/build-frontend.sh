#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="/opt/project-time-platform"
REPO_DIR="$APP_ROOT/app/project-time-platform"
FRONTEND_DIR="$REPO_DIR/src/frontend/project-time-web"

if [ ! -d "$FRONTEND_DIR" ]; then
  echo "ERROR: Missing $FRONTEND_DIR"
  exit 1
fi

cd "$FRONTEND_DIR"

echo "==> Node.js version"
node --version

echo "==> npm version"
npm --version

echo "==> Installing frontend dependencies"
npm install

echo "==> Building frontend"
npm run build

echo "==> Frontend build complete"
ls -la dist
