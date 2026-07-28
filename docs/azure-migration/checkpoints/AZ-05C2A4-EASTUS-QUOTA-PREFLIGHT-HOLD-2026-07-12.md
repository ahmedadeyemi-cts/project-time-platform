# AZ-05C2A4 East US Quota Preflight Hold — 2026-07-12

## Status

Paused for the day after completing the Microsoft.Quota registration and direct East US quota preflight.

## Completed

- `Microsoft.Quota` resource provider registered successfully.
- East US network prerequisites remain ready:
  - management subnet `snet-management` attached to existing NAT Gateway `nat-phd-test-aca-eastus`;
  - East US and West US 3 VNet peerings connected and fully synchronized;
  - East US PostgreSQL private DNS link completed;
  - official RESF Rocky Linux 10 image available.
- Direct quota preflight completed without creating or changing Azure resources.

## Direct quota result

The Microsoft Quota API returned zero quota records and zero usage records for the East US compute scope. Therefore no family limit could be confirmed programmatically.

The current conservative decision is:

- `QUOTA_DECISION=QUOTA_REQUEST_REQUIRED`
- requested candidate size: `Standard_D2alds_v7`
- requested family: `StandardDaldsv7Family`
- minimum requested family limit: `2` vCPUs

This result reflects missing quota records, not a verified zero-core quota for the selected family.

## Current Azure state

- No East US restore-runner VM exists.
- No new East US NIC or public IP was created.
- West US 3 temporary restore-runner VM does not exist.
- Existing West US 3 private NIC `nic-phd-test-db-migrate-w3` remains available.
- No billable VM compute is running for the migration runner.

## Resume point

Tomorrow, continue with one of these controlled options:

1. Request or confirm a 2-vCPU quota for `StandardDaldsv7Family` in East US, then deploy `Standard_D2alds_v7`; or
2. query the Azure portal quota blade to identify an already-approved small x64 family and use that exact family/size.

Do not rerun the West US 3 FX4mds deployment script.
