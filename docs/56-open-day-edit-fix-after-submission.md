# Open-Day Edit Fix After Daily Submission

## Purpose

This document records the correction for editing open days after one day has already been submitted.

## Date

2026-06-23

## Issue

After submitting time for Sunday, the weekly timesheet status showed as submitted and the frontend treated the entire week as non-editable. This caused Monday, Tuesday, and other days to appear locked even though only Sunday should have been locked.

The activity cards also appeared grayed out because the UI was still checking whole-week editability instead of day-level editability.

## Correct Behavior

- Submitting Sunday locks Sunday only.
- Monday through Saturday remain editable.
- Engineers and project managers can still add non-project time, project tasks, regular tasks, or requests for days that are still open.
- A submitted day can be unlocked within two hours.
- After two hours, the engineer must contact their manager.

## File Added

- `deployment/rocky-linux/apply-daily-open-day-edit-fix.sh`

## What the Fix Changes

The patch script changes frontend editability from a whole-week check to a day-level check:

- Activity cards are enabled if at least one day is still open.
- Time cells are disabled only when that specific day is locked.
- Save draft is enabled if at least one day is still open.
- The status message clarifies that submitted days are locked individually while open days remain editable.

## Validation Steps

1. Apply migration 007.
2. Run the daily submission policy patch.
3. Run the open-day edit fix patch.
4. Rebuild the frontend.
5. Submit Sunday with at least 8.00 hours.
6. Confirm Sunday is locked.
7. Confirm Monday through Saturday remain editable.
8. Confirm additional activity rows can still be added for open days.

## Status

Ready for validation.
