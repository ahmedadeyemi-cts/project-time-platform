# AZ-05C2B1 — PostgreSQL Initial-Seed Restore Running

**Date:** 2026-07-12

## Submission

- Managed Run Command: `phd-restore-postgresql13-seed`
- Result prefix: `restore-results/20260712T165858Z`
- Restore VM: `vm-phd-test-db-migrate-eus`
- Target database: `project_health_dashboard`
- Target server: `pg-phd-test-w3-7825cc.postgres.database.azure.com`

## Current state

- Run Command execution state: `Running`
- Run Command provisioning state: `Succeeded`
- Displayed exit code: `0` while still running; not final
- PostgreSQL client installed: 16.14
- pg_restore installed: 16.14
- AzCopy installed: 10.32.4
- Temporary `Storage Blob Data Contributor` role created at the `database-exports` container scope

## Interpretation

The restore workflow is active and has completed tool installation successfully. It has not yet reached a terminal restore-validation marker. The submitter must not be rerun.

## Required completion condition

Success requires all of the following:

- execution state `Succeeded`;
- final exit code `0`;
- output contains `POSTGRESQL INITIAL SEED RESTORE VALIDATION PASSED`;
- nonsecret evidence is present under `database-exports/restore-results/20260712T165858Z`.

## Next action

Recheck the managed Run Command instance view until execution reaches a terminal state. On success, verify uploaded evidence, remove the temporary contributor role, and deallocate the VM immediately.
