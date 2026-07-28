# AZ-05C2B1C — Blob DNS Zone-Group Association Missing and Probe Output Stale

**Date:** 2026-07-12

## Findings

The first Blob DNS repair stopped safely after validating:

- East Blob private endpoint provisioning: `Succeeded`
- Private endpoint connection: `Approved`
- Private endpoint IP: `10.40.5.8`

It then found that the private endpoint DNS zone group did not reference:

`privatelink.blob.core.windows.net`

Therefore, creating only an East VNet link would not be sufficient. The private endpoint must first be associated with the correct private DNS zone through its DNS zone group.

## Stale probe result

The existing probe Run Command was updated and queried immediately, but Azure returned the prior instance-view output with timestamp `2026-07-12T17:13:47Z`.

The later command therefore did not provide a new DNS test result.

## Impact

- The original restore remains blocked in AzCopy scanning.
- No source files were downloaded.
- PostgreSQL restore has not started.
- The target database has not been modified by the restore attempt.

## Corrective actions

1. Run `az05c2b1c2-repair-eastus-blob-private-dns-zone-group-and-link.sh`.
2. The script repairs the private endpoint DNS zone-group association.
3. It then ensures the East VNet is linked to the Blob private DNS zone.
4. It validates that the storage A record contains `10.40.5.8`.
5. Run `az05c2b1d-submit-unique-blob-access-probe.sh` so every probe has a new managed Run Command name and cannot reuse prior instance-view output.

## Safety

The repair and probe do not modify PostgreSQL or storage data and do not resubmit the restore.
