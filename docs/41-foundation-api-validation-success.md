# Foundation API Validation Success

## Purpose

This document records successful validation of the first PostgreSQL-backed foundation API endpoints.

## API Version

Confirmed API version:

```text
0.2.1
```

## Confirmed Runtime

The API reported:

```text
.NET 10.0.6
Oracle Linux Server 9.7
```

## Confirmed Endpoint Results

### Version

Endpoint:

```text
GET /api/version
```

Confirmed:

```text
application: Project Time Platform
component: ProjectTime.Api
version: 0.2.1
```

### Non-Project Time Categories

Endpoint:

```text
GET /api/non-project-time-categories
```

Confirmed:

```text
count: 15
```

Categories include:

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

### Work Location Groups

Endpoint:

```text
GET /api/work-location-groups
```

Confirmed:

```text
count: 4
```

Groups:

```text
Remote
Office
Customer Site
Other
```

### Work Locations

Endpoint:

```text
GET /api/work-locations
```

Confirmed:

```text
count: 1
```

Default location:

```text
Los Angeles, CA
America/Los_Angeles
```

### Utilization Policies

Endpoint:

```text
GET /api/utilization/policies
```

Confirmed:

```text
Default 2026 Quarterly Utilization Policy
period type: quarterly
standard period hours: 482
standard target percent: 70
presales/training requires approval: true
```

### Utilization Targets

Endpoint:

```text
GET /api/utilization/targets
```

Confirmed:

```text
count: 8
```

Targets:

```text
70%
75%
80%
85%
90%
95%
100%
105%
```

### Weekly Timesheet Shell

Endpoint:

```text
GET /api/timesheets/week?weekStart=2026-06-21
```

Confirmed week:

```text
2026-06-21 through 2026-06-27
```

Confirmed days:

```text
Sunday
Monday
Tuesday
Wednesday
Thursday
Friday
Saturday
```

Confirmed time types:

```text
normal
afterhours
```

## Current Status

The backend API can now expose real platform configuration data from PostgreSQL for:

```text
non-project time categories
work locations
utilization policies
utilization targets
weekly timesheet shell
```

## Next Step

Update the React frontend to consume these endpoints and display:

```text
weekly timesheet shell
non-project category list
work location options
utilization policy summary
utilization target thresholds
```

The API should remain bound to localhost until reverse proxy, TLS, and Microsoft Entra authentication are added.
