# AZ-05C3B1 — East US Location Restriction Diagnostic Ready

**Date:** 2026-07-12 UTC

## Current state

The billable East US PostgreSQL replica request was rejected before resource creation with:

```text
The location is restricted from performing this operation.
```

No replica exists, no successful-submission state was written, and no replica billing began.

## Diagnostic prepared

Canonical script:

`deployment/azure/scripts/az05c3b1-diagnose-eastus-postgresql-location-restriction.sh`

The script is read-only and records:

- whether the planned replica exists;
- whether a replica-submission state file exists;
- current East US PostgreSQL capability metadata;
- root-level region restriction, status, and reason fields;
- exact fast-provisioning compatibility count;
- failed PostgreSQL activity-log evidence when available;
- a nonsecret local diagnostic state file.

## Decision gate

Replica creation must not be retried until one of these occurs:

1. Microsoft enables East US PostgreSQL deployment access for this subscription; or
2. architecture formally approves another supported and unrestricted secondary region.

The West US 3 primary remains Ready and the imported database remains intact.
