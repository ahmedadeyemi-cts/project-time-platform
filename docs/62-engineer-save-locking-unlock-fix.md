# Engineer Save, Locking, and Unlock Fix

## Purpose

This document records the fix for engineer save behavior, approved-time locking, and missing unlock visibility.

## Date

2026-06-23

## Issue

After manager approval and Approval Inbox updates, engineer save behavior could fail because the weekly timesheet shell was no longer purely `draft`. The draft-save endpoint still treated the entire week as editable or non-editable, while the business rule is day-level locking.

The engineer-side modal also needed clearer status behavior:

- Submitted days should show `Unlock this day`.
- Manager-approved days should be read-only.
- Project-manager-approved/accounting/reconciled/locked days should be read-only.
- Manager-declined days should be editable for correction and resubmission.

## Files Added

- `deployment/rocky-linux/apply-engineer-save-lock-unlock-fix.sh`

## Behavior Added

### Draft Save

Draft save now preserves protected days instead of replacing the entire week.

Protected day statuses:

- `submitted`
- `manager_approved`
- `pm_approved`
- `accounting_ready`
- `reconciled`
- `locked`

Editable day statuses:

- `draft`
- `manager_declined`

### Engineer Locking

Engineers cannot edit approved time after it moves forward in workflow.

### Unlock Visibility

Submitted day cells remain clickable so the modal can show `Unlock this day`. The fields remain locked until the day is unlocked.

### Read-Only Status

Approved or downstream workflow days show a read-only indicator instead of showing `Submit this day`.

## Validation Steps

1. Apply this patch script.
2. Redeploy the API.
3. Rebuild the frontend.
4. Submit a day as engineer.
5. Confirm the submitted day shows `Unlock this day`.
6. Approve the day as manager.
7. Confirm the engineer can no longer edit that day.
8. Confirm open or manager-declined days can still be edited and saved.
9. Confirm approved days are preserved when saving open days in the same week.

## Status

Ready for validation.
