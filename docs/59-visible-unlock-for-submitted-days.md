# Visible Unlock for Submitted Days

## Purpose

This document records the fix for the missing Unlock action after the time-entry modal action buttons were moved to the top-right of the modal.

## Date

2026-06-23

## Issue

The `Unlock this day` button was available only inside the time-entry modal. However, submitted day cells were disabled in the grid. Because disabled cells could not be clicked, the user could not open the modal for a submitted day and therefore could not see the Unlock button.

## Correct Behavior

Submitted days should remain locked from editing, but the submitted day cells must still be clickable so the user can open the modal and request an unlock.

## File Added

- `deployment/rocky-linux/apply-visible-unlock-for-submitted-days.sh`

## What the Fix Does

The patch keeps time-entry cells clickable even when the day is submitted.

The modal still prevents editing the fields for submitted days. The only action available for that submitted day is `Unlock this day`.

## Validation Steps

1. Pull the latest repository changes.
2. Run the visible unlock patch script.
3. Rebuild the frontend.
4. Restart the local frontend server.
5. Open a submitted day cell.
6. Confirm the modal opens.
7. Confirm `Unlock this day` appears beside `Close`.
8. Click `Unlock this day` within two hours and confirm the day becomes editable.

## Status

Ready for validation.
