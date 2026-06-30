# 021 Operational Runbook

Generated UTC: `2026-06-30T16:53:54.531533+00:00`

## Purpose

This runbook defines the operational process for release hardening, production readiness validation, backup, deployment, rollback, smoke testing, and evidence capture for ProjectPulse / ChangePoint.

## Runtime Services

| Service | Purpose | Health Check | Restart | Logs |
|---|---|---|---|---|
| `projecttime-api.service` | Backend API service | `systemctl is-active projecttime-api.service` | `sudo systemctl restart projecttime-api.service` | `journalctl -u projecttime-api.service -n 120 --no-pager` |
| `projecttime-frontend-public.service` | Frontend static public service | `systemctl is-active projecttime-frontend-public.service` | `sudo systemctl restart projecttime-frontend-public.service` | `journalctl -u projecttime-frontend-public.service -n 120 --no-pager` |
| `nginx.service` | Reverse proxy and TLS endpoint | `systemctl is-active nginx.service` | `sudo systemctl restart nginx.service` | `journalctl -u nginx.service -n 120 --no-pager` |
| `postgresql.service` | Database service | `systemctl is-active postgresql.service` | `sudo systemctl restart postgresql.service` | `journalctl -u postgresql.service -n 120 --no-pager` |

## Deployment Paths

- `/opt/project-time-platform/app/project-time-platform`
- `/opt/project-time-platform/runtime/backend`
- `/opt/project-time-platform/runtime/frontend`

## Backup Locations

- `/opt/project-time-platform/backups`
- `/tmp/projectpulse-*`

## Production Readiness Artifacts

- `docs/production-readiness/021_RELEASE_HARDENING_TRACKER.md`
- `docs/production-readiness/021_ROUTE_INTEGRITY_REPORT.md`
- `docs/production-readiness/021_ROLE_BASED_PRODUCTION_READINESS_RUNBOOKS.md`
- `docs/production-readiness/021_WORKFLOW_DATA_READINESS_REPORT.md`
- `docs/production-readiness/021_UI_PRODUCTION_POLISH_REPORT.md`
- `docs/production-readiness/021_OPERATIONAL_RUNBOOK.md`

## Standard Validation Sequence

1. Confirm the working branch is clean before deployment.
2. Build backend and frontend artifacts.
3. Create a timestamped backup before replacing runtime files.
4. Deploy backend and frontend build outputs.
5. Restart services in dependency-safe order.
6. Run service status checks.
7. Run endpoint smoke checks.
8. Review production-readiness reports.
9. Capture logs and final evidence before closing the release-candidate validation.

## Endpoint Smoke Matrix

| Endpoint | URL | Expected HTTP Status |
|---|---|---:|
| API health | `http://127.0.0.1:5080/health` | `200` |
| API version | `http://127.0.0.1:5080/api/version` | `200` |
| Production readiness command center protected access | `http://127.0.0.1:5080/api/production/readiness-command-center` | `401` |
| Workflow operational readiness protected access | `http://127.0.0.1:5080/api/workflow/operational-readiness` | `401` |
| Manager approvals protected access | `http://127.0.0.1:5080/api/manager/approvals` | `401` |
| Audit history protected access | `http://127.0.0.1:5080/api/audit/history` | `401` |
| Public frontend | `https://projectpulse-test.onenecklab.com` | `200` |

## Rollback Sequence

1. Identify the last known-good backup under `/opt/project-time-platform/backups`.
2. Stop the frontend and API services.
3. Restore the backend published output from the selected backup.
4. Restore the frontend published output from the selected backup.
5. Restart API, frontend, and nginx services.
6. Run the production-readiness smoke script.
7. Capture service status, endpoint status, and git revision evidence.

## Evidence Capture Checklist

- Current branch and commit hash.
- Backend build output.
- Frontend build output.
- Service active status for API, frontend, nginx, and PostgreSQL.
- Endpoint smoke output.
- Backup folder path.
- Any release-candidate validation findings.
- Rollback decision, if rollback is required.

## Smoke Script

Use the generated smoke script during production readiness validation:

```bash
scripts/021-production-readiness-smoke.sh
```

The script checks service status, endpoint status, and git revision evidence.
