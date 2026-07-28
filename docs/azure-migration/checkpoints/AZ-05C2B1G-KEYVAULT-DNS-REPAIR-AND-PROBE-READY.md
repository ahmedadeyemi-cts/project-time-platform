# AZ-05C2B1G — Key Vault DNS Repair and Probe Ready

**Date:** 2026-07-12

## Current state

- Restore runner: running and billable
- Blob private DNS: repaired and validated
- Blob object listing: 15 objects
- Blob managed-identity authorization: passed
- Original restore Run Command: terminal `Failed`
- Original restore exit code: `6`
- Original failure stage: `retrieving-key-vault-secret`
- PostgreSQL contacted: no
- Target database modified: no
- Failure evidence uploaded: yes

## Prepared repair

`deployment/azure/scripts/az05c2b1f-repair-eastus-keyvault-private-dns-zone-group-and-link.sh`

The repair validates and, only when necessary, corrects:

- East Key Vault private endpoint state and approval;
- private endpoint DNS zone-group association to `privatelink.vaultcore.azure.net`;
- East VNet link to the Key Vault private DNS zone;
- Key Vault private DNS A record against the East private endpoint IP.

## Prepared validation

- `deployment/azure/scripts/az05c2b1g-guest-keyvault-access-probe.sh`
- `deployment/azure/scripts/az05c2b1g-submit-unique-keyvault-access-probe.sh`

The probe uses a unique Run Command name and validates:

- current DNS resolution;
- managed-identity token acquisition;
- HTTP 200 secret retrieval;
- non-empty secret value without printing the secret.

## Safety

The repair and probe do not modify PostgreSQL, storage data, role assignments, VM configuration, or Key Vault secret values.

## Next gate

A clean restore retry is permitted only after the unique Key Vault probe reports:

`KEYVAULT_ACCESS_PROBE_RESULT=SECRET_RETRIEVAL_SUCCEEDED`
