# API Port 5080 Address In Use Troubleshooting

## Purpose

This document records the troubleshooting process when the local development API cannot start because port `5080` is already in use.

## Error

```text
Failed to bind to address http://127.0.0.1:5080: address already in use
```

## Meaning

Another process is already listening on port `5080`, or a previous `dotnet run` process is still active.

## Find the Process

Run:

```bash
ss -ltnp | grep 5080 || true
ps -ef | grep -E 'ProjectTime.Api|dotnet run|dotnet' | grep -v grep || true
```

## Test Current Listener

If something is listening on port `5080`, test the API before killing it:

```bash
curl http://127.0.0.1:5080/health
curl http://127.0.0.1:5080/api/db-health
curl http://127.0.0.1:5080/api/schema/tables
```

## Stop Existing Development API Process

For development only, stop the existing API process with:

```bash
pkill -f 'ProjectTime.Api' || true
pkill -f 'dotnet run' || true
```

Then confirm the port is free:

```bash
ss -ltnp | grep 5080 || true
```

## Start API Again

Run from the backend project directory:

```bash
cd /opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api

set -a
source /opt/project-time-platform/config/postgres.env
set +a

dotnet run --urls http://127.0.0.1:5080
```

## Temporary Background Run

For quick testing:

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

## Security Reminder

Keep the API bound to `127.0.0.1` during this phase. Do not open port `5080` publicly.
