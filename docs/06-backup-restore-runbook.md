# Backup and Restore Runbook

## 1. Purpose

This runbook defines how backups and restores will be handled for the platform.

A backup is not considered valid until it has been restored and tested in a non-production environment.

## 2. Backup Scope

Backups must include:

- PostgreSQL database
- Application configuration
- Environment files
- Reverse proxy configuration
- Podman/systemd service files
- Deployment scripts
- SSL/certificate configuration if applicable
- Documentation updates

## 3. Database Backup Strategy

Planned backup types:

| Backup Type | Frequency | Purpose |
|---|---|---|
| Daily logical backup | Daily | Standard recovery |
| Pre-upgrade backup | Before every upgrade | Rollback protection |
| Weekly full backup | Weekly | Longer retention |
| Quarterly restore test | Quarterly | Backup validation |

## 4. Restore Strategy

Restores must be tested in staging before being trusted.

Standard restore process:

1. Select backup file.
2. Prepare clean staging database.
3. Restore database backup.
4. Apply any required migrations.
5. Start application in staging.
6. Validate login.
7. Validate time entry.
8. Validate manager approval.
9. Validate PM approval.
10. Validate accounting reconciliation.
11. Validate reports.
12. Document restore result.

## 5. Pre-Upgrade Backup Requirement

Before any OS, database, framework, or application upgrade:

- Take a database backup.
- Capture current application version.
- Capture current container image versions.
- Capture current OS version.
- Capture current PostgreSQL version.
- Confirm rollback plan.

## 6. Backup Storage

Backup location must be documented before production use.

Open decision:

- Local backup only
- Network backup
- Object storage
- GitHub release artifact for non-sensitive deployment scripts only

Sensitive backups must not be committed to GitHub.

## 7. Restore Test Log

| Date | Backup Used | Environment | Result | Notes |
|---|---|---|---|---|
| TBD | TBD | TBD | TBD | TBD |
