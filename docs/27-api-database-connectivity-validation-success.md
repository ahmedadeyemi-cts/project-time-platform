# API Database Connectivity Validation Success

## Purpose

This document records successful end-to-end validation from the ASP.NET Core API to the local PostgreSQL database.

## Confirmed Listener

The API was confirmed listening locally on:

```text
127.0.0.1:5080
```

Process validation showed:

```text
ProjectTime.Api
```

running from:

```text
/opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api/bin/Debug/net10.0/ProjectTime.Api
```

## Confirmed API Health

Endpoint:

```text
GET /health
```

Successful result:

```text
status: healthy
service: Project Time Platform API
```

## Confirmed Database Health

Endpoint:

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

Endpoint:

```text
GET /api/schema/tables
```

Successful result:

```text
count: 20
```

The API returned the expected application tables:

```text
accounting_periods
accounting_reconciliations
app_users
approval_records
audit_logs
clients
notification_log
notification_preferences
project_assignments
project_tasks
projects
reporting_relationships
roles
schema_migrations
team_memberships
teams
time_entries
timesheets
user_roles
utilization_snapshots
```

## Current Status

The platform now has a validated backend-to-database connection on the OCI development VM.

## Security Notes

- API is still bound to localhost only.
- Port 5080 should not be opened publicly yet.
- PostgreSQL port 5432 should not be opened publicly.
- External access should wait for reverse proxy, TLS, and Microsoft Entra authentication.

## Next Step

Create a systemd service so the API can run reliably without requiring an active SSH session.
