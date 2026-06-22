# Initial Schema Migration

## Purpose

This document explains how to apply the first database schema migration for the Project Time Platform.

## Migration File

```text
database/migrations/001_initial_schema.sql
```

## Rollback File

```text
database/rollback/001_initial_schema_rollback.sql
```

## Apply Script

```text
deployment/rocky-linux/apply-initial-schema.sh
```

## Tables Created

The initial migration creates the following core tables:

- schema_migrations
- app_users
- roles
- user_roles
- teams
- team_memberships
- reporting_relationships
- clients
- projects
- project_tasks
- project_assignments
- accounting_periods
- timesheets
- time_entries
- approval_records
- accounting_reconciliations
- utilization_snapshots
- notification_preferences
- notification_log
- audit_logs

## Seed Data

The migration seeds system roles:

- Engineer
- Team Lead
- Manager
- Project Manager
- Accounting
- Organizational Admin
- System Admin
- Super Admin

## Apply Steps

From the VM, pull the latest repo changes:

```bash
cd /opt/project-time-platform/app/project-time-platform
GIT_SSH_COMMAND='ssh -i ~/.ssh/github_project_time_platform -o IdentitiesOnly=yes' git pull
```

Apply the migration:

```bash
chmod +x deployment/rocky-linux/apply-initial-schema.sh
./deployment/rocky-linux/apply-initial-schema.sh
```

## Manual Apply Option

If the script is not used:

```bash
source /opt/project-time-platform/config/postgres.env

PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -v ON_ERROR_STOP=1 \
  -f database/migrations/001_initial_schema.sql
```

## Validation Commands

```bash
source /opt/project-time-platform/config/postgres.env

PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT migration_id, description, applied_at FROM schema_migrations ORDER BY applied_at;"

PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT table_name FROM information_schema.tables WHERE table_schema='public' ORDER BY table_name;"
```

## Rollback Warning

The rollback file drops the initial schema tables and will remove data. Use it only in development or after a backup.
