#!/usr/bin/env bash
set -euo pipefail

# Project Time Platform
# Install Node.js and npm for the React frontend build on Oracle Linux 9.

echo "==> Checking enabled repositories"
sudo dnf repolist --enabled

echo "==> Installing Node.js and npm"
sudo dnf install -y nodejs npm

echo "==> Validating Node.js installation"
node --version
npm --version

echo "==> Node.js setup complete"
