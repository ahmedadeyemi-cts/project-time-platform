# AZ-05C2B0 — East US Rocky Linux 10 Restore Runner Prepared

**Date:** 2026-07-12

## Result

The temporary East US restore runner completed guest preparation successfully.

## VM

- Resource group: `rg-project-health-dashboard-test-migration-eastus`
- VM: `vm-phd-test-db-migrate-eus`
- NIC: `nic-phd-test-db-migrate-eus`
- Private IP: `10.40.7.4`
- Public IP: none
- Size: `Standard_D2alds_v7`
- Operating system: Rocky Linux 10.2 x86-64
- Kernel: `6.12.0-211.16.1.el10_2.0.1.x86_64`
- Power state at validation: running

## Managed identity

- Principal ID: `9e0a22d9-1331-487e-a6b8-dc8513f0e461`
- Storage role: `Storage Blob Data Reader`
- Key Vault role: `Key Vault Secrets User`

## Validation

- PostgreSQL private FQDN resolved to `10.30.4.5`.
- TCP 5432 connectivity succeeded.
- Managed identity storage token acquisition succeeded.
- Managed identity Key Vault token acquisition succeeded.
- Outbound HTTPS through the East US NAT gateway succeeded.
- Managed Run Command `phd-prepare-rocky10` completed with exit code 0.
- Final marker: `PRIVATE ROCKY 10 RESTORE RUNNER PREPARATION READY`.

## Cost state

The temporary VM is running and compute billing is active. Proceed directly to restore and validation or deallocate the VM if execution is paused.

## Next step

Submit the PostgreSQL initial-seed restore and validation managed Run Command, preserve nonsecret results in Blob Storage, remove temporary result-upload permission, and deallocate the VM immediately after completion.
