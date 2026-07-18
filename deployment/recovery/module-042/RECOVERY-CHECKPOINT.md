# MODULE 042 Recovery Checkpoint

## Purpose

This file is the durable Git checkpoint for the Invoice & Billing Center implementation and recovery work completed on 2026-07-14 and 2026-07-15.

## Branch and pull request

- Branch: `source/invoice-billing-center-preview-20260714`
- Pull request: `#12` — MODULE 042: Invoice & Billing Center
- Checkpoint parent commit: `e4973fdf64a509b691568cb88dee871844943653`
- Lost local implementation commit recorded in build logs: `0fd13fe04474bf5a4f614e35712f83618eb02606`
- Lost local commit title: `MODULE 042: implement live billing candidates and invoices`

The implementation commit built successfully but was not pushed before the Azure Cloud Shell session was replaced. The validated generator artifact must be retained in Git before rebuilding or deploying.

## Live database state

Migration `deployment/database/056-module-042-billing-integration-foundation.sql` was applied successfully before this checkpoint.

Validated live results:

- `MIGRATION_APPLIED=YES`
- `DATABASE_SCHEMA_MODIFIED=YES`
- Module 042 table count: `9`
- Live task foreign key: `project_tasks.task_id`
- Invoice identity format: `PHD-XXXXXX-N`
- Invoice sequence consumed during migration: `NO`
- Connector definitions seeded as `not_configured`: `3`
- No operational invoice was created by migration or smoke testing.

Corrected migration SHA-256:

`44f165b789be07a4db5d213559c911ed834fc4b1e998554b46de2f15cac8e111`

Pre-migration backup:

`m042-pre-migration-056-20260714-235604`

## Validated implementation behavior

The recovered source generator creates and validates these files:

- `src/backend/ProjectTime.Api/Modules/InvoiceBillingModule.cs`
- `src/backend/ProjectTime.Api/Program.cs`
- `src/frontend/project-time-web/src/InvoiceBillingCenter.jsx`
- `src/frontend/project-time-web/src/PageContextGuide.jsx`

Backend endpoints:

- `GET /api/billing/candidates`
- `GET /api/billing/projects/{projectId}/candidates`
- `GET /api/billing/projects/{projectId}/invoices`
- `GET /api/billing/invoices/{invoiceId}`
- `POST /api/billing/projects/{projectId}/invoices`

Validated rules:

- Uses approved, billable, uninvoiced time entries.
- Uses effective stored rate-card lines only.
- Requires explicit rate selection when more than one stored rate matches.
- Does not fabricate rates, hours, amounts, projects, customers, or invoices.
- Blocks hourly-dollar invoice creation for Fixed Price projects until milestone/fixed-price billing records exist.
- Uses serializable transactions and unique time-entry protection.
- Allocates immutable per-project invoice identities atomically.
- Supports partial and final invoices.
- Final invoice requires all remaining eligible lines.
- Preserves source time entries.
- Leaves external connectors `not_configured`.

## Successful source validation recorded before session loss

- .NET SDK used: `10.0.302`
- Backend build: `PASSED`
- Backend errors: `0`
- Frontend build: `PASSED`
- Frontend billing route bundle: `PASSED`
- Worktree after commit: `CLEAN`
- Local implementation commit created: `YES`
- Remote push completed: `NO`
- Application deployed: `NO`
- Database modified by source/build process: `NO`

## Validated generator artifact

Preferred artifact:

`implement-module-042-live-billing-slice-e497-dotnet10-eof-fixed.sh`

SHA-256:

`1dd2b207ec30009e8db295d2aa029cd837e8549242bc880223a32f0ad51b61f8`

The artifact contains the complete generated C# and React source, exact source patching logic, static validation, .NET 10 setup, backend build, frontend build, local commit creation, and checkpoint bundle creation.

## Deployment checkpoint

Current live images at the time of recovery inspection:

- API: `acrphdtest7825cc.azurecr.io/project-health-dashboard-api@sha256:621dc01218306c6842ec7ca85fc57baf27a5bf469bccc3117ab52c204bf95be9`
- Web: `acrphdtest7825cc.azurecr.io/project-health-dashboard-web@sha256:86290813e964e7ee0e2710f898238ba574c716ab023f0a4cd90751d540e5e520`

Current live revisions at the time of recovery inspection:

- API: `ca-phd-test-api-westus3--az12d3api-0713203653`
- Web: `ca-phd-test-web-westus3--m042live-0714194827`

Both applications were running successfully and had not received the lost implementation commit.

## Recovery order

1. Store the validated generator artifact in this Git recovery directory.
2. Reconstruct the source from the exact generator.
3. Build API and web.
4. Commit and push immediately after successful validation.
5. Build versioned ACR images from the pushed commit.
6. Deploy API first and validate health/auth guard.
7. Deploy web second and validate the live billing bundle.
8. Do not create an invoice during automated smoke testing.

## Operational rule

All future implementation generators, schema reports, migration scripts, build manifests, deployment scripts, and recovery notes for this module must be committed to Git before long-running builds or Azure deployment operations begin.
