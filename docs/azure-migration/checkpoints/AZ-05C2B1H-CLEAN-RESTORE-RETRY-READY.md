# AZ-05C2B1H — Clean PostgreSQL Restore Retry Ready

**Date:** 2026-07-12

## Preconditions satisfied

- Rocky Linux 10 restore runner is prepared and running privately in East US.
- Blob access succeeds through private DNS and managed identity.
- Key Vault access succeeds through private DNS and managed identity.
- PostgreSQL private DNS and TCP 5432 were previously validated.
- The first restore attempt is terminal and no restore process remains active.
- The first attempt did not connect to or modify PostgreSQL.
- Temporary evidence-upload access already exists at the database export container scope.

## Canonical retry submitter

`deployment/azure/scripts/az05c2b1h-submit-clean-postgresql-restore-retry.sh`

The submitter creates:

- a unique managed Run Command name;
- an isolated guest state directory;
- a new guest log path;
- a new result prefix under `restore-results/retry-<timestamp>`;
- an injected private-DNS preflight for Blob, Key Vault, and PostgreSQL;
- a locally transformed and syntax-validated copy of the canonical guest restore script.

It does not update or reuse the failed Run Command.

## Status helper

`deployment/azure/scripts/az05c2b1h-check-clean-restore-retry.sh`

The helper reads the generated local state file and reports the new retry command state without printing the entire managed Run Command output.

## Next action

Submit the clean retry once. Do not resubmit after the `POSTGRESQL CLEAN RESTORE RETRY SUBMITTED` marker appears.
