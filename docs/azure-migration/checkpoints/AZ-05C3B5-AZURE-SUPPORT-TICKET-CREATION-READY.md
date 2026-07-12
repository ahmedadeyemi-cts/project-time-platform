# AZ-05C3B5 — Azure Support Ticket Creation Ready

**Date:** 2026-07-12

## Discovery result

The refreshed Azure support discovery completed successfully:

- current subscription matched;
- `Microsoft.Support` provider is registered;
- Azure CLI support extension version `2.0.1` is installed;
- support service catalog is valid with 356 services;
- 5 candidate services were identified;
- 5 valid problem-classification files were retrieved;
- 249 total classifications were inspected.

## Selected route

Service:

- `Service and subscription limits (quotas)`
- service name: `06bfd9d3-516b-d5c6-5802-169c800dec89`

Problem classification:

- `Azure Database for PostgreSQL Flexible Server`
- classification name: `af87bb6b-2275-4355-9dde-dff5f7eec887`

Full classification ID:

`/providers/Microsoft.Support/services/06bfd9d3-516b-d5c6-5802-169c800dec89/problemClassifications/af87bb6b-2275-4355-9dde-dff5f7eec887`

## Guarded creation

Canonical script:

`deployment/azure/scripts/az05c3b5-create-eastus-postgresql-support-ticket.sh`

The script requires:

`PHD_CREATE_SUPPORT_TICKET=YES`

It creates an Azure support request only. It does not create or modify PostgreSQL, network, compute, storage, DNS, or application resources.

## Status check

Canonical script:

`deployment/azure/scripts/az05c3b6-check-eastus-postgresql-support-ticket.sh`

## Current state

- East US PostgreSQL replica: not created
- replica billing: not started
- support discovery: passed
- support ticket creation: ready, not yet submitted
