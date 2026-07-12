# AZ-05C3B — East US PostgreSQL Location Restricted

**Date:** 2026-07-12 UTC

## Summary

The guarded East US PostgreSQL read-replica creation command was executed after the corrected read-only preflight passed.

Azure rejected the create operation with:

```text
ERROR: The location is restricted from performing this operation.
```

## Confirmed state

- source primary: `pg-phd-test-w3-7825cc`
- planned replica: `pg-phd-test-eus-7825cc`
- target region: `eastus`
- planned SKU: `Standard_D2ds_v4`
- tier: `GeneralPurpose`
- PostgreSQL version: `16`
- storage: `128 GiB`
- live fast-provisioning capability match: `1`
- current replica count before submission: `0`
- replica resource created: **no**
- replica submission state file written: **no**
- new replica billing started: **no**

## Interpretation

The East US service capability metadata advertises the requested SKU/tier/version/storage combination, but the subscription or service control plane denied placement in East US.

SKU capability and subscription placement authorization are separate gates. A successful SKU capability check does not override a region restriction applied to the subscription or service deployment operation.

## Safety outcome

The creation script uses `set -Eeuo pipefail` and writes its submission state only after Azure accepts the request. Because Azure rejected the operation, the script stopped before writing a false submitted state.

The status checker then correctly reported the submission state file as missing.

## Next action

Run the read-only diagnostic:

`deployment/azure/scripts/az05c3b1-diagnose-eastus-postgresql-location-restriction.sh`

The diagnostic captures:

1. East US capability-root `restricted`, `status`, and `reason` fields;
2. the exact compatible fast-provisioning match count;
3. failed PostgreSQL control-plane activity-log evidence when available;
4. confirmation that no replica or submission state exists;
5. a nonsecret diagnostic state file for escalation.

Do not rerun replica creation until the restriction is resolved or a different secondary region is formally approved.
