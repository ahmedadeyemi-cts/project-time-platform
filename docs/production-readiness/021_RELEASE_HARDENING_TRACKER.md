# 021 Release Hardening / Production Readiness Tracker

## Purpose

021 prepares ProjectPulse / ChangePoint for a cleaner end-to-end production readinessnstration and release-readiness pass after the 020 module build sprint.

## Starting Baseline

- 020 Module Build Sprint merged to `main`.
- 020J Shared Email Recipient Safety Review merged to `main`.
- Backend build, backend publish, frontend build, API service, frontend service, and nginx service passed after 020J deployment.
- Protected endpoint smoke checks returned expected unauthenticated `401` responses.

## 021 Module Plan

| Module | Area | Goal | Status |
|---|---|---|---|
| 021A | Release baseline | Create release hardening tracker and reusable smoke script | Complete |
| 021B | Navigation / route integrity | Confirm route labels, route visibility, and dashboard grouping alignment | Complete |
| 021C | Production readiness naming alignment | Align branch, documentation, route reports, and permission naming to production readiness | Complete |
| 021D | Role-based production readiness runbooks | Prepare production readiness paths for Admin, PM, Manager, Engineer, Accounting, and read-only stakeholder access | Complete |
| 021E | Workflow data readiness | Confirm production-critical data signals support customer, intake, assignment, approval, export, and audit flows | Complete |
| 021F | UI production polish readiness | Review copy signals, empty states, route metadata, and responsive presentation indicators | Complete |
| 021G | Operational runbook | Document deploy, rollback, smoke, backup, and restore checkpoints | Not Started |
| 021H | Final release candidate validation | Build, deploy, endpoint smoke, browser validation, and PR closeout | Not Started |

## Production-Critical Workflows

1. Login and role-aware navigation.
2. Customer directory readiness.
3. Project intake creation and readiness review.
4. Resource request and assignment readiness.
5. Approval workflow action path.
6. Export package readiness and download evidence.
7. Audit evidence filtering.
8. Production operations and reporting dashboard consolidation.
9. View-As safety behavior.
10. Email recipient safety review.

## Release Hardening Rules

- Keep 021 changes on `feature/021-release-hardening-production readiness-readiness`.
- Do not mix unrelated feature work into the release hardening branch.
- Preserve any unexpected local WIP before switching branches.
- Use lightweight builds for each 021 submodule.
- Run full deployment validation only at 021G.


## 021B Navigation / Route Integrity

021B added a reusable static route-integrity scanner and generated route inventory reports for production readiness review.

Artifacts:

- `scripts/021-route-integrity-report.py`
- `docs/production-readiness/021_ROUTE_INTEGRITY_REPORT.md`
- `docs/production-readiness/021_ROUTE_INTEGRITY_REPORT.json`
- `docs/help/021B-navigation-route-integrity.md`


## 021C Production Readiness Naming Alignment

021C corrected release-hardening language so this phase is framed as production readiness.

Changes:

- Renamed the working branch to `feature/021-release-hardening-production-readiness`.
- Moved 021 release artifacts into `docs/production-readiness`.
- Standardized the August production-readiness tracker name.
- Updated scanner/report paths to use production-readiness terminology.
- Reframed role walkthrough work as production readiness runbooks.


## Production Readiness Permission Naming

The release-hardening pass standardizes the production readiness command-center permission identifier as:

- `VIEW_PRODUCTION_READINESS_COMMAND_CENTER`


Active route naming was also aligned to `/api/production/readiness-command-center`.


## 021D Role-Based Production Readiness Runbooks

021D generated role-focused production readiness runbooks using the route inventory created in 021B.

Artifacts:

- `scripts/021-role-production-readiness-runbook-report.py`
- `docs/production-readiness/021_ROLE_BASED_PRODUCTION_READINESS_RUNBOOKS.md`
- `docs/production-readiness/021_ROLE_BASED_PRODUCTION_READINESS_RUNBOOKS.json`
- `docs/help/021D-role-based-production-readiness-runbooks.md`


## 021E Workflow Data Readiness

021E generated workflow data readiness reports and a read-only SQL probe for production-critical workflow validation.

Artifacts:

- `scripts/021-workflow-data-readiness-report.py`
- `docs/production-readiness/021_WORKFLOW_DATA_READINESS_REPORT.md`
- `docs/production-readiness/021_WORKFLOW_DATA_READINESS_REPORT.json`
- `database/reports/021-workflow-data-readiness-probe.sql`
- `docs/help/021E-workflow-data-readiness.md`


## 021F UI Production Polish Readiness

021F generated a static UI production-polish report covering product-facing naming, copy review signals, empty/loading/error-state wording, route metadata, and responsive-surface indicators.

Artifacts:

- `scripts/021-ui-production-polish-report.py`
- `docs/production-readiness/021_UI_PRODUCTION_POLISH_REPORT.md`
- `docs/production-readiness/021_UI_PRODUCTION_POLISH_REPORT.json`
- `docs/help/021F-ui-production-polish-readiness.md`
