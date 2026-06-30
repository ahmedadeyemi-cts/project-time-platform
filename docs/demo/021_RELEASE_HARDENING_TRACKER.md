# 021 Release Hardening / Demo Readiness Tracker

## Purpose

021 prepares ProjectPulse / ChangePoint for a cleaner end-to-end demonstration and release-readiness pass after the 020 module build sprint.

## Starting Baseline

- 020 Module Build Sprint merged to `main`.
- 020J Shared Email Recipient Safety Review merged to `main`.
- Backend build, backend publish, frontend build, API service, frontend service, and nginx service passed after 020J deployment.
- Protected endpoint smoke checks returned expected unauthenticated `401` responses.

## 021 Module Plan

| Module | Area | Goal | Status |
|---|---|---|---|
| 021A | Release baseline | Create release hardening tracker and reusable smoke script | In Progress |
| 021B | Navigation / route integrity | Confirm route labels, route visibility, and dashboard grouping alignment | Not Started |
| 021C | Role-based demo scripts | Prepare demo paths for Admin, PM, Manager, Engineer, Accounting, and Viewer-style access | Not Started |
| 021D | Workflow data readiness | Confirm seeded/demo data supports customer, intake, assignment, approval, export, and audit flows | Not Started |
| 021E | UI polish | Address obvious copy, empty states, filter clarity, and responsive presentation gaps | Not Started |
| 021F | Operational runbook | Document deploy, rollback, smoke, backup, and restore checkpoints | Not Started |
| 021G | Final release candidate validation | Build, deploy, endpoint smoke, browser validation, and PR closeout | Not Started |

## Demo-Critical Workflows

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

- Keep 021 changes on `feature/021-release-hardening-demo-readiness`.
- Do not mix unrelated feature work into the release hardening branch.
- Preserve any unexpected local WIP before switching branches.
- Use lightweight builds for each 021 submodule.
- Run full deployment validation only at 021G.
