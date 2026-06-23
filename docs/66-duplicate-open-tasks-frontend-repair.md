# Duplicate Open Tasks Frontend Repair

## Purpose

This document records a repair for a frontend build failure caused by duplicate Open Tasks declarations in `App.jsx`.

## Date

2026-06-23

## Issue

The Open Tasks patch was applied more than once locally, which created duplicate declarations:

```text
const assignedOpenTasks = openTasks.data?.tasks ?? [];
const assignedOpenTasks = openTasks.data?.tasks ?? [];
```

Vite/esbuild failed because the same symbol was declared twice in the same scope.

## File Added

- `deployment/rocky-linux/repair-duplicate-open-tasks-declarations.sh`

## Behavior

The script removes repeated consecutive duplicate `assignedOpenTasks` declarations while leaving the valid declarations intact.

## Validation Steps

1. Pull the latest repository changes.
2. Run the repair script.
3. Rebuild the frontend.
4. Restart the frontend server.
5. Confirm the build completes successfully.
6. Refresh the browser.

## Status

Ready for validation.
