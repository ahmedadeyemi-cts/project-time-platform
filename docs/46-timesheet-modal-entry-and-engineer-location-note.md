# Timesheet Modal Entry and Engineer Location Note

## Purpose

This document records the follow-up update made after validating the interactive timesheet page layout.

## Date

2026-06-23

## User Feedback

During validation, the user identified two important usability and business requirements:

1. The time values entered into the weekly grid were not easy to see because the input fields were too narrow.
2. The Details panel did not need to remain on the far right of the page. It could work better as a centered pop-up/modal when an engineer selects a day and time type.
3. Work location must not be hard-coded to one location. Engineers are located across multiple areas, and the system must reflect the correct location for each engineer once onboarding begins.

## Changes Made

### Visible Time Values

The weekly grid no longer uses narrow native number inputs directly inside the table. Instead, each Normal and Afterhours slot now displays as a visible time-entry button.

The button shows the current value, such as:

- `0.00`
- `1`
- `2.5`
- `8`

When selected, the engineer enters or updates the hours from the details modal.

### Centered Details Modal

The former right-side Details panel was removed from the timesheet workspace. Selecting a time slot now opens a centered modal window that contains:

- Activity name
- Date
- Time type: Normal or Afterhours
- Hours input
- Description/comment field
- Work location group dropdown
- Work location dropdown

This gives more horizontal space back to the weekly grid and avoids pushing the details section off-screen.

### Engineer Work Location Requirement

The current work location options are still foundation/demo data. The long-term design must support work location by engineer/resource profile.

Future onboarding should capture or derive the following per engineer:

- Primary work location group
- Primary work location
- Time zone
- Workgroup/team
- Manager/reporting relationship
- Role assignments
- Project task assignments

The timesheet should default to the engineer's configured profile location, but still allow appropriate authorized overrides if company policy permits.

## Files Updated

- `src/frontend/project-time-web/src/App.jsx`
- `src/frontend/project-time-web/src/timesheet.css`

## Expected Result

After rebuilding the frontend, the timesheet page should show clear visible values in the weekly grid. Clicking a Normal or Afterhours time slot should open a centered modal where the engineer can enter hours, comments, and location details.

## Validation Steps

1. Pull the latest repository updates on the OCI VM.
2. Rebuild the frontend.
3. Restart the local frontend server.
4. Open the application through `http://127.0.0.1:5173/` using the SSH tunnel.
5. Hard refresh the browser.
6. Scroll to the Timesheet section.
7. Click a `0.00` cell under a day and time type.
8. Confirm a centered modal opens.
9. Enter hours into the modal.
10. Confirm the value becomes visible in the weekly grid after entry.
11. Add a comment.
12. Select a work location group and work location.
13. Close the modal and confirm the grid remains usable.
14. Submit the timesheet and confirm the status updates.

## Next Recommended Build Phase

The next build phase should add persistence and engineer-specific defaults:

- Engineer/resource profile defaults for location and time zone.
- API endpoint to save draft time entries.
- API endpoint to load saved weekly draft entries.
- Submit endpoint that changes timesheet status and creates the manager approval workflow.

## Status

Ready for validation.
