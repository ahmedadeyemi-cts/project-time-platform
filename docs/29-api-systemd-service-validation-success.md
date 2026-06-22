# API systemd Service Validation Success

## Purpose

This document records the successful validation of the Project Time Platform API running as a systemd service.

## Service Name

```text
projecttime-api
```

## Confirmed Service Status

The service was installed, enabled, and started successfully.

Confirmed status:

```text
Active: active (running)
```

Systemd unit path:

```text
/etc/systemd/system/projecttime-api.service
```

Published API path:

```text
/opt/project-time-platform/app/published/api
```

## Confirmed Runtime

The service is running through:

```text
/usr/bin/dotnet
```

Application DLL:

```text
/opt/project-time-platform/app/published/api/ProjectTime.Api.dll
```

## Confirmed Listener

The API is listening on localhost only:

```text
127.0.0.1:5080
```

This is the correct state for the current phase.

## Confirmed API Endpoints

The following endpoint returned healthy status:

```text
GET /health
```

Successful result:

```text
status: healthy
service: Project Time Platform API
```

## Confirmed Database Connectivity

The following endpoint confirmed database connectivity:

```text
GET /api/db-health
```

Successful result:

```text
status: database_connected
database: project_time_platform
user: ptp_app
```

## Confirmed Schema Lookup

The following endpoint confirmed the API can query the database schema:

```text
GET /api/schema/tables
```

Successful result:

```text
count: 20
```

## Service Management Commands

Check status:

```bash
sudo systemctl status projecttime-api --no-pager
```

View logs:

```bash
sudo journalctl -u projecttime-api -n 100 --no-pager
```

Restart service:

```bash
sudo systemctl restart projecttime-api
```

Stop service:

```bash
sudo systemctl stop projecttime-api
```

Start service:

```bash
sudo systemctl start projecttime-api
```

## Security Notes

- The API is still bound to `127.0.0.1` only.
- Do not open port `5080` publicly.
- Do not open PostgreSQL port `5432` publicly.
- External user access should wait for reverse proxy, TLS, and Microsoft Entra authentication.

## Current Platform Milestone

The development VM now has:

- Oracle Linux 9.7 configured.
- GitHub private repo cloned.
- PostgreSQL 13.23 running.
- Initial schema applied.
- ASP.NET Core API built and published.
- API running as a systemd service.
- API-to-database connectivity validated.

## Recommended Next Step

Create the frontend skeleton and keep it internal/local first, or create the reverse proxy foundation while still avoiding public exposure of unauthenticated API endpoints.
