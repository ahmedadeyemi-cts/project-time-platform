# ChangePoint Timesheet Behavior Requirements

## Purpose

This document captures additional requirements observed from ChangePoint-style timesheet screenshots and user feedback.

## Requirement 1: Non-Project Time Categories

Engineers and project managers must be able to select non-project time categories while entering weekly time.

Examples include:

```text
Administrative
Bereavement
Comp Time
Holiday
Jury Duty
Long-Term Disability
Peer Support
Personal Holiday
Pre-Approved FMLA
Short-Term Disability
Sick Leave
Time off without pay
Training
Vacation
Volunteer Time
```

## Requirement 2: Daily Normal Time and Afterhours Time

Each time entry day must support separate buckets for:

```text
normal
afterhours
```

This means an engineer may enter regular project time and afterhours project time separately on the same day.

The same structure should also support non-project activities when the organization allows it.

## Data Model Update

Migration added:

```text
database/migrations/002_non_project_time_and_hour_types.sql
```

Seed data added:

```text
database/seed-data/001_non_project_time_categories.sql
```

Apply script added:

```text
deployment/rocky-linux/apply-migration-002.sh
```

## Database Changes

New table:

```text
non_project_time_categories
```

New columns on `time_entries`:

```text
non_project_time_category_id
time_type
```

Adjusted column:

```text
project_id can be null when non_project_time_category_id is used
```

## Validation Rules

A time entry must be either:

```text
project-based
```

or:

```text
non-project category-based
```

A time entry cannot be both at the same time.

The `time_type` field must be:

```text
normal
```

or:

```text
afterhours
```

If a task is selected, the entry must be tied to a project.

## UI Implications

The time entry screen should eventually support:

1. A project activity selector.
2. A non-project time selector.
3. Daily normal hours entry.
4. Daily afterhours entry.
5. Weekly totals by row.
6. Weekly totals by time type.
7. Submission workflow after all entries are complete.

## Screenshots That Would Help Later

Additional screenshots that would be useful before we build the full time entry UI:

1. A populated project time entry row.
2. A populated non-project time entry row.
3. Any details panel shown when a row is selected.
4. The manager or project manager approval view.
5. The accounting or reconciliation view, if available.

## Security and Governance Notes

- Leave categories should be role-controlled and organization-configurable.
- Some categories may require approval while others may be system-generated or pre-approved.
- Utilization calculations must treat each non-project category according to its configured classification.
