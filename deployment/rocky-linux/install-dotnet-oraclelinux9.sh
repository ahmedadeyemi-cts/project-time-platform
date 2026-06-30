#!/usr/bin/env bash
set -euo pipefail

# Project Time Platform
# Install .NET SDK for Oracle Linux 9 development VM.

DOTNET_PACKAGE="dotnet-sdk-10.0"

echo "==> Checking enabled repositories"
sudo dnf repolist --enabled

echo "==> Installing $DOTNET_PACKAGE"
if sudo dnf install -y "$DOTNET_PACKAGE"; then
  echo "==> .NET SDK installed using DNF package: $DOTNET_PACKAGE"
else
  echo "ERROR: Failed to install $DOTNET_PACKAGE from enabled repositories."
  echo "Available dotnet SDK packages:"
  sudo dnf search dotnet-sdk || true
  sudo dnf list available 'dotnet-sdk*' || true
  exit 1
fi

echo "==> Validating .NET installation"
dotnet --info
dotnet --list-sdks
dotnet --list-runtimes

echo "==> .NET SDK setup complete"
