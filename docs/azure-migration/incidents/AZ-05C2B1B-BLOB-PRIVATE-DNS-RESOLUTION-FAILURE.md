# AZ-05C2B1B — Blob Private DNS Resolution Failure

**Date:** 2026-07-12

## Summary

The East US Rocky Linux 10 restore runner could not resolve:

`stphdtest7825cc.blob.core.windows.net`

The read-only Blob access probe returned:

- managed-identity storage token: success
- DNS result: empty
- curl error: `Could not resolve host`
- probe exit code: 6

## Impact

- The PostgreSQL restore managed Run Command remained in `Running` state.
- AzCopy remained active in its scanning stage.
- No source files were downloaded.
- No PostgreSQL restore began.
- The target Azure PostgreSQL database was not modified by this attempt.

## Root cause

The Blob private endpoint and private DNS zone group existed, but the canonical storage foundation did not ensure that the `privatelink.blob.core.windows.net` private DNS zone was linked to the East US VNet.

The East US restore runner therefore could not resolve the storage account FQDN to the East US Blob private endpoint IP.

## Corrective action

Run:

`deployment/azure/scripts/az05c2b1c-repair-eastus-blob-private-dns-link.sh`

The repair is idempotent and:

1. validates the East Blob private endpoint and approved connection;
2. validates its DNS zone group;
3. creates the East VNet link only when absent;
4. validates the storage A record against the private endpoint IP;
5. does not modify VM, database, storage data, or RBAC.

## Follow-up

After DNS repair:

1. rerun the read-only Blob access probe;
2. observe whether the existing AzCopy process resumes;
3. only if the original process remains stuck after DNS works, stop that restore attempt and submit a corrected retry;
4. preserve all logs and deallocate the VM promptly after validation or a controlled stop.
