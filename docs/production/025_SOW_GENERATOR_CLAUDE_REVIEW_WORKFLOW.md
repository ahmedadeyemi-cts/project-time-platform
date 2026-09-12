# 025 SOW/GSD Workspace

## Status
Applied as the current, persistent Module 025 implementation. This document replaces an
earlier hash-route SOW Generator prototype (see History below); none of that prototype's
behavior exists in the current codebase.

## What it is
Module 025 gives Solution Architects (SAs) a governed workspace to author Statements of
Work (SOW) and General Solution Designs (GSD) as a single persistent record per engagement
— from a free-text Service Overview, through Celar AI-assisted scoping, SA review,
confirmation, and document export.

## Roles and access
- Solution Architects create and own their own engagements.
- Managers and team leads get read-only visibility into their direct reports' engagements
  (scoped via `reporting_relationships`); they cannot edit.
- Administrators have full read/write access across all engagements.
- Administrator "View-As" is always read-only, even for an SA's own records.

## Lifecycle
`draft` → `review_ready` → `confirmed` → `archived`, with `reopen` (`confirmed` →
`review_ready`) and `unarchive`.

- **draft** — created; Service Overview being written.
- **review_ready** — Celar AI has generated a Plan/Design/Implement/Validate/Release scope
  for SA review.
- **confirmed** — SA has confirmed the reviewed package; enables `.docx`/`.xlsx` download.
- **archived** — removed from the active work queue (`is_active=false`); restorable via
  unarchive.

Editing the Service Overview on an already-generated engagement resets its status to
`draft` and clears `last_generated_at`, since the scope must be regenerated against the
new description.

## Scope generation (Celar AI)
`POST /api/module025/sow-gsd/{id}/generate` calls
`CelarAiEnterprisePlatformService.ComposeAsync` (capability `SowGsdPlanning`) with the
Service Overview, in `sow_draft` mode. The returned work packages are classified into the
five Plan/Design/Implement/Validate/Release phases; each phase receives AI-suggested hours,
an objective, and the full detail set (activities, technical tasks, deliverables,
US Signal/customer responsibilities, prerequisites, dependencies, assumptions, open
questions, acceptance criteria, validation steps, risks). If Celar AI can't support a
phase's work with evidence, that phase's objective says so explicitly and its suggested
hours stay at 0 — unsupported claims are never fabricated as generic scope.

AI-suggested hours (`suggested_hours`) and Solution-Architect-reviewed hours
(`final_hours`) are stored as separate columns; only `final_hours` drives the confirmed
GSD's level of effort.

## Confirmation requirements
`POST /api/module025/sow-gsd/{id}/confirm` requires: a generated scope exists, a customer
is selected or entered, an Account Executive is selected, a Resale person is selected, all
five phases have an objective, and total `final_hours` is greater than zero.

## Customer program / GSD template
A `customer_program` of `toyota` or `hyundai` automatically selects the **HAEA Staff Aug
GSD KUS UVO Telematics 1** template for GSD export; `standard` uses the default GSD
template. This pairing is enforced by a database check constraint tying
`customer_program` to `gsd_template_key`.

## Document export
Confirmed engagements can be downloaded as:

- **SOW (`.docx`)** — a hand-built minimal OOXML Word document
  (`Module025SowGsdDocumentExporter.CreateSowDocx`), not the OpenXML SDK.
- **GSD (`.xlsx`)** — a ClosedXML workbook (`CreateGsdXlsx`) with a summary sheet, a
  P-D-I-V-R detail sheet, and a scope/assumptions sheet.

Both exports read from the same document model built from the confirmed engagement and its
phase rows; there is no separate uploaded template file.

## Database
- `module025_sow_gsd_engagements` — one row per SOW/GSD engagement. `engagement_number`
  (e.g. `SOW-2026-000123`) and `owner_user_id` are immutable after creation, enforced by
  trigger `module025_protect_sow_gsd_identity`.
- `module025_sow_gsd_phases` — exactly 5 rows per engagement (plan/design/implement/
  validate/release), each with independent `suggested_hours` (AI) and `final_hours` (SA).
- `module025_sow_gsd_events` — append-only audit trail (`created`, `ai_generated`,
  `confirmed`, `reopened`, `archived`, `unarchived`).

Migration: `database/migrations/099_module025_sow_gsd_workspace.sql` · rollback:
`database/rollback/099_module025_sow_gsd_workspace_rollback.sql`.

## Frontend
`src/frontend/project-time-web/src/module025/SowGsdWorkspace.jsx` — a persistent
(non-hash-route) workspace: a work queue (active/archived tabs, search, a per-SA filter
for managers/admins), an engagement editor with 900ms-debounced autosave and optimistic
revision-conflict handling, the five phase editors, and confirm/reopen/archive/download
actions.

## API surface
- `GET /api/module025/sow-gsd/bootstrap` — current user, access, customers, AE/resale
  directories, visible SAs, static catalogs.
- `GET /api/module025/sow-gsd` — list (`state=active|archived`, `ownerUserId`, `search`).
- `POST /api/module025/sow-gsd` — create.
- `GET /api/module025/sow-gsd/{id}` — read.
- `PUT /api/module025/sow-gsd/{id}` — save (optimistic revision via `expectedRevision`).
- `POST /api/module025/sow-gsd/{id}/generate` — Celar AI scope generation.
- `POST /api/module025/sow-gsd/{id}/confirm`, `/reopen`, `/archive`, `/unarchive` —
  lifecycle transitions.
- `GET /api/module025/sow-gsd/{id}/sow.docx`, `/gsd.xlsx` — confirmed-only document export.

## Workflow placement
- Module 024 validates signed SOW/GSD intake readiness.
- Module 025 (this module) supplies the governed SOW/GSD authoring and export workspace.
- Module 026 supplies CRM-originated context.
- Module 027 prepares the signed handoff, PTC/Executive notification, and PM/Engineer
  assignment trigger from a confirmed Module 025 package.
- Module 028 uses the confirmed SOW/GSD scope context for SOW-aware AI time-entry
  generation.

## History
This document originally tracked an earlier hash-route (`#sow-generator`) SOW Generator
prototype (sub-passes 025A through 025M): dashboard-card injection, a Claude draft studio,
a research-brief workflow, and several rendering/endless-scroll fixes, downloading a
Word-compatible `.doc`. That prototype was fully replaced on 2026-08-30 by the persistent
Module 025 SOW/GSD Workspace described above. The corresponding `docs/help/025*` articles
for that prototype have been marked superseded rather than deleted.
