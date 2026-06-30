#!/usr/bin/env bash
set -euo pipefail

# Project Time Platform
# Oracle Linux 9 baseline tool installation script
# End-state target remains Rocky Linux, but this supports the OCI Oracle Linux development VM.

echo "==> Project Time Platform baseline setup for Oracle Linux 9"

echo "==> Confirming OS"
cat /etc/os-release

echo "==> Cleaning and refreshing DNF metadata"
sudo dnf clean all
sudo dnf makecache

echo "==> Installing DNF plugins"
sudo dnf install -y dnf-plugins-core

echo "==> Enabling common Oracle Linux 9 repositories if available"
sudo dnf config-manager --set-enabled ol9_baseos_latest ol9_appstream ol9_addons || true

echo "==> Refreshing metadata after repository check"
sudo dnf clean all
sudo dnf makecache

echo "==> Installing baseline packages"
sudo dnf install -y \
  git \
  curl \
  wget \
  unzip \
  tar \
  vim \
  nano \
  jq \
  firewalld \
  podman \
  buildah \
  skopeo

echo "==> Enabling firewalld"
sudo systemctl enable --now firewalld
sudo firewall-cmd --permanent --add-service=ssh
sudo firewall-cmd --reload

echo "==> Creating application directory structure"
sudo mkdir -p /opt/project-time-platform/{app,config,data,logs,backups,scripts}
sudo chown -R opc:opc /opt/project-time-platform

echo "==> Validation"
git --version
podman --version
buildah --version
skopeo --version
jq --version
curl --version
sudo firewall-cmd --list-all
ls -la /opt/project-time-platform

echo "==> Baseline setup complete"
