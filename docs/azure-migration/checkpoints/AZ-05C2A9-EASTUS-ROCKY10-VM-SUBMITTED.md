# AZ-05C2A9 — East US Rocky Linux 10 Restore Runner Submitted

**Date:** 2026-07-12

## Result

The temporary East US database migration VM deployment was submitted successfully.

- Resource group: `rg-project-health-dashboard-test-migration-eastus`
- VM: `vm-phd-test-db-migrate-eus`
- NIC: `nic-phd-test-db-migrate-eus`
- Region: `eastus`
- Size: `Standard_D2alds_v7`
- Image: `resf:rockylinux-x86_64:10-base:10.2.20260525`
- Private IP: `10.40.7.4`
- Public IP: none
- Initial provisioning state: `Creating`
- Initial power state: `VM running`

## Preconditions confirmed

- Microsoft.Compute: Registered
- Microsoft.Network: Registered
- Daldsv7 family quota: 4 total, 0 used before deployment
- Total Regional vCPUs: 4 total, 0 used before deployment
- East US management subnet: ready
- NAT gateway: attached
- Global VNet peering: connected
- PostgreSQL private DNS link: completed

## Billing posture

Compute billing began once Azure allocated and started the VM. The VM is temporary and tagged for deletion after migration validation.

## Next action

After provisioning reaches `Succeeded`:

1. Assign the VM system-managed identity `Storage Blob Data Reader` on the source export storage account.
2. Assign `Key Vault Secrets User` on the East US Key Vault.
3. Submit asynchronous Rocky Linux preparation and private-connectivity validation.
4. Continue directly to PostgreSQL restore validation or deallocate promptly if validation is blocked.
