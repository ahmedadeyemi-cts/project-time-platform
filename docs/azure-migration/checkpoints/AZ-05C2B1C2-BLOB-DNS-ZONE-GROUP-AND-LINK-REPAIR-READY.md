# AZ-05C2B1C2 — Corrected Blob DNS Repair Ready

**Date:** 2026-07-12

## Current state

- Restore runner: running
- Restore command: running
- Active blocker: Blob hostname cannot resolve
- East Blob private endpoint: `10.40.5.8`
- Private endpoint state: `Succeeded`
- Private endpoint connection: `Approved`
- DNS zone-group association: missing
- East VNet Blob private DNS link: not yet validated
- PostgreSQL restore started: no

## Corrected repair

Canonical script:

`deployment/azure/scripts/az05c2b1c2-repair-eastus-blob-private-dns-zone-group-and-link.sh`

The script:

1. repairs the private endpoint DNS zone-group association;
2. ensures the East VNet link exists;
3. validates the storage A record against `10.40.5.8`;
4. makes no PostgreSQL or storage-data changes.

## Fresh probe

After the repair, use:

`deployment/azure/scripts/az05c2b1d-submit-unique-blob-access-probe.sh`

Each execution creates a uniquely named managed Run Command so prior instance-view output cannot be mistaken for a new probe result.
