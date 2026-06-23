# Admin, Settings, and Resource Profile Observations

## Purpose

This document captures observations from ChangePoint screenshots related to administrator view, user settings, user profile, and resource profile behavior.

## Administrator View Observation

The user can enter an administrator-style view but is not a full administrator. The available menu items appear role-dependent.

Observed administration-related items include:

```text
Tax Rates
Tax Schedule
Work Codes
Work Locations
Expense Taxation
Recoverable Taxes
Service Taxation
Product Taxation
```

For the Project Time Platform, the immediate relevant items are:

```text
Work Codes
Work Locations
```

Taxation and expense modules are not part of the initial scope unless added later.

## User Menu Observation

The user menu includes:

```text
My profile
My settings
Administrator view
Exit Administrator view
Customer Success Center
Help
About
Sign out
```

For the Project Time Platform, the first version should support:

```text
My profile
My settings
Sign out
```

Admin view should be role-controlled and only visible to users with an administrative role.

## My Settings Observation

The settings screen includes sections such as:

```text
My contact info
Dashboards
Approvals
Preferred resources
Security features
Qualifications
Notifications
Knowledge subscriptions
Calendar synchronization
Calendar time format
Display options
Request templates
Planning and time sheet units
```

For the first Project Time Platform version, the relevant settings are:

```text
Contact information
Dashboard preferences
Approval preferences
Security profile display
Qualifications
Notifications
Display options
Time sheet units
```

## Resource Profile Observation

The resource profile includes:

```text
Resource ID
Time zone
Location
Resource type
Reports to
Organization or workgroup
Primary function
Functions
Qualifications
Project history
Knowledge items
Team folders
Record history
```

For Project Time Platform, the core fields to support are:

```text
resource identifier
time zone
work location
resource type
manager relationship
workgroup or team
primary function
qualifications
project/task assignment history
record history/audit history
```

## Qualifications Observation

The resource profile includes qualification rows with:

```text
qualification category
qualification
competency
years of experience
```

This can support future resource matching, task assignment, and reporting.

## Work Location Observation

Work location appears in two places:

1. Timesheet entry details panel.
2. Resource profile general information.

This means work location should be modeled as a reusable configuration object rather than a free-text field only.

## Implementation Direction

Recommended next data model additions:

```text
work_location_groups
work_locations
resource_profiles
resource_functions
resource_qualifications
```

Recommended time entry additions:

```text
work_location_group_id
work_location_id
```

## Scope Control

The first build should not attempt to reproduce all ChangePoint admin modules. The first build should focus on:

```text
users
roles
teams
resource profiles
work locations
project tasks
task assignments
time entry
approval
accounting review
utilization
reports
```
