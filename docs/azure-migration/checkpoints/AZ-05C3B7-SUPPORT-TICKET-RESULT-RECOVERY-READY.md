# AZ-05C3B7 — Azure Support Ticket Result Recovery Ready

**Date:** 2026-07-12

## Observed behavior

The guarded Azure Support ticket creation workflow validated the intended subscription, registered `Microsoft.Support`, and confirmed the exact PostgreSQL Flexible Server quota problem classification.

The Azure CLI create operation then returned control without emitting a JSON response body. The wrapper subsequently stopped with:

`Support ticket create command returned no JSON result.`

Because the Azure CLI command itself did not trigger `set -e`, the create command appears to have returned exit code 0. The ticket creation outcome is therefore unknown and must be queried before any retry.

## Duplicate-prevention decision

Do not rerun the ticket creation script until the exact ticket name is queried:

`phd-postgresql-eastus-access-20260712t212351z`

A duplicate support request must not be created while the first operation may already have succeeded.

## Recovery workflow

Canonical script:

`deployment/azure/scripts/az05c3b7-recover-eastus-postgresql-support-ticket-result.sh`

The script performs read-only Azure Support queries:

1. query the exact ticket name;
2. if needed, search tickets created after the original submission time;
3. validate an exact name match;
4. reconstruct the missing nonsecret state file when found;
5. report an inconclusive result without permitting a create retry when not found.

## Current state

- support ticket creation: unknown
- ticket state file: missing
- duplicate retry: blocked
- PostgreSQL East US replica: not created
- new replica billing: not started
