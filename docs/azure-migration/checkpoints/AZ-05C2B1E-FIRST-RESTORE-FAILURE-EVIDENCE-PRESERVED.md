# AZ-05C2B1E — First Restore Failure Evidence Preserved

**Date:** 2026-07-12

## Final result

The first PostgreSQL restore attempt failed before contacting PostgreSQL because the East US restore runner could not resolve the East Key Vault hostname.

## Evidence

- Managed Run Command: `phd-restore-postgresql13-seed`
- Execution state: `Failed`
- Exit code: `6`
- Failure stage: `retrieving-key-vault-secret`
- Source files downloaded: `15`
- Source bytes: `3,545,545`
- Dump bytes: `3,341,746`
- All source checksums: passed
- Result files uploaded: `5`
- Result bytes: `97,579`
- Result prefix: `restore-results/20260712T165858Z`
- Result upload exit code: `0`

## Database safety conclusion

No `psql` or `pg_restore` process started. The target Azure PostgreSQL database was not modified by this attempt.

## Next action

Repair and validate the East Key Vault private DNS path before submitting a clean restore retry with a new Run Command name, isolated state directory, and new result prefix.
