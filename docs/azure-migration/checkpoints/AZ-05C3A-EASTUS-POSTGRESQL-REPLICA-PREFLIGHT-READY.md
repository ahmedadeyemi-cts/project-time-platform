# AZ-05C3A — East US PostgreSQL Replica Preflight Ready

**Date:** 2026-07-12

## Prerequisite completed

The verified PostgreSQL 13 to Azure PostgreSQL 16 initial-seed restore is complete, its evidence is preserved, the temporary result-upload role was removed, and the migration VM is deallocated.

## Planned billable resource

- Resource type: Azure Database for PostgreSQL Flexible Server read replica
- Name: `pg-phd-test-eus-7825cc`
- Region: `eastus`
- Source: `pg-phd-test-w3-7825cc`
- Compute tier: `GeneralPurpose`
- SKU: `Standard_D2ds_v4`
- Storage: `128 GiB`
- Networking: private access through the East US delegated PostgreSQL subnet
- Private DNS zone: `phd-test.postgres.database.azure.com`

Replica creation is intentionally not included in the preflight because it creates a continuously billable database server.

## Read-only preflight

Canonical script:

`deployment/azure/scripts/az05c3a-postgresql-eastus-replica-preflight.sh`

The script validates:

1. all required resource groups;
2. primary server state, version, tier, SKU, storage, networking, and imported database;
3. existing replica topology;
4. absence of the planned East US replica;
5. East US delegated PostgreSQL subnet;
6. private DNS zone and completed East VNet link;
7. current East US SKU metadata;
8. migration VM deallocation;
9. nonsecret state needed by the later creation workflow.

## Safety

The preflight is read-only and prints:

- `READ_ONLY_PREFLIGHT=true`
- `BILLABLE_REPLICA_CREATED=false`
- `REPLICA_CREATION_DECISION=READY_NOT_CREATED`

No PostgreSQL replica, VM, role assignment, DNS record, or network resource is created or modified.
