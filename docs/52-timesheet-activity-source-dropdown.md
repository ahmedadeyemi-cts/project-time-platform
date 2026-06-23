# Timesheet Activity Source Dropdown

## Purpose

This document records the UI change to make timesheet activities selectable by activity source.

## Date

2026-06-23

## User Request

The user wanted the activity list to behave more like the reference ChangePoint screen. Instead of always showing all non-project time categories as a static list, the Timesheet page should provide an activity-source dropdown.

## Activity Sources

The Timesheet activity panel now includes a dropdown with these options:

- Non-project time
- Open tasks
- Regular tasks
- Requests / Service Requests

## Current Behavior

When `Non-project time` is selected, the panel displays all non-project time categories loaded from the API.

When `Open tasks`, `Regular tasks`, or `Requests / Service Requests` is selected, the panel displays an empty-state message until those workflows are connected.

## Future Behavior

In future phases:

- Open tasks should display assigned project task work.
- Regular tasks should display recurring or standard task assignments.
- Requests / Service Requests should display service request work available for time entry.

## Files Updated

- `src/frontend/project-time-web/src/App.jsx`
- `src/frontend/project-time-web/src/timesheet.css`

## Validation Steps

1. Rebuild the frontend.
2. Open the Timesheet page.
3. Confirm the Activities panel contains an Activity type dropdown.
4. Select `Non-project time`.
5. Confirm the non-project time categories display.
6. Select `Open tasks`, `Regular tasks`, and `Requests / Service Requests`.
7. Confirm each option displays a clean empty-state message until backend data is connected.

## Status

Ready for validation.
