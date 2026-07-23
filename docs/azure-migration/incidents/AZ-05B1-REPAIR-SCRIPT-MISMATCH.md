# AZ-05B1 Repair-Script Mismatch

Date: 2026-07-11

## Summary

The first AZ-05B PostgreSQL deployment stopped before creating a server because the installed Azure CLI rejected `--database-name` during normal Flexible Server creation. A follow-up patch script attempted to modify the local failed script but stopped with `The original SKU assignment block was not found`.

## Impact

- No PostgreSQL server was created.
- No database was created.
- No network or DNS resource was changed.
- The generated PostgreSQL administrator password remained safely stored in both regional Key Vaults.

## Cause

The repair script depended on an exact multi-line text match for the SKU assignment block. The local script formatting did not exactly match the expected block, so the patch intentionally failed before writing changes.

## Resolution

Do not patch either failed local script. Use the clean standalone canonical deployment:

`deployment/azure/scripts/az05b2-postgresql-primary-clean.sh`

The replacement:

- Recursively discovers common SKU strings from both regional `list-skus` responses.
- Recognizes both `Standard_D2...` and `D2...` formats.
- Preserves and checks the SKU-discovery exit code.
- Refuses to continue with an empty SKU.
- Creates the Flexible Server without `--database-name`.
- Creates the application database after the server reaches Ready.
- Reuses the password already stored in Key Vault.

## Current checkpoint

No PostgreSQL resource exists until AZ-05B2 completes successfully.
