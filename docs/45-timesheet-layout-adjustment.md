# Timesheet Layout Adjustment

## Purpose

This document records the layout adjustment made after validating the first interactive weekly timesheet page.

## Date

2026-06-23

## User Feedback

During validation, the user confirmed that the timesheet page loaded and that the interactive grid was visible. The user also identified two usability concerns:

1. The timesheet layout was difficult to manage because the Details panel was pushed off the visible page area.
2. The dashboard hero title, `Time, approval, utilization, and accounting workflow foundation`, was too large for the working application interface.

## Root Cause

The initial timesheet workspace used three fixed-width layout areas:

- Activities panel
- Timesheet grid
- Details panel

The combined minimum width was too wide for the current browser viewport. As a result, the Details panel was visible only partially or pushed beyond the usable screen area.

The dashboard hero title used a large landing-page style font size. That was acceptable for an early visual landing page but too large once the application began functioning as an operational tool.

## Files Updated

- `src/frontend/project-time-web/src/styles.css`
- `src/frontend/project-time-web/src/timesheet.css`

## Changes Made

### Dashboard Hero

The dashboard hero area was reduced by:

- Lowering the hero panel padding.
- Reducing the maximum `h1` font size.
- Slightly increasing line height for readability.
- Reducing hero copy size slightly.

### Timesheet Workspace

The timesheet workspace was adjusted by:

- Reducing the left activity panel width.
- Reducing the timesheet grid minimum width.
- Reducing individual grid column widths.
- Keeping the Details panel visible as a right-side panel on wider screens.
- Making the Details panel sticky so it remains accessible while scrolling.
- Allowing the timesheet grid itself to scroll horizontally inside its own container instead of pushing the Details panel off-screen.
- Stacking the timesheet workspace on narrower screens.

## Expected Result

After rebuilding the frontend, the user should see:

- A smaller dashboard hero section.
- A more manageable timesheet workspace.
- The Details panel visible on the right side on wider screens.
- Horizontal scrolling contained inside the timesheet grid when needed.
- Continued support for light and dark mode.

## Validation Steps

1. Pull the latest repository updates on the OCI VM.
2. Rebuild the frontend.
3. Restart the local frontend server.
4. Open `http://127.0.0.1:5173/` through the SSH tunnel.
5. Hard refresh the browser.
6. Confirm the dashboard title is no longer oversized.
7. Scroll to the Timesheet section.
8. Confirm the Details panel is visible on the right side.
9. Enter time in a cell and confirm the Details panel remains usable.
10. Confirm the grid can scroll horizontally inside its own panel if needed.

## Status

Ready for user validation.
