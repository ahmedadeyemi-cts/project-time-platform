# AZ-05C3B4 — Azure Support Discovery Refresh Ready

**Date:** 2026-07-12

## Observed state

- current subscription matches `cd32baeb-7b71-4bc0-8ea3-9f23a50903fe`
- `Microsoft.Support` provider is `Registered`
- Azure CLI `support` extension is installed at version `2.0.1`
- the existing `/tmp/azure-support-services.json` file is non-empty but invalid JSON
- no service-candidate TSV or problem-classification files were produced

## Root cause

The first `az support services list` invocation occurred while the support extension was being dynamically installed. The redirected output file was contaminated by non-JSON command output, causing the subsequent Python JSON parser to fail.

## Recovery

Canonical script:

`deployment/azure/scripts/az05c3b4-refresh-azure-support-discovery.sh`

The script:

1. validates the active subscription;
2. confirms `Microsoft.Support` registration;
3. confirms the installed support extension;
4. deletes stale temporary discovery files;
5. downloads the service catalog into a new temporary file;
6. separates stderr from JSON stdout;
7. validates JSON before replacing the canonical temporary file;
8. selects relevant support services;
9. downloads and validates problem classifications atomically;
10. prints the discovered IDs without creating a ticket.

## Safety

- read-only Azure discovery
- no support ticket created
- no PostgreSQL replica created
- no new billable resource created
