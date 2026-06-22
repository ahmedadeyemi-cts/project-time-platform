# API systemd Service Setup

## Purpose

This runbook documents how to run the Project Time Platform API as a systemd service on the OCI Oracle Linux development VM.

## Goal

The API should:

- Start automatically after reboot.
- Restart if it crashes.
- Run without an active SSH session.
- Stay bound to localhost only.
- Load database credentials from the protected local environment file.

## Files

Systemd unit template:

```text
deployment/rocky-linux/projecttime-api.service
```

Installer script:

```text
deployment/rocky-linux/install-api-systemd-service.sh
```

Published application location:

```text
/opt/project-time-platform/app/published/api
```

Local environment file:

```text
/opt/project-time-platform/config/postgres.env
```

## Service Name

```text
projecttime-api
```

## Apply Steps

Pull the latest repository changes:

```bash
cd /opt/project-time-platform/app/project-time-platform

git restore deployment/rocky-linux/apply-initial-schema.sh || true

GIT_SSH_COMMAND='ssh -i ~/.ssh/github_project_time_platform -o IdentitiesOnly=yes' \
git pull
```

Run the installer:

```bash
chmod +x deployment/rocky-linux/install-api-systemd-service.sh
./deployment/rocky-linux/install-api-systemd-service.sh
```

## Validate Service

Check status:

```bash
sudo systemctl status projecttime-api --no-pager
```

Check logs:

```bash
sudo journalctl -u projecttime-api -n 100 --no-pager
```

Check listener:

```bash
ss -ltnp | grep 5080 || true
```

Check endpoints:

```bash
curl http://127.0.0.1:5080/health
curl http://127.0.0.1:5080/api/db-health
curl http://127.0.0.1:5080/api/schema/tables
```

## Service Management

Restart API:

```bash
sudo systemctl restart projecttime-api
```

Stop API:

```bash
sudo systemctl stop projecttime-api
```

Start API:

```bash
sudo systemctl start projecttime-api
```

Disable API startup at boot:

```bash
sudo systemctl disable projecttime-api
```

## Security Notes

- The API remains bound to `127.0.0.1:5080`.
- Do not open port `5080` publicly.
- Do not open PostgreSQL port `5432` publicly.
- External access should wait for reverse proxy, TLS, and Microsoft Entra authentication.
