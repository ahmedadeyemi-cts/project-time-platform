# Foundation API Endpoints

## Purpose

This document captures the first API endpoints that expose real PostgreSQL-backed platform configuration data to the React frontend.

## API Version

```text
0.2.0
```

## Health and Database Endpoints

```text
GET /health
GET /api/version
GET /api/db-config-check
GET /api/db-health
GET /api/schema/tables
```

## Timesheet Configuration Endpoints

### Non-Project Time Categories

```text
GET /api/non-project-time-categories
```

Returns active non-project time categories, including:

```text
category code
category name
description
utilization classification
utilization bucket
approval requirement
display order
```

### Weekly Timesheet Shell

```text
GET /api/timesheets/week
GET /api/timesheets/week?weekStart=2026-06-21
```

Returns:

```text
week start
week end
Sunday through Saturday day list
normal and afterhours time types
active non-project time categories
```

This is the initial shell endpoint. User-specific saved time entries will be added later.

## Work Location Endpoints

### Work Location Groups

```text
GET /api/work-location-groups
```

### Work Locations

```text
GET /api/work-locations
```

These support the timesheet details panel and resource profile work location fields.

## Utilization Endpoints

### Utilization Policies

```text
GET /api/utilization/policies
```

Returns configured utilization policies, including:

```text
policy name
period type
standard period hours
default target percent
presales/training approval requirement
active status
```

### Utilization Targets

```text
GET /api/utilization/targets
```

Returns active target thresholds and reference amounts.

## Local Validation Commands

After pulling and publishing the updated API, validate locally:

```bash
curl http://127.0.0.1:5080/api/version
curl http://127.0.0.1:5080/api/non-project-time-categories
curl http://127.0.0.1:5080/api/work-location-groups
curl http://127.0.0.1:5080/api/work-locations
curl http://127.0.0.1:5080/api/utilization/policies
curl http://127.0.0.1:5080/api/utilization/targets
curl 'http://127.0.0.1:5080/api/timesheets/week?weekStart=2026-06-21'
```

## Deployment Note

Use the existing systemd installer to publish and restart the API:

```bash
chmod +x deployment/rocky-linux/install-api-systemd-service.sh
./deployment/rocky-linux/install-api-systemd-service.sh
```

## Security Notes

- API remains bound to `127.0.0.1:5080`.
- Do not open the API directly to the public internet.
- Authentication and reverse proxy controls must be added before public access.
