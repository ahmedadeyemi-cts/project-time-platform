# Project Health Dashboard Disaster Recovery Restore Runbook

## Purpose

This runbook documents the controlled steps for validating and restoring Project Health Dashboard from a backup bundle.

## Current safe validation scope

The Restore Validation process does not restore over production. It validates:

1. Backup bundle exists.
2. Checksum file exists and matches.
3. Backup bundle can be opened.
4. PostgreSQL dump exists.
5. PostgreSQL dump can be inspected with pg_restore.
6. Configuration archive exists and can be opened.
7. Application snapshot archive exists and can be opened.
8. Runbook exists.

## Manual restore warning

Do not restore directly into the production Project Health Dashboard database without an approved maintenance window, stakeholder approval, and a verified rollback plan.

## High-level restore sequence

1. Stop Project Health Dashboard services on the target restore node.
2. Copy the selected backup bundle and checksum file to the target node.
3. Validate checksum.
4. Extract the backup bundle to a temporary restore directory.
5. Restore the PostgreSQL dump into a clean target database.
6. Restore required configuration files.
7. Restore or redeploy the application snapshot.
8. Restart PostgreSQL, API, frontend, and Nginx services.
9. Validate login, admin access, backup history, service control, and replication readiness.
10. Record restore validation outcome.

## Future enhancement

A non-production restore sandbox can later be added so Project Health Dashboard can perform automated test restores without touching production.
