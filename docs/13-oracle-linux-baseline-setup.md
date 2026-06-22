# Oracle Linux Baseline Setup Runbook

## 1. Purpose

This runbook documents the baseline setup process for the initial OCI development VM running Oracle Linux Server 9.7.

The end-state target remains Rocky Linux, but Oracle Linux is being used as a temporary OCI Free Tier development environment because Rocky Linux was not available in the OCI image selection flow.

## 2. Confirmed Server Baseline

| Item | Value |
|---|---|
| OS | Oracle Linux Server 9.7 |
| OS ID | ol |
| Platform | platform:el9 |
| Kernel | 6.12.0-203.76.7.1.el9uek.x86_64 |
| Architecture | x86_64 |
| Hostname | cts |
| SSH User | opc |
| Public IP | 167.234.223.32 |
| Private IP | 10.0.0.200 |
| CPU Count | 2 |
| Memory | Approximately 498 MiB |
| Swap | Approximately 497 MiB |
| Root Disk | 30 GB root filesystem, approximately 25 GB free during first validation |

## 3. First Validation Commands

Run:

```bash
cat /etc/os-release
whoami
uname -a
hostnamectl
ip addr show
ip route
free -h
df -h
nproc
```

## 4. Update System Packages

Run:

```bash
sudo dnf clean all
sudo dnf makecache
sudo dnf update -y
```

If the kernel or core system packages are updated, reboot:

```bash
sudo reboot
```

Reconnect after reboot:

```bash
ssh -i ~/.ssh/private_key.key opc@167.234.223.32
```

## 5. Install Baseline Tools

Run:

```bash
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
```

## 6. Package Install Troubleshooting

Observed validation after first update/reboot:

```text
git: command not found
podman: command not found
buildah: command not found
skopeo: command not found
jq-1.6
curl 7.76.1
```

Meaning: the system update completed, but the baseline development/container tools were not fully installed yet.

Run the following repeatable installation sequence:

```bash
sudo dnf repolist --enabled
sudo dnf install -y dnf-plugins-core
sudo dnf config-manager --set-enabled ol9_baseos_latest ol9_appstream ol9_addons || true
sudo dnf clean all
sudo dnf makecache
sudo dnf install -y git curl wget unzip tar vim nano jq firewalld podman buildah skopeo
```

Then validate:

```bash
git --version
podman --version
buildah --version
skopeo --version
jq --version
curl --version
```

## 7. Enable Firewall

Check current status:

```bash
sudo systemctl status firewalld --no-pager
```

Enable and start firewalld if it is not running:

```bash
sudo systemctl enable --now firewalld
```

Allow SSH:

```bash
sudo firewall-cmd --permanent --add-service=ssh
sudo firewall-cmd --reload
sudo firewall-cmd --list-all
```

Do not open PostgreSQL to the public internet.

## 8. Validate Installed Tools

Run:

```bash
git --version
podman --version
buildah --version
skopeo --version
jq --version
curl --version
```

## 9. Create Application Directory Structure

Run:

```bash
sudo mkdir -p /opt/project-time-platform/{app,config,data,logs,backups,scripts}
sudo chown -R opc:opc /opt/project-time-platform
ls -la /opt/project-time-platform
```

## 10. Clone Repository

Clone the GitHub repository after GitHub authentication method is confirmed.

Recommended location:

```bash
cd /opt/project-time-platform/app
git clone https://github.com/ahmedadeyemi-cts/project-time-platform.git
```

If the repository is private, a GitHub token or SSH deploy key will be required. Do not paste tokens or private keys into documentation or chat.

## 11. Resource Constraint Note

The current VM has approximately 498 MiB of memory. This is enough for early operating system validation and light setup, but it may be constrained for running PostgreSQL, .NET, Node.js builds, and containers at the same time.

The platform should still be documented and built carefully, but later development may require:

- More swap space.
- A larger free-tier shape if OCI capacity becomes available.
- Building frontend/backend artifacts elsewhere and deploying compiled output to this VM.
- Moving to Rocky Linux on a larger VM when available.

## 12. Next Setup Steps

After baseline tools are installed and validated, continue with:

1. PostgreSQL installation or container setup.
2. .NET SDK/runtime setup.
3. Node.js setup.
4. Backend API skeleton.
5. React frontend skeleton.
6. Podman container definitions.
7. Reverse proxy setup.
8. Microsoft Entra ID application registration.

## 13. Change Log

| Date | Change |
|---|---|
| 2026-06-21 | Created baseline setup runbook for OCI Oracle Linux 9.7 development VM |
| 2026-06-21 | Documented post-update validation and missing baseline packages |
