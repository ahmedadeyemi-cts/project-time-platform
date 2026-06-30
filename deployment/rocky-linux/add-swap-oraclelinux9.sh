#!/usr/bin/env bash
set -euo pipefail

# Project Time Platform
# Add swap file for low-memory Oracle Linux 9 OCI development VM.

SWAPFILE="/swapfile"
SWAPSIZE="2G"

echo "==> Current memory and swap"
free -h
swapon --show || true

if sudo swapon --show | awk '{print $1}' | grep -qx "$SWAPFILE"; then
  echo "==> $SWAPFILE is already active. No changes made."
  exit 0
fi

if [ -f "$SWAPFILE" ]; then
  echo "==> $SWAPFILE already exists but is not active. Reusing it."
else
  echo "==> Creating $SWAPSIZE swap file at $SWAPFILE"
  sudo fallocate -l "$SWAPSIZE" "$SWAPFILE" || sudo dd if=/dev/zero of="$SWAPFILE" bs=1M count=2048 status=progress
fi

echo "==> Securing swap file permissions"
sudo chmod 600 "$SWAPFILE"

echo "==> Formatting swap file"
sudo mkswap "$SWAPFILE"

echo "==> Activating swap file"
sudo swapon "$SWAPFILE"

if ! grep -q '^/swapfile none swap sw 0 0' /etc/fstab; then
  echo "==> Adding swap file to /etc/fstab"
  echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab > /dev/null
else
  echo "==> /etc/fstab already contains swapfile entry"
fi

echo "==> Updated memory and swap"
free -h
swapon --show

echo "==> Swap setup complete"
