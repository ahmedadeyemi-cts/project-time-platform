# Upgrade Runbook

## 1. Purpose

This runbook defines how OS, database, framework, application, and dependency upgrades will be performed safely.

## 2. Upgrade Principle

No upgrade goes directly to production.

Every upgrade must be tested in staging using a production-like backup before production is changed.

## 3. Upgrade Types

| Upgrade Type | Examples |
|---|---|
| OS Upgrade | Rocky Linux package updates or major version upgrade |
| Database Upgrade | PostgreSQL minor or major upgrade |
| Application Upgrade | New backend/frontend release |
| Framework Upgrade | .NET, Node.js, React dependency updates |
| Infrastructure Upgrade | Podman, Caddy/NGINX, firewall, systemd changes |

## 4. Standard Upgrade Process

1. Document current production versions.
2. Review release notes for the target upgrade.
3. Take pre-upgrade backups.
4. Restore production backup into staging.
5. Apply upgrade in staging.
6. Run database migrations if required.
7. Run automated regression tests.
8. Run manual workflow tests.
9. Validate reports.
10. Validate notifications.
11. Validate authentication.
12. Document results.
13. Approve production upgrade.
14. Apply upgrade to production during a maintenance window.
15. Run post-upgrade validation.
16. Document final result.

## 5. Required Regression Validation

The following must work after every major upgrade:

- Microsoft Entra login
- Role-based access
- Engineer time entry
- Manager approval
- Manager decline
- Project Manager approval
- Project Manager decline
- Accounting reconciliation
- Period locking
- Utilization calculation
- Notification generation
- Reports
- Audit logging

## 6. Rollback Requirement

Every upgrade must identify the rollback path before it begins.

Rollback options may include:

- Restore previous container image.
- Restore database backup.
- Revert migration if safe.
- Restore previous configuration files.
- Restore VM snapshot, if available.

## 7. Upgrade Log

| Date | Component | From Version | To Version | Environment | Result | Notes |
|---|---|---|---|---|---|---|
| TBD | TBD | TBD | TBD | TBD | TBD | TBD |
