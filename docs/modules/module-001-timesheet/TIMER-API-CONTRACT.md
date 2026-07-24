# Module 001 proposed timer API contract

This document defines behavior, not final route names.

## Read capabilities

- Get the authenticated user's active timer.
- Get current-week timer history.
- Get assignment-scoped Work Queue items.
- Validate a week for submission.

## Write capabilities

- Start a timer for an assignment or allowed non-project activity.
- Stop the authenticated user's active timer.
- Discard an unconverted timer with confirmation and audit evidence.
- Add a Work Queue assignment to the weekly draft.
- Update an unsubmitted draft task association.
- Submit a valid week to Module 002.

## Start request

Contains assignment/activity identifiers, time classification, optional description, and selected week/date context. The server determines the authenticated user and official UTC start timestamp.

## Stop response

Returns official start and stop timestamps, raw seconds, capped seconds, rounded minutes, auto-stop status, resulting draft entry identifiers, description completeness, and week-validation state.

## Concurrency

Starting a second timer returns conflict status and the currently running timer. Stop and discard operations use an optimistic version or equivalent concurrency token.

## Authorization integration

After RBAC PR #83 merges, runtime endpoints must evaluate the published scoped policy and preserve legacy fallback for cells still marked Not Set. View-As writes return 403.
