# AZ-05C2A Rocky Linux 10 Restore Runner — Ready to Deploy

Date: 2026-07-12

## Decision

The temporary private PostgreSQL restore runner will use Rocky Linux 10.x only.

- Official Azure Marketplace publisher: `resf`
- Architecture: x86-64
- Preferred release after update: Rocky Linux 10.2 or newer Rocky 10.x minor release
- Ubuntu and Rocky Linux 9 are not permitted fallbacks
- Current-generation Azure D-series VM sizes are required because Rocky Linux 10 uses the x86-64-v3 baseline

## Canonical deployment script

`deployment/azure/scripts/az05c2a-private-rocky10-restore-runner.sh`

The script dynamically discovers the latest matching `resf` Rocky Linux 10 image available in West US 3, records the exact image metadata, validates the operating system from `/etc/os-release`, applies Rocky 10 updates with `dnf`, and refuses to declare success unless the guest remains Rocky Linux 10.x.

## Security design

- No public IP
- Private NIC in `snet-management`
- Existing NAT Gateway used for fixed outbound access
- System-assigned managed identity
- `Storage Blob Data Reader` on the migration storage account
- `Key Vault Secrets User` on the West US 3 Key Vault
- Azure Run Command used instead of inbound SSH
- Private DNS and TCP 5432 validation against the PostgreSQL Flexible Server

## Source package

- Container: `database-exports`
- Prefix: `source-postgresql13/20260712T023119Z`
- Expected artifact count: 15
- PostgreSQL archive: `ProjectPulse-pg13-20260712T023119Z.dump`
- Verified archive size: 3,341,746 bytes

## Current state

No restore-runner VM had been created when this checkpoint was written. The next action is to execute the canonical AZ-05C2A script from Azure Cloud Shell.
