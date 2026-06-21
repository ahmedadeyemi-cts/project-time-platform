# Database Rollback Scripts

This folder contains rollback scripts for database migrations when rollback is safe and practical.

## Rules

- Rollback scripts must be tested in staging.
- Destructive rollbacks must be clearly marked.
- Production rollback should require approval.
- Backups must be taken before rollback execution.
