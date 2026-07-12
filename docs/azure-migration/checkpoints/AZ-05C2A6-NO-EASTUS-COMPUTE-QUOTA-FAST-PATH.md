# AZ-05C2A6: No East US Compute Quota - Fast Path

## Status

Blocked on Azure compute quota as of 2026-07-12.

## Confirmed result

The read-only East US quota scan returned:

`NO_EASTUS_COMPUTE_QUOTA_WITH_AT_LEAST_2_FREE_CORES`

Therefore the subscription currently cannot allocate a two-vCPU virtual machine in East US.

## Impact

- No East US restore-runner VM can be created until quota is approved.
- Azure cannot begin VM operating-system installation and apply quota retroactively because quota is validated before compute allocation.
- No VM or other billable compute resource was created by the quota scan.
- Existing East US networking prerequisites remain ready: management subnet, NAT Gateway attachment, VNet peering, and PostgreSQL private DNS link.

## Fast path

Use the Azure portal Quotas service:

1. Select Compute.
2. Filter subscription `Azure subscription 1` and region `East US`.
3. Search for `Daldsv7`.
4. Request a limit of 2 vCPUs for the matching Standard Daldsv7 family.
5. If the quota is not adjustable, open the support request offered by the portal.

Azure documentation states that an approved VM-family quota request automatically increases the regional vCPU quota when needed.

## Parallel work while approval is pending

- Finish the East US Rocky Linux 10 VM deployment script.
- Finish PostgreSQL restore and validation automation.
- Prepare role assignments, cleanup, deallocation, and evidence upload steps.
- Do not create a public IP or weaken private networking to bypass quota.

## Required resume condition

The East US VM-family quota must show at least 2 approved vCPUs before creating `Standard_D2alds_v7`.
