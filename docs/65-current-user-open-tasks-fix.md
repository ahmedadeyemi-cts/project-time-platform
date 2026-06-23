# Current User Open Tasks Fix

## Purpose

This document records the fix for Open Tasks returning zero tasks after the PSA project assignments were seeded correctly.

## Date

2026-06-23

## Issue

The database contained seven assigned PSA project tasks for:

```text
ahmed.adeyemi@ussignal.com
```

However, the Open Tasks API still returned:

```json
{
  "count": 0,
  "tasks": []
}
```

The root cause is that the development API user helper was still resolving a different hard-coded development user. The seeded assignments belonged to `ahmed.adeyemi@ussignal.com`, but the API queried assignments for the older development identity.

## File Added

- `deployment/rocky-linux/apply-current-user-open-tasks-fix.sh`

## Behavior Added

The patch forces the development engineer identity used by the API to:

```text
Ahmed Adeyemi
ahmed.adeyemi@ussignal.com
```

This keeps the local development user aligned with the seeded PSA project assignments.

## Validation Steps

1. Pull the latest repo.
2. Apply the current-user Open Tasks fix.
3. Redeploy the API.
4. Confirm API version `0.4.4`.
5. Validate Open Tasks for week `2026-06-21`.
6. Restart the frontend server.
7. Refresh the browser.
8. Select `Activity Type → Open tasks`.
9. Confirm the seven PSA project tasks appear.

## Status

Ready for validation.
