# AZ-05C2A5: East US Daldsv7 Quota Request

## Purpose

Request the minimum VM-family quota needed to create the temporary Rocky Linux 10 PostgreSQL restore runner in East US.

## Target

- Region: `eastus`
- VM size: `Standard_D2alds_v7`
- VM family quota name: `StandardDaldsv7Family`
- Requested quota: `2` dedicated vCPUs
- Scope: `/subscriptions/<subscription-id>/providers/Microsoft.Compute/locations/eastus`

## Why this family

The East US preflight identified `Standard_D2alds_v7` as a small x64 Generation 2 VM compatible with the Rocky Linux 10 x86-64-v3 baseline. The request is intentionally limited to the two vCPUs required by one temporary restore runner.

## Script

`deployment/azure/scripts/az05c2a5-request-eastus-daldsv7-quota.sh`

The script:

1. Confirms `Microsoft.Quota` is registered.
2. Installs or updates the Azure CLI `quota` extension.
3. Checks whether the quota already exists.
4. Submits a two-vCPU quota request when necessary.
5. Polls the family quota for up to twenty minutes.
6. Creates no VM and no billable Azure resource.

## Success markers

Approved:

`EASTUS DALDSV7 COMPUTE QUOTA READY`

Submitted but still pending or requiring manual review:

`EASTUS DALDSV7 QUOTA REQUEST SUBMITTED`

## Next step after approval

Create the temporary private Rocky Linux 10 restore runner in East US, assign managed-identity access to the PostgreSQL export and Key Vault secret, restore and validate the PostgreSQL database, and then delete the temporary compute resources.