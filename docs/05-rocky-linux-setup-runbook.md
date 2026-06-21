# Rocky Linux Setup Runbook

## 1. Purpose

This runbook documents the repeatable process for preparing a Rocky Linux server for the Project Time Platform.

## 2. Target Use

This runbook will be used for:

- Oracle Free Tier test server setup
- Future staging server setup
- Future production Rocky Linux deployment

## 3. Server Assumptions

| Item | Value |
|---|---|
| OS | Rocky Linux |
| Access | SSH |
| Runtime | Podman |
| Database | PostgreSQL |
| Reverse Proxy | Caddy or NGINX |
| Application | .NET backend and React frontend |

## 4. Server Preparation Checklist

1. Confirm server hostname.
2. Confirm public/private IP address.
3. Confirm SSH access.
4. Confirm firewall rules.
5. Confirm DNS records.
6. Confirm ports 80 and 443 requirements.
7. Confirm outbound internet access for updates.
8. Confirm backup location.

## 5. Baseline Package Installation

Commands will be added once the target Rocky Linux version is confirmed.

Expected baseline packages:

- git
- podman
- buildah
- skopeo
- firewalld
- curl
- wget
- unzip
- vim or nano
- postgresql client tools
- caddy or nginx

## 6. Security Baseline

The server should use:

- SSH key-based access.
- No shared administrator password.
- Firewall enabled.
- Only required ports open.
- Least-privilege service accounts.
- Documented sudo access.
- Regular patch schedule.

## 7. Application Directory Standard

Recommended directory structure:

```text
/opt/project-time-platform/
├── app/
├── config/
├── data/
├── logs/
├── backups/
└── scripts/
```

## 8. Firewall Requirements

| Port | Purpose |
|---|---|
| 22 | SSH, restricted to admin IPs if possible |
| 80 | HTTP challenge/redirect |
| 443 | HTTPS application access |

Database ports should not be publicly exposed.

## 9. Next Steps

This document will be updated with exact commands once the first Rocky Linux test server is available.
