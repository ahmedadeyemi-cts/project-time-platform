# PostgreSQL Installation Validation

## Purpose

This document records the successful PostgreSQL installation and validation on the OCI Oracle Linux development VM.

## Repository Clone Status

The private GitHub repository was successfully cloned to:

```text
/opt/project-time-platform/app/project-time-platform
```

Git validation showed:

```text
On branch main
Your branch is up to date with 'origin/main'.
nothing to commit, working tree clean
```

## PostgreSQL Package Installation

PostgreSQL packages were installed successfully using DNF.

Installed packages included:

```text
postgresql-13.23-2.el9_7.x86_64
postgresql-contrib-13.23-2.el9_7.x86_64
postgresql-server-13.23-2.el9_7.x86_64
postgresql-private-libs-13.23-2.el9_7.x86_64
```

## PostgreSQL Initialization

PostgreSQL was initialized with:

```bash
sudo postgresql-setup --initdb
```

Successful output:

```text
Initializing database in '/var/lib/pgsql/data'
Initialized, logs are in /var/lib/pgsql/initdb_postgresql.log
```

## PostgreSQL Service Status

PostgreSQL was enabled and started with:

```bash
sudo systemctl enable --now postgresql
sudo systemctl status postgresql --no-pager
```

Service status showed:

```text
Active: active (running)
```

## PostgreSQL Version

Validation command:

```bash
sudo -u postgres psql -c "SELECT version();"
```

Confirmed version:

```text
PostgreSQL 13.23 on x86_64-redhat-linux-gnu
```

## Current Decision

PostgreSQL is running natively on the VM for the initial low-memory development environment.

## Next Step

Create the application database and application database user:

```text
Database: project_time_platform
User: ptp_app
Host: localhost
Port: 5432
```

Database credentials must be stored only in:

```text
/opt/project-time-platform/config/postgres.env
```

Do not commit this credentials file to GitHub.
