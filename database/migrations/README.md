# Database Migrations

This folder contains forward database migration scripts.

## Rules

- Every schema change must be versioned.
- Every migration should be reviewed before production use.
- Every migration should be tested in staging first.
- A rollback script should be added to `database/rollback/` when possible.

## Naming Convention

```text
001_create_identity_tables.sql
002_create_project_tables.sql
003_create_time_entry_tables.sql
```
