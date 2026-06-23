# ChangePoint Navigation and Module Observations

## Purpose

This document captures additional navigation and module observations from ChangePoint screenshots.

## Top-Level Navigation Observed

The top navigation includes:

```text
Customers
Opportunities
Projects
Contracts
Invoices
Requests
```

The application launcher also exposes grouped modules.

## Application Group

Observed items:

```text
Time Sheet
Expenses
Calendar
Team Folders
Recents
New
```

For the Project Time Platform, the most relevant item in this group is:

```text
Time Sheet
```

Future optional modules may include expenses or calendar-style scheduling, but they are not part of the first build unless explicitly scoped later.

## Project Group

Observed item:

```text
Worksheet
```

This reinforces the need for a project/task planning area where project tasks can be created and assigned to engineers.

## Resource Group

Observed items:

```text
Match
Scheduling
Planner
Resources
```

This maps to future resource planning and task assignment workflows.

## Finance Group

Observed items:

```text
Time and Expenses
Revenue Recognition
```

For the Project Time Platform, the most relevant finance workflow is time reconciliation and accounting review.

## Analytics Group

Observed items:

```text
Dashboards
Reports
Portal
```

This supports future dashboards and reports for utilization, approvals, reconciliation, and historical reporting.

## Home Page Observation

The base user home page includes reminder-style content, such as:

```text
You have time to approve.
You have unsubmitted time entries.
```

The Project Time Platform should include a dashboard that surfaces role-based reminders.

## Design Direction for Project Time Platform

The first version should not attempt to clone every ChangePoint module. Instead, it should focus on the core workflow:

```text
Dashboard
Timesheet
Approvals
Projects and Tasks
Resources
Accounting Review
Utilization
Reports
Administration
```

## Suggested Initial Navigation for Our Platform

Recommended first-version navigation:

```text
Dashboard
Timesheets
Approvals
Projects
Resources
Accounting
Utilization
Reports
Admin
```

## Notes

- The platform should feel familiar to users who understand ChangePoint, but the design should be cleaner and more focused.
- Engineers should primarily see Dashboard, Timesheets, Utilization, and Reports.
- Managers should see Dashboard, Approvals, Team Timesheets, Utilization, and Reports.
- Project Managers should see Project Tasks, Project Approval, and Reports.
- Accounting users should see Accounting Review, Reconciliation, Period Locking, and Reports.
- Admin users should see Projects, Tasks, Users, Roles, Non-Project Categories, and System Configuration.
