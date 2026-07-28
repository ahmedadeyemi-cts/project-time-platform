# AZ-05C3A — East US Replica SKU Preflight Exact-Match Result Inconclusive

**Date:** 2026-07-12

## Summary

The read-only East US PostgreSQL replica preflight stopped before creating any replica because the exact scalar `Standard_D2ds_v4` was not found in the current `az postgres flexible-server list-skus --location eastus` response.

## Safety outcome

- No East US PostgreSQL replica was created.
- No PostgreSQL server, database, subnet, DNS resource, secret, role assignment, or VM was modified.
- The imported West US 3 primary remained `Ready`.
- The migration VM remained deallocated.

## Why the result is inconclusive

The preflight used a case-sensitive recursive scalar comparison. Azure PostgreSQL capability responses can vary by region and may represent a SKU through different structures or normalized names, including:

- `supportedFastProvisioningEditions`
- `supportedServerEditions` / `supportedServerSkus`
- `D2ds_v4` versus `Standard_D2ds_v4`
- case differences

The failed exact comparison therefore does not by itself prove that the expected replica configuration is unavailable.

## Corrective action

Run the read-only schema-aware diagnostic:

`deployment/azure/scripts/az05c3a1-eastus-postgresql-replica-sku-diagnostic.sh`

The diagnostic reports:

- current response shape;
- normalized occurrences of the expected SKU;
- fast-provisioning and standard-edition matches;
- current D2-family candidates;
- tier, version, storage, status, vCore, and HA metadata when present.

## Decision gate

Do not create the billable East US replica until the diagnostic confirms a currently advertised, compatible configuration or provides enough evidence to select a documented alternative.
