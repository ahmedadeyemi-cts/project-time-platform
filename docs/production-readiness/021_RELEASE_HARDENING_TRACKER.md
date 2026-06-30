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
| 021G | Operational runbook | Document deploy, rollback, smoke, backup, restore, and evidence checkpoints | Complete |
| 021H | Test deployment and webpage validation baseline | Deploy branch to test runtime, run endpoint smoke, and define browser validation checks | Complete |

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


## 021G Operational Runbook

021G generated the operational runbook and production readiness smoke script for release-candidate validation.

Artifacts:

- `scripts/021-operational-runbook-report.py`
- `scripts/021-production-readiness-smoke.sh`
- `docs/production-readiness/021_OPERATIONAL_RUNBOOK.md`
- `docs/production-readiness/021_OPERATIONAL_RUNBOOK.json`
- `docs/help/021G-operational-runbook.md`


## 021H Test Deployment and Webpage Validation

021H deployed the production-readiness branch to the test runtime, ran service and endpoint smoke checks, and created a browser validation checklist.

Artifact:

- `docs/production-readiness/021H_TEST_DEPLOYMENT_AND_WEBPAGE_VALIDATION.md`


## 021I Web-Visible Production Readiness Center

021I adds a webpage-visible Production Readiness Center so backend readiness work can be reviewed directly in the app.

Webpage:

- `https://projectpulse-test.onenecklab.com/#production-readiness`

What to check:

- Production Readiness appears in navigation.
- The page loads without a blank screen.
- The backend readiness endpoint status is shown.
- Readiness cards and check table display for authorized users.
- Unauthorized users receive a clear access/session message.
- Validation checklist links route to major workflow areas.

Artifacts:

- `src/frontend/project-time-web/src/ProductionReadinessCenterPanel.jsx`
- `src/frontend/project-time-web/src/production-readiness-center.css`
- `docs/help/021I-web-visible-production-readiness-center.md`


## 021J Browser Validation Assist

021J adds a webpage-visible browser validation checklist to the Production Readiness Center.

Webpage:

- `https://projectpulse-test.onenecklab.com/#production-readiness`

What to check:

- Checklist appears below readiness cards.
- Checkboxes update progress.
- Notes save in browser local storage.
- Open links navigate to the related app pages.
- Reset checklist clears progress.

Artifacts:

- `src/frontend/project-time-web/src/ProductionReadinessBrowserValidationPanel.jsx`
- `src/frontend/project-time-web/src/ProductionReadinessCenterPanel.jsx`
- `src/frontend/project-time-web/src/production-readiness-center.css`
- `docs/help/021J-browser-validation-assist.md`


## 021K Webpage Backend Purpose Map

021K adds a webpage-visible purpose map that explains what each page shows, which backend process supports it, and what should be checked during validation.

Webpage:

- `https://projectpulse-test.onenecklab.com/#production-readiness`

What to check:

- Purpose map appears in the Production Readiness Center.
- Rows connect visible pages to backend support.
- Links navigate to the correct app routes.
- The table remains readable and scrollable.

Artifacts:

- `src/frontend/project-time-web/src/ProductionReadinessPurposeMapPanel.jsx`
- `src/frontend/project-time-web/src/ProductionReadinessCenterPanel.jsx`
- `src/frontend/project-time-web/src/production-readiness-center.css`
- `docs/help/021K-webpage-backend-purpose-map.md`


## 021L In-App Page Context Guide

021L adds a visible page context guide across the signed-in app so every major webpage explains its purpose, backend support, and validation expectation.

What to check:

- Guide appears on signed-in pages.
- Guide updates when navigating between app routes.
- Purpose, backend support, and validation guidance are clear.
- Show / hide works.

Artifacts:

- `src/frontend/project-time-web/src/PageContextGuide.jsx`
- `src/frontend/project-time-web/src/page-context-guide.css`
- `src/frontend/project-time-web/src/App.jsx`
- `docs/help/021L-in-app-page-context-guide.md`


## 021M Release Candidate Closeout Panel

021M adds a visible release-candidate closeout panel to the Production Readiness Center.

Webpage:

- `https://projectpulse-test.onenecklab.com/#production-readiness`

What to check:

- Closeout panel appears.
- Checklist updates the Pending/Ready status.
- Final decision notes save after refresh.
- Reset closeout clears state.

Artifacts:

- `src/frontend/project-time-web/src/ProductionReadinessReleaseCloseoutPanel.jsx`
- `src/frontend/project-time-web/src/ProductionReadinessCenterPanel.jsx`
- `src/frontend/project-time-web/src/production-readiness-center.css`
- `docs/help/021M-release-candidate-closeout-panel.md`


## 021N Final Release Candidate Validation

021N captures final release-candidate evidence for the production-readiness branch.

Artifact:

- `docs/production-readiness/021N_FINAL_RELEASE_CANDIDATE_VALIDATION.md`

Status:

- Build validation complete.
- Service validation complete.
- Endpoint smoke validation complete.
- Final browser validation ready for PR review.
