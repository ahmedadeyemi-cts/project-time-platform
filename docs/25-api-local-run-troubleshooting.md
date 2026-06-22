# API Local Run Troubleshooting

## Purpose

This document captures the local API run issue encountered during backend validation.

## Confirmed Working Items

The following completed successfully:

- .NET SDK 10.0.106 installed.
- API project restored.
- API project built successfully.
- API started successfully on `http://127.0.0.1:5080`.

## Issue Observed

A second SSH session showed:

```text
curl: (7) Failed to connect to 127.0.0.1 port 5080: Connection refused
```

This means no process was listening on port `5080` at the time of the curl test.

## Common Causes

1. The API process was stopped with `Ctrl+C`.
2. The SSH session running the API disconnected.
3. The API was started in the wrong working directory.
4. The API failed after startup.
5. The test was run before the API finished starting.

## Correct Manual Test Process

### SSH Session 1

Run the API from the project directory:

```bash
cd /opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api

set -a
source /opt/project-time-platform/config/postgres.env
set +a

dotnet run --urls http://127.0.0.1:5080
```

Leave this session open.

### SSH Session 2

Confirm the port is listening:

```bash
ss -ltnp | grep 5080 || true
```

Then test:

```bash
curl http://127.0.0.1:5080/health
curl http://127.0.0.1:5080/api/version
curl http://127.0.0.1:5080/api/db-config-check
curl http://127.0.0.1:5080/api/db-health
curl http://127.0.0.1:5080/api/schema/tables
```

## Temporary Background Run Option

For quick testing only:

```bash
cd /opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api

set -a
source /opt/project-time-platform/config/postgres.env
set +a

nohup dotnet run --urls http://127.0.0.1:5080 > /opt/project-time-platform/logs/projecttime-api-dev.log 2>&1 &
```

Check logs:

```bash
tail -f /opt/project-time-platform/logs/projecttime-api-dev.log
```

Stop the temporary background process:

```bash
pkill -f 'ProjectTime.Api'
```

## Security Notes

- Keep the API bound to `127.0.0.1` during this phase.
- Do not open port `5080` publicly.
- External access should wait until reverse proxy, TLS, and authentication are implemented.
