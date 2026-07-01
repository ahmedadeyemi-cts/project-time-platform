# 025A Timesheet SOW-Aware AI Suggestion

## Purpose
This correction wires SOW-aware AI support into the existing Timesheet time-entry modal instead of creating a separate engineer-facing workflow.

## Webpage impact
The existing Timesheet modal remains the engineer experience. When the engineer selects a Regular/project task and clicks **Generate AI suggestion**, the system uses:
- the selected task/project context,
- the engineer's typed note,
- SOW/GSD context when available.

For non-project time, the assistant skips SOW lookup.

## Backend/process support
Adds `POST /api/timesheets/ai-description-suggestion`.

The endpoint returns:
- suggested customer-facing time-entry description,
- whether SOW context was used,
- whether SOW context is missing,
- whether scope review is recommended.

## Validation
Open `#timesheet`, open a Regular/project task time-entry modal, type a rough note, and click **Generate AI suggestion**.
