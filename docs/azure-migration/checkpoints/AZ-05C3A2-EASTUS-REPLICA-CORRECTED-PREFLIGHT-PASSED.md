# AZ-05C3A2 — Corrected East US PostgreSQL Replica Preflight Passed

**Date:** 2026-07-12

## Outcome

The corrected read-only East US PostgreSQL replica preflight passed all gates.

## Confirmed primary state

- primary server: `pg-phd-test-w3-7825cc`
- state: `Ready`
- PostgreSQL version: `16`
- SKU: `Standard_D2ds_v4`
- tier: `GeneralPurpose`
- storage: `128 GiB`
- public network access: `Disabled`
- replication role: `Primary`
- imported database: `project_health_dashboard`

## Confirmed topology

- existing replica count: `0`
- planned replica exists: `false`
- planned replica: `pg-phd-test-eus-7825cc`
- planned region: `eastus`

## Confirmed East US networking

- delegated PostgreSQL subnet: confirmed
- subnet private endpoint policies: disabled
- PostgreSQL private DNS East VNet link: confirmed

## Confirmed East US capability

- SKU: `Standard_D2ds_v4`
- tier: `GeneralPurpose`
- PostgreSQL version: `16`
- storage: `128 GiB`
- fast-provisioning match count: `1`
- SKU preflight: passed

## Cost cleanup

The temporary migration VM remains deallocated.

## Decision

- corrected preflight: passed
- replica creation decision: `READY_NOT_CREATED`
- billable East US replica: not yet created

## Next scripts

Creation:

`deployment/azure/scripts/az05c3b-submit-postgresql-eastus-replica.sh`

The creation script requires:

`PHD_CREATE_BILLABLE_REPLICA=YES`

Status:

`deployment/azure/scripts/az05c3c-check-postgresql-eastus-replica.sh`
