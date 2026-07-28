# AZ-05B2A PostgreSQL regional SKU discovery result

Date: 2026-07-11/12 UTC

## Outcome

No PostgreSQL server or database was created during AZ-05B2A. The operation was read-only.

The Azure CLI returned different capability shapes by region:

- West US 3 returned the normal `supportedServerEditions` and `supportedServerSkus` structure.
- East US returned PostgreSQL 16 availability through `supportedFastProvisioningEditions` instead of the normal SKU table.

## Confirmed common configuration

The following two-vCore General Purpose SKUs are represented in both regions for PostgreSQL 16:

- `Standard_D2s_v3`
- `Standard_D2ds_v4`

The migration standard selects:

- SKU: `Standard_D2ds_v4`
- Tier: General Purpose
- Memory: 8 GiB
- Storage: 128 GiB Premium SSD
- PostgreSQL: 16

## Why storage changed from 32 GiB to 128 GiB

East US advertised PostgreSQL 16 fast-provisioning compatibility for `Standard_D2ds_v4` with 128 GiB storage. The planned East US read replica is intended to support controlled promotion during a regional disaster. Keeping the primary and replica on the same tier and storage allocation avoids a promotion compatibility mismatch.

This replaces the earlier 32 GiB starting-storage plan. Storage autogrow remains enabled.

## HA interpretation

West US 3 advertised `ZoneRedundant` HA for `Standard_D2ds_v4`, so the primary will use zone-redundant HA.

Read replicas do not support HA while they remain replicas. East US HA will therefore be evaluated and enabled only after a controlled replica promotion or independent-server promotion during the DR procedure.

## Canonical next script

`deployment/azure/scripts/az05b3-postgresql-primary-explicit-sku.sh`

The script performs a live capability validation against both regional response shapes before creating any billable PostgreSQL resource.
