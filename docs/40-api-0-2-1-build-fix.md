# API 0.2.1 Build Fix

## Purpose

This document records the build issue encountered after adding the foundation API endpoints and the corrective change.

## Issue

The API failed during publish with the following compiler error:

```text
error CS0173: Type of conditional expression cannot be determined because there is no implicit conversion between '<null>' and 'decimal'
```

## Cause

The utilization targets endpoint returned a nullable bonus/reference amount with this pattern:

```csharp
reader.IsDBNull(3) ? null : reader.GetDecimal(3)
```

C# could not infer the nullable decimal type from `null` and `decimal`.

## Fix

The expression was updated to explicitly use nullable decimal:

```csharp
reader.IsDBNull(3) ? (decimal?)null : reader.GetDecimal(3)
```

## API Version

The API version was advanced to:

```text
0.2.1
```

## Installer Improvement

The API systemd installer was also improved to stop the service through systemd before publishing instead of only killing processes directly.

Updated script:

```text
deployment/rocky-linux/install-api-systemd-service.sh
```

## Apply Steps

Pull the fix and rerun the service installer:

```bash
cd /opt/project-time-platform/app/project-time-platform

git restore deployment/rocky-linux/build-frontend.sh || true
git restore deployment/rocky-linux/install-nodejs-oraclelinux9.sh || true
git restore deployment/rocky-linux/apply-initial-schema.sh || true

GIT_SSH_COMMAND='ssh -i ~/.ssh/github_project_time_platform -o IdentitiesOnly=yes' \
git pull

chmod +x deployment/rocky-linux/install-api-systemd-service.sh
./deployment/rocky-linux/install-api-systemd-service.sh
```

## Validation Endpoints

```bash
curl http://127.0.0.1:5080/api/version
curl http://127.0.0.1:5080/api/non-project-time-categories
curl http://127.0.0.1:5080/api/work-location-groups
curl http://127.0.0.1:5080/api/work-locations
curl http://127.0.0.1:5080/api/utilization/policies
curl http://127.0.0.1:5080/api/utilization/targets
curl 'http://127.0.0.1:5080/api/timesheets/week?weekStart=2026-06-21'
```
