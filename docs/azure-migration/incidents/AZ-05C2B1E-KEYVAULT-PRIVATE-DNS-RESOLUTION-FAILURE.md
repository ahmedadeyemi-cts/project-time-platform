# AZ-05C2B1E — Key Vault Private DNS Resolution Failure

**Date:** 2026-07-12

## Summary

The first PostgreSQL restore attempt reached the Key Vault secret-retrieval stage after the Blob private DNS path was repaired, but the East US restore runner could not resolve:

`kv-phd-t-eus-7825cc.vault.azure.net`

The restore exited with code `6` at:

`FAILURE_STAGE=retrieving-key-vault-secret`

## Preserved evidence

- Restore managed Run Command: `phd-restore-postgresql13-seed`
- Execution state: `Failed`
- Exit code: `6`
- Source artifacts downloaded: `15`
- Downloaded bytes: `3,545,545`
- Source checksum verification: passed for all artifacts
- Dump bytes: `3,341,746`
- Result artifacts uploaded: `5`
- Result prefix: `restore-results/20260712T165858Z`
- Result upload exit code: `0`

## Impact

- PostgreSQL was not contacted.
- `pg_restore` did not start.
- The target database was not modified.
- Failure evidence was uploaded successfully to Blob Storage.

## Root cause

The East Key Vault private endpoint path was not resolvable from the East US VNet. The shared-services implementation accepted an existing DNS zone group without verifying that it referenced `privatelink.vaultcore.azure.net`, and it did not explicitly ensure an East VNet link to that zone.

## Corrective action

Run:

`deployment/azure/scripts/az05c2b1f-repair-eastus-keyvault-private-dns-zone-group-and-link.sh`

Then validate with the uniquely named, read-only probe:

- `deployment/azure/scripts/az05c2b1g-guest-keyvault-access-probe.sh`
- `deployment/azure/scripts/az05c2b1g-submit-unique-keyvault-access-probe.sh`

The probe must confirm DNS resolution, managed-identity token acquisition, HTTP 200 secret retrieval, and a non-empty secret value without printing the value.

## Retry rule

Do not update or reuse the failed restore Run Command. The clean retry must use a new Run Command name, new result prefix, and isolated state directory after Key Vault access is validated.
