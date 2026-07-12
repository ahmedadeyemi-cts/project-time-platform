# AZ-05C2B1F — Key Vault DNS Repair Prepared

**Date:** 2026-07-12

The Blob path is healthy, but the first restore attempt failed at Key Vault DNS resolution. An idempotent repair and unique validation probe have been committed.

Prepared repair:

`deployment/azure/scripts/az05c2b1f-repair-eastus-keyvault-private-dns-zone-group-and-link.sh`

Prepared probe:

- `deployment/azure/scripts/az05c2b1g-guest-keyvault-access-probe.sh`
- `deployment/azure/scripts/az05c2b1g-submit-unique-keyvault-access-probe.sh`

The retry gate remains closed until the probe confirms successful secret retrieval without exposing the secret value.
