# AZ-05C2A3: Legacy Compute Usage Quota Rows Missing

## Status

Resolved diagnostically on 2026-07-12 by replacing the legacy usage join with a direct Microsoft Quota REST preflight.

## What succeeded

The East US continuation and preflight confirmed:

- the existing East US NAT Gateway is attached to `snet-management`;
- the management subnet is `10.40.7.0/24` and is in `Succeeded` state;
- bidirectional West US 3 / East US global VNet peering is `Connected` and `FullyInSync`;
- the East US VNet link to `phd-test.postgres.database.azure.com` is `Completed`;
- the official Rocky Linux 10 image is available as `resf:rockylinux-x86_64:10-base:10.2.20260525`;
- several small x64 Generation 2 v7 VM SKUs are visible in East US.

## What failed

The original East US preflight joined VM SKU family names against `az vm list-usage` results. No matching quota rows were returned for the visible v7 families, so every candidate was reported as `quota-row-missing` and the script could not safely recommend a size.

## Impact

- No virtual machine was created.
- No East US NIC was created.
- No public IP was created.
- No additional billable compute started.
- The existing West US 3 private NIC remains unchanged.

## Root cause

`az vm list-usage` did not provide quota records whose resource names matched the family identifiers returned by `az vm list-skus` for the visible East US v7 candidates. The result was inconclusive rather than proof that quota was zero.

## Resolution

Added:

`deployment/azure/scripts/az05c2a4-eastus-quota-rest-preflight.sh`

The replacement preflight:

1. queries the Microsoft Quota REST API directly for quota limits;
2. queries the Microsoft Quota REST API directly for current usage;
3. follows paginated `nextLink` responses;
4. joins direct quota records to current East US SKU family identifiers;
5. recommends a small deployable VM when both family limit and usage provide enough remaining vCPUs;
6. otherwise reports the exact family and minimum quota required;
7. creates or changes no Azure resource.

## Required completion marker

`EASTUS DIRECT COMPUTE QUOTA PREFLIGHT COMPLETE`
