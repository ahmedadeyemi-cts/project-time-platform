# Module 001 conceptual timer persistence design

No migration number is assigned during Phase 0.

## Proposed timer session

A durable session should store identifiers for user, week, customer, project, task/work item, assignment, activity type, time classification, UTC start/stop/effective-stop timestamps, raw seconds, rounded minutes, description, status, auto-stop flag, resulting Timesheet entry, audit actor, and created/updated timestamps.

## Suggested statuses

`RUNNING`, `STOPPED_DRAFT`, `AUTO_STOPPED`, `DISCARDED`, and `CONVERTED_TO_ENTRY`.

## Required invariants

- Partial unique constraint for one RUNNING timer per user.
- Rounded minutes divisible by 15 and no greater than 720.
- Effective stop is not earlier than start.
- Immutable timer lifecycle audit evidence.
- No deletion after conversion to a Timesheet entry.
- Existing Timesheet records and schemas remain intact.

## Midnight and week boundaries

Preserve one timer-session audit record. Allocate resulting draft segments by the user's local calendar date. Apply ceiling rounding once to the total session, then allocate rounded minutes across segments without repeated inflation.

## RBAC dependency

Select the final schema and migration only after migration 040 is merged and the authoritative scoped evaluator contract is known.
