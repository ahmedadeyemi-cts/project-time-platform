# PostgreSQL Application Database Validation Success

## Purpose

This document records the successful application database setup on the OCI Oracle Linux development VM.

## Confirmed Setup

| Item | Value |
|---|---|
| PostgreSQL Version | 13.23 |
| Database Name | project_time_platform |
| Application User | ptp_app |
| Host | localhost |
| Port | 5432 |
| Local Config File | /opt/project-time-platform/config/postgres.env |
| Config File Permissions | 600 |

## Validation Result

The application database login test completed successfully and returned the expected database and user:

```text
current_database: project_time_platform
current_user: ptp_app
```

## Authentication Fix Applied

The local PostgreSQL host-based authentication file was updated so localhost TCP connections use password-based authentication instead of ident authentication.

Updated file:

```text
/var/lib/pgsql/data/pg_hba.conf
```

The relevant localhost rules now use:

```text
md5
```

PostgreSQL was reloaded after the change.

## Security Notes

- Keep the local database config file outside the Git repository.
- Do not print or commit database credentials.
- Do not open PostgreSQL port 5432 publicly.
- Keep PostgreSQL local-only for this development phase.

## Next Step

Create and run the initial schema migration for the core platform tables.
