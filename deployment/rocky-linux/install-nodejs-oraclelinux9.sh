#!/usr/bin/env bash
set -euo pipefail

# Project Time Platform
# Install Node.js and npm for the React frontend build on Oracle Linux 9.
# Vite requires Node.js 18 or higher. Prefer Node.js 20 when available.

MIN_NODE_MAJOR=18
PREFERRED_NODE_STREAM="20"
FALLBACK_NODE_STREAM="18"

echo "==> Checking enabled repositories"
sudo dnf repolist --enabled

echo "==> Available Node.js module streams"
sudo dnf module list nodejs || true

echo "==> Resetting Node.js module stream"
sudo dnf module reset -y nodejs || true

echo "==> Enabling Node.js $PREFERRED_NODE_STREAM stream when available"
if sudo dnf module enable -y "nodejs:$PREFERRED_NODE_STREAM"; then
  echo "==> Enabled nodejs:$PREFERRED_NODE_STREAM"
else
  echo "WARNING: nodejs:$PREFERRED_NODE_STREAM was not available. Trying nodejs:$FALLBACK_NODE_STREAM."
  sudo dnf module reset -y nodejs || true
  sudo dnf module enable -y "nodejs:$FALLBACK_NODE_STREAM"
fi

echo "==> Installing Node.js and npm"
sudo dnf install -y nodejs npm

echo "==> Validating Node.js installation"
node --version
npm --version

NODE_MAJOR=$(node -p "parseInt(process.versions.node.split('.')[0], 10)")
if [ "$NODE_MAJOR" -lt "$MIN_NODE_MAJOR" ]; then
  echo "ERROR: Node.js $MIN_NODE_MAJOR or higher is required, but found $(node --version)."
  echo "Run: sudo dnf module list nodejs"
  exit 1
fi

echo "==> Node.js setup complete"
