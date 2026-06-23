# Open-Day Frontend Hard Fix

## Purpose

This document records a frontend hard fix for the case where the weekly timesheet header status is `submitted`, but only one day has actually been submitted.

## Date

2026-06-23

## Issue

The UI was still using whole-week editability in several places. After Sunday was submitted, the weekly header status caused the entire week to appear locked. As a result, the engineer could click Monday, Tuesday, or another open day, but could not enter time.

## Correct Behavior

- Submitted days are locked individually.
- Days that have not been submitted remain editable.
- Activity rows can still be added when at least one day is open.
- Save draft can still be used when at least one day is open.

## File Added

- `deployment/rocky-linux/apply-open-days-frontend-hard-fix.sh`

## What the Patch Does

The patch updates the React frontend to:

- Add `getDayStatus(workDate)`.
- Add `isDayEditable(workDate)`.
- Add `isAnyDayEditable`.
- Use day-level editability for time cell buttons.
- Use day-level editability for time-entry modal fields.
- Allow activity cards to be used when at least one day is still open.
- Display a message explaining that submitted days are locked individually.

## Validation Steps

1. Pull the latest repository changes.
2. Run the hard fix script.
3. Rebuild the frontend.
4. Restart the local frontend server.
5. Open the browser and hard refresh.
6. Confirm Sunday remains locked if already submitted.
7. Click Monday or Tuesday.
8. Confirm the modal opens and allows hours to be entered.
9. Confirm additional activity rows can be added for open days.

## Status

Ready for validation.
