# 021H Test Deployment and Webpage Validation

Generated UTC: Tue Jun 30 05:11:28 PM UTC 2026

## Branch / Commit

- Branch: feature/021-release-hardening-production-readiness
- Commit: 2c196a76c30f054d509873f07c440b3fdb1ac62c
- Commit summary: 2c196a7 021G Add operational runbook

## Deployment Target

- Webpage: https://projectpulse-test.onenecklab.com
- API local base: http://127.0.0.1:5080
- Backup root: /opt/project-time-platform/backups/021h-test-deploy-20260630-170554

## Backend Purpose

The backend processes in this release hardening branch support role enforcement, production readiness status, workflow protection, approval/export/audit controls, and endpoint smoke validation.

## Webpage Areas to Check

| Area | What to Confirm |
|---|---|
| Dashboard | App loads cleanly and primary navigation is usable. |
| Production Operations | Production operations/readiness panels load without frontend errors. |
| Project Intake | Intake, aging, post-intake, and handoff areas load. |
| Resource Assignment | Resource assignment and allocation views load. |
| Approval / Export / Audit Workflows | Approval, export, reconciliation, and audit workflow views load. |
| Manager Approvals | Approval queues are visible only to the right role. |
| Role / Security Administration | Role enforcement and View-As remain controlled. |
| Audit History | Audit records and filters load. |

## Endpoint Smoke Results

| Check | Expected | Actual |
|---|---:|---:|
| /health | 200 | 200 |
| /api/version | 200 | 200 |
| Frontend | 200 | 200 |
| /api/production/readiness-command-center unauthenticated | 401 | 401 |
| /api/workflow/operational-readiness unauthenticated | 401 | 401 |
| /api/manager/approvals unauthenticated | 401 | 401 |
| /api/audit/history unauthenticated | 401 | 401 |

## Manual Webpage Validation Status

Manual browser validation still needs to be performed after this deployment.

