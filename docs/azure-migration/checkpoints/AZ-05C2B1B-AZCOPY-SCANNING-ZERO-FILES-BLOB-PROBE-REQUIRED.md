# AZ-05C2B1B — AzCopy Scanning With Zero Files; Blob Probe Required

**Date:** 2026-07-12

## Observed state

- Restore managed Run Command: `Running`
- Active process: AzCopy source-package download
- Source files downloaded: `0`
- Source bytes downloaded: `0`
- Restore result files: `0`
- AzCopy scanning log contains only startup metadata
- PostgreSQL restore has not started
- Target database has not been modified by this run

## Interpretation

The restore is blocked before checksum validation, Key Vault secret retrieval, target connection validation, and `pg_restore`. The next safe action is a read-only Blob access probe to separate:

1. DNS or network failure;
2. managed-identity token failure;
3. Blob data-plane authorization denial;
4. an AzCopy-specific listing or scanning problem.

## Canonical diagnostic scripts

- `deployment/azure/scripts/az05c2b1b-guest-blob-access-probe.sh`
- `deployment/azure/scripts/az05c2b1b-submit-blob-access-probe.sh`

## Safety constraints

- Do not rerun the restore submitter.
- Do not deallocate or stop the VM while the restore command remains active.
- Do not kill AzCopy until the direct REST and isolated AzCopy listing probe results are reviewed.
