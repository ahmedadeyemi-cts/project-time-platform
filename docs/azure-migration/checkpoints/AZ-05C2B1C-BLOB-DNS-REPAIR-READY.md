# AZ-05C2B1C — East US Blob DNS Repair Ready

**Date:** 2026-07-12

## Current state

- Restore runner: `vm-phd-test-db-migrate-eus`
- Private IP: `10.40.7.4`
- Restore managed Run Command: `Running`
- Active process: AzCopy scanning source prefix
- Source files downloaded: `0`
- PostgreSQL restore started: no
- Managed-identity storage token: success
- Blob DNS resolution: failed

## Repair prepared

Canonical repair script:

`deployment/azure/scripts/az05c2b1c-repair-eastus-blob-private-dns-link.sh`

The script ensures the East US VNet is linked to:

`privatelink.blob.core.windows.net`

It also validates that the storage A record contains the East Blob private endpoint IP.

## Safety

The repair does not:

- stop or restart the restore runner;
- modify PostgreSQL;
- alter storage data;
- change RBAC;
- recreate the private endpoint;
- create a billable resource.

## Next action

Run the DNS-link repair and then rerun the read-only Blob access probe. Do not resubmit the PostgreSQL restore until the existing attempt is evaluated after DNS resolution is restored.
