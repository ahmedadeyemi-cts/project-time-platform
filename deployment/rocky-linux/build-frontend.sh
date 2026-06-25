#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="/opt/project-time-platform"
REPO_DIR="$APP_ROOT/app/project-time-platform"
FRONTEND_DIR="$REPO_DIR/src/frontend/project-time-web"
MIN_NODE_MAJOR=18

if [ ! -d "$FRONTEND_DIR" ]; then
  echo "ERROR: Missing $FRONTEND_DIR"
  exit 1
fi

cd "$FRONTEND_DIR"

echo "==> Node.js version"
node --version

NODE_MAJOR=$(node -p "parseInt(process.versions.node.split('.')[0], 10)")
if [ "$NODE_MAJOR" -lt "$MIN_NODE_MAJOR" ]; then
  echo "ERROR: Node.js $MIN_NODE_MAJOR or higher is required for the frontend build."
  echo "Found: $(node --version)"
  echo "Run: ./deployment/rocky-linux/install-nodejs-oraclelinux9.sh from the repository root."
  exit 1
fi

echo "==> npm version"
npm --version

echo "==> Installing frontend dependencies"
npm install

echo "==> Building frontend"
npm run build

echo "==> Frontend build complete"
ls -la dist
