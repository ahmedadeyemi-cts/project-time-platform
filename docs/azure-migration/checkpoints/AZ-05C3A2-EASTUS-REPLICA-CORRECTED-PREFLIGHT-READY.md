# AZ-05C3A2 — Corrected East US PostgreSQL Replica Preflight Ready

**Date:** 2026-07-12

## Confirmed diagnostic result

The read-only East US PostgreSQL SKU diagnostic confirmed:

- expected SKU: `Standard_D2ds_v4`
- expected tier: `GeneralPurpose`
- PostgreSQL version: `16`
- storage: `128 GiB`
- fast-provisioning compatibility matches: `1`
- current decision: `EXPECTED_CONFIGURATION_ADVERTISED`
- diagnostic result: `PASSED`

No Azure resource was created or modified by the diagnostic.

## Root cause of the earlier preflight stop

The original AZ-05C3A preflight used a generic exact scalar search for `Standard_D2ds_v4`. East US returns the relevant configuration under `supportedFastProvisioningEditions`, and its current scalar value is lowercase (`standard_d2ds_v4`).

The earlier stop was therefore a preflight implementation defect, not an East US capacity failure.

## Corrected preflight

Canonical script:

`deployment/azure/scripts/az05c3a2-postgresql-eastus-replica-preflight-corrected.sh`

It revalidates:

1. imported West US 3 PostgreSQL primary state and configuration;
2. imported database presence;
3. current replica count and planned-name absence;
4. East US delegated PostgreSQL subnet;
5. shared PostgreSQL private DNS zone and East VNet link;
6. schema-aware East US fast-provisioning compatibility;
7. migration VM deallocated or deleted;
8. nonsecret preflight state for the guarded creation step.

## Billable creation script staged but not executed

Canonical script:

`deployment/azure/scripts/az05c3b-submit-postgresql-eastus-replica.sh`

The script requires the explicit environment gate:

`PHD_CREATE_BILLABLE_REPLICA=YES`

It also revalidates topology and live East US compatibility immediately before submission.

## Current decision

- corrected preflight: ready to execute
- East US replica: not created
- billable replica action: blocked until corrected preflight passes
