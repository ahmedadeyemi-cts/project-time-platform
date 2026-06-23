# Timesheet Edit Window and Time Box Formatting

## Purpose

This document records the update requested after validating browser-based timesheet persistence.

## Date

2026-06-23

## User Request

The requested update includes three changes:

- Keep submitted timesheets editable when they are still in the current week or were submitted within the past hour.
- Display time values consistently with two decimal places, such as `1.00`, `1.50`, and `0.00`.
- Increase the size of the time entry boxes in the weekly grid.

## Files Added or Updated

- `src/frontend/project-time-web/src/timesheet.css`
- `deployment/rocky-linux/apply-timesheet-edit-window-and-format-patch.sh`

## Edit Policy

A timesheet is editable when one of these conditions is true:

- Status is `draft`.
- Status is `manager_declined`.
- Status is `submitted` and the selected week is still the current week.
- Status is `submitted` and the submission occurred within the past hour.

The backend should remain the source of truth for this policy.

## Time Display Policy

All grid time values should display with two decimals. Examples:

- User enters `1`; grid displays `1.00`.
- User enters `1.5`; grid displays `1.50`.
- Empty values display `0.00`.

## Validation Steps

1. Pull the latest repository changes.
2. Run the patch script.
3. Redeploy the API service.
4. Rebuild the frontend.
5. Restart the local frontend server.
6. Enter time in the weekly grid.
7. Confirm the grid displays fixed two-decimal values.
8. Submit a timesheet.
9. Confirm it remains editable under the allowed edit-window rule.
10. Confirm the larger time boxes display cleanly.

## Status

Ready for validation.
