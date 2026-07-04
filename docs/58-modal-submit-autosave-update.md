# Modal Submit Placement and Autosave Update

## Purpose

This document records the requested update to improve the time-entry modal in Project Health Dashboard.

## Date

2026-06-23

## User Request

The user requested two changes:

1. Move `Submit this day` from the bottom of the modal to the top-right beside `Close`, and make it more visually prominent.
2. Automatically save draft time when the user closes the modal or clicks outside of it, so entered information is not lost.

## Files Updated

- `src/frontend/project-time-web/src/App.jsx`
- `src/frontend/project-time-web/src/timesheet.css`

## Behavior Added

### Submit Placement

The modal header now contains action buttons:

- `Submit this day` for open days
- `Unlock this day` for submitted days
- `Close`

This keeps the day submission action visible and easier to find.

### Autosave on Close

When an engineer enters time and then closes the modal or clicks outside of it, Project Health Dashboard now attempts to save the weekly draft automatically.

The autosave uses the existing draft-save API:

- `POST /api/timesheets/week/draft`

The user sees status messaging such as:

- `Auto-saving draft...`
- `Draft autosaved`

## Notes

Autosave applies only to open/editable days. Submitted days remain locked unless unlocked according to the configured unlock policy.

## Validation Steps

1. Pull the latest source from GitHub.
2. Rebuild the frontend.
3. Restart the local frontend server.
4. Open a time-entry modal for an open day.
5. Enter hours.
6. Click outside the modal or click Close.
7. Confirm the Save status changes to `Draft autosaved`.
8. Refresh the browser and confirm the entered time remains.
9. Reopen the modal and confirm `Submit this day` is visible beside `Close`.

## Status

Ready for validation.
