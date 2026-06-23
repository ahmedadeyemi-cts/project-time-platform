# Additional ChangePoint Workflow Observations

## Purpose

This document captures additional workflow observations from ChangePoint screenshots and user feedback.

## Weekly Timesheet Navigation

The timesheet is week-based.

Observed behavior:

- The screen shows the current week start date.
- Engineers can move backward and forward by week.
- The weekly grid displays Sunday through Saturday.
- Each day has separate entry cells for regular time and afterhours or overtime.

## Time Entry Details Panel

When an engineer enters time, a details panel appears.

Observed fields:

- Description or comment.
- Work location group.
- Work location.

The description field appears required when entering time.

## Normal Hours and OT Hours

ChangePoint shows separate approval columns for:

```text
Regular hours
OT hours
```

In Project Time Platform, this maps to:

```text
normal
afterhours
```

stored in:

```text
time_entries.time_type
```

## Manager Approval View

The manager approval view shows rows with:

- View details.
- Resource.
- Customer.
- Contract.
- Project.
- Task.
- Date.
- Regular hours.
- OT hours.
- Approver.

The manager can approve selected or reject selected entries.

## Home Page Reminders

The home page shows reminders such as:

```text
You have time to approve.
You have unsubmitted time entries.
```

Project Time Platform should include a reminder service or dashboard widget that surfaces similar action items.

## Project Task Assignment Rule

A project assignment should not mean assigning the entire project to an engineer.

The expected model is:

```text
Project -> Task -> Engineer Assignment
```

This means project tasks are the assignable unit for engineers.

## Utilization Notes

Utilization should include billable time from assigned project tasks.

Some non-project categories, such as vacation, may also need to be included or excluded based on the organization's utilization policy.

The system must therefore avoid hardcoding all non-project categories as one utilization treatment. Each non-project time category needs a classification that can drive utilization calculations.

## Future Screenshots Needed

The following would be useful later:

- Project manager approval screen.
- Accounting reconciliation screen.
- Utilization report or utilization calculation screen.
- Project task assignment screen.
- Details panel for a project-based time entry.
