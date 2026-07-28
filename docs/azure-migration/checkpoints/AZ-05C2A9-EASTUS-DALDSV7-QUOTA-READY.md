# AZ-05C2A9 — East US Daldsv7 Quota Ready

Date: 2026-07-12

## Confirmed state

The subscription now exposes the exact East US quota row required by the selected temporary restore runner:

- Quota display name: `Standard Daldsv7 Family vCPUs`
- Exact quota resource name: `StandardDaldsv7Family`
- Current usage: `0`
- Current limit: `4`
- Quota applicable: `true`
- Required restore-runner size: `Standard_D2alds_v7`
- Required vCPUs: `2`

This clears the VM-family quota blocker for the East US Rocky Linux 10 restore runner.

## Deployment decision

Proceed with an asynchronous deployment so Azure continues provisioning independently of the browser-hosted Cloud Shell session.

Deployment target:

- Resource group: `rg-project-health-dashboard-test-migration-eastus`
- VM: `vm-phd-test-db-migrate-eus`
- NIC: `nic-phd-test-db-migrate-eus`
- Region: `eastus`
- Size: `Standard_D2alds_v7`
- Image: `resf:rockylinux-x86_64:10-base:10.2.20260525`
- Public IP: none
- Subnet: `vnet-phd-test-eastus/snet-management`
- Outbound path: `nat-phd-test-aca-eastus`

The deployment script validates regional and family quotas, selected SKU availability, the Rocky Linux image, NAT attachment, bidirectional VNet peering, and the PostgreSQL private DNS link before creating resources.

## Billing statement

The migration resource group and NIC do not incur VM compute charges. VM billing begins when Azure successfully allocates and starts the temporary restore runner. The VM must be deallocated promptly after validation and deleted after migration evidence is retained.
