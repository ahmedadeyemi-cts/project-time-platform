# AZ-05C2A2: FX4mds Family Quota Is Zero in West US 3

## Status

Resolved by pivoting to a read-only East US restore-runner preflight on 2026-07-12.

## What happened

The West US 3 restore-runner deployment successfully completed these preflight checks:

- confirmed `Standard_FX4mds` was the only unrestricted compatible x64 SKU returned by SKU discovery;
- confirmed 4 vCPUs, 84 GiB RAM, x64 architecture, and Hyper-V generation V2;
- queried the current retail compute price;
- selected the official RESF Rocky Linux 10 image `resf:rockylinux-x86_64:10-base:10.2.20260525`;
- confirmed the existing private NIC without a public IP.

Azure Resource Manager then rejected VM creation during preflight validation because the subscription's `standardFXMDVSFamily` quota in West US 3 was zero cores.

## Azure response

- Region: `WestUS3`
- VM family quota: `standardFXMDVSFamily`
- Current limit: `0`
- Current usage: `0`
- Additional cores required: `4`
- Minimum required new limit: `4`

The subsequent Azure CLI `The content for this response was already consumed` traceback was a secondary CLI error while formatting the ARM quota response. The authoritative failure was `QuotaExceeded`.

## Impact

- No VM was created.
- No FX4mds compute charge started.
- No public IP was created.
- The existing private NIC `nic-phd-test-db-migrate-w3` remains at `10.30.7.4` and can be deleted later if the West US 3 runner is abandoned.
- The PostgreSQL dump and migration inventory remain safe in Blob Storage.

## Decision

Do not request quota for an oversized 84-GiB temporary VM unless no practical alternative exists.

Use the existing East US network foundation as the next candidate because:

- East US and West US 3 VNets are globally peered;
- the PostgreSQL private DNS zone is already linked to East US;
- East US has its own management subnet and NAT Gateway;
- a small temporary East US Rocky Linux 10 VM can connect privately to the West US 3 PostgreSQL server through global VNet peering, subject to NSG and quota validation.

## Resolution artifact

`deployment/azure/scripts/az05c2a3-eastus-rocky10-restore-runner-preflight.sh`

The script is read-only and validates:

- East US management subnet and NAT Gateway;
- bidirectional global VNet peering;
- PostgreSQL private DNS link;
- official RESF Rocky Linux 10 image availability;
- unrestricted x64 Gen2 D/E/F SKUs;
- exact VM-family quota remaining;
- a recommended small VM size with sufficient quota.

## Next step

Run the East US preflight. If no suitable East US family quota exists, request the minimum practical VM-family quota increase or use a private containerized restore job instead.
