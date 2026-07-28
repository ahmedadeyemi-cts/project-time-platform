# AZ-05C3A1 — East US Replica SKU Diagnostic Ready

**Date:** 2026-07-12

## Current state

- West US 3 PostgreSQL primary: `Ready`
- Imported database: `project_health_dashboard`
- Imported schema/table validation: passed
- Existing replicas: `0`
- Planned East US replica: absent
- East PostgreSQL subnet delegation: confirmed
- PostgreSQL private DNS East VNet link: confirmed
- Migration VM: deallocated
- Billable East US replica: not created

## Preflight issue

The first replica preflight stopped at a case-sensitive exact-SKU search. The result is treated as inconclusive because East US PostgreSQL capability metadata may use region-specific structures, casing, or a shortened SKU name.

## Diagnostic prepared

Canonical read-only diagnostic:

`deployment/azure/scripts/az05c3a1-eastus-postgresql-replica-sku-diagnostic.sh`

Expected decision markers:

- `CURRENT_EASTUS_SKU_DECISION=EXPECTED_CONFIGURATION_ADVERTISED`
- or `CURRENT_EASTUS_SKU_DECISION=EXPECTED_CONFIGURATION_NOT_ADVERTISED`

The script does not create or modify Azure resources.

## Next action

Run the diagnostic and review its normalized SKU, tier, PostgreSQL version, storage, and availability evidence before preparing the billable replica creation command.
