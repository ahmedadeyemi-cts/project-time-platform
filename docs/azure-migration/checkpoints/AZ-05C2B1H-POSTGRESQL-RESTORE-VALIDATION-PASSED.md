# AZ-05C2B1H — PostgreSQL Restore and Validation Passed

**Date:** 2026-07-12

## Result

The isolated clean retry completed successfully.

- Managed Run Command: `phdrestoreseedretry20260712t184644z`
- Result prefix: `restore-results/retry-20260712T184644Z`
- Guest state directory: `/var/lib/project-health-dashboard/az05c2b1h-20260712t184644z`
- Execution state: `Succeeded`
- Exit code: `0`
- Result upload exit code: `0`
- Evidence files uploaded: `12`
- Evidence transfer failures: `0`

## Validation

The comparison completed with:

- status: `PASSED`
- errors: `0`
- warnings: `0`
- schemas: `1`
- tables: `170`
- extensions: `2`
- sequences: `0`

The success marker was present:

`POSTGRESQL INITIAL SEED RESTORE RETRY VALIDATION PASSED`

## Notes

The `ANALYZE` operation emitted permission warnings for Azure-managed system catalogs such as `pg_authid` and `pg_database`. Those warnings did not affect the application-schema restore or validation. The application inventory and exact row-count comparison passed.

## Next action

Run `deployment/azure/scripts/az05c2b1i-finalize-successful-restore.sh` to:

1. verify the uploaded nonsecret evidence through the runner managed identity;
2. remove the temporary `Storage Blob Data Contributor` assignment;
3. submit VM deallocation.

The East US PostgreSQL replica remains deferred until successful evidence finalization and restore-runner deallocation are confirmed.
