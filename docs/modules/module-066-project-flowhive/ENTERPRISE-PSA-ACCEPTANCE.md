# FlowHive enterprise PSA acceptance contract

Date: 2026-09-06
Status: implementation in progress; NOT a deployment or product-parity declaration.
Target environment: Protected UAT/Test only. Production changes are not authorized by this workstream.

## Product objective

A PM selects an existing project, reviews the automatically discovered current SOW, and generates a detailed executable Plan / Design / Implement / Validate / Release WBS. One reviewed task graph supplies the grid, board, timeline, Gantt, calendar, capacity, project controls, customer portal and exports. The financial view reconciles to authorized time and financial records rather than asking AI to invent numbers.

"Better than ClickUp and Smartsheet combined" is an outcome to prove with PM acceptance and measured capability coverage. Adding tabs or naming a table "Gantt" does not meet it. No parity claim is permitted from a passing build alone.

## Benchmark sources checked

Official product references, checked 2026-09-06:

- ClickUp product catalog: https://clickup.com/features
- ClickUp task capabilities: https://clickup.com/features/tasks
- ClickUp task documentation: https://help.clickup.com/hc/en-us/articles/10552031987735-Intro-to-tasks
- Smartsheet platform: https://www.smartsheet.com/platform
- Smartsheet feature catalog: https://www.smartsheet.com/platform/features
- Smartsheet Gantt documentation: https://help.smartsheet.com/articles/765675-work-with-gantt-chart

These establish a benchmark, not proof that FlowHive implements the features. Licensing, region-specific behavior and vendor plan entitlements must be assessed separately before a procurement/parity claim.

## Delivery gates and known gaps

| Area | Acceptance behavior | Evidence required / current gap |
|---|---|---|
| SOW authority | Discover existing Module 055C / Work Register evidence by exact authorized project and current version. Preserve customer linkage, exclusions and conflicting scope. No duplicate upload requirement. | Existing resolver retained. Live tests must cover current, replaced, ambiguous, missing and inaccessible SOWs. |
| AI work breakdown | Each distinct task remains in its intended phase once. Technology-specific steps, inputs, outputs, acceptance, validation, effort, roles and evidence are retained. | New native WBS builder and regression project added. Live provider quality remains unproven. |
| Anti-placeholder quality | A document heading, repeated boilerplate or citation-only scaffold is not a finished plan. Invalid results cannot replace a working copy. | Reject generic scaffolds, missing phases/details/citations, ambiguous WBS and invalid dependencies. Semantic scope coverage still needs representative-SOW evaluation. |
| Milestones | PM-managed project gates are separate from tasks. AI Planner does not manufacture a milestone for each task. | Native builder creates no milestones. Regeneration with existing milestones requires an explicit reviewed merge. |
| Scheduling | Preserve effort separately from elapsed duration. Schedule from dependencies, calendars, availability and project boundaries; show infeasibility instead of shrinking estimates. | Existing deterministic engine is weekday/day-granularity only. Module 057 holidays, leave, resource leveling, intraday time and timezone authority remain release gaps. |
| Plan / Forge convergence | Project Forge reuses the same current-version, same-date-window task graph rather than making a second conflicting project plan. | Reuse now excludes obsolete scaffold contracts and changed date windows. Outcome/assumption fingerprinting and concurrency UAT remain required. |
| Canonical delivery tasks | Reviewed adoption maps stable planning IDs to canonical tasks/assignments without duplicating existing tasks or time records. All task views use this mapping. | Explicit adoption/synchronization and idempotency acceptance remain required; planning persistence alone is not canonical task publication. |
| Project hours | Show approved/assigned budget hours, actual recorded hours, estimate to complete and forecast total as distinct values. Keep overruns visible. | Reconcile with authoritative timesheets, approval states and task mapping. Missing or restricted values must remain unknown, never zero. |
| Project financials | Separate internal cost, contractual revenue, customer bill rates, labor/expense budgets, actuals, open commitments, forecast-at-completion and margin. Show currency and as-of status. | Current financial authority retained. No revenue-to-cost-budget fallback, commitment double counting, or AI-generated rates. Reconciliation tests required. |
| RAID | Risks, assumptions, issues and dependencies have owners, priority, due dates, status and links to tasks. Actions, decisions and changes are supported without confusing record types. | Every create, edit, reassignment, closure and deletion/archival must retain actor, timestamp, prior/new values and reason in append-only audit. Verify database behavior, not just a UI History label. |
| Decisions | Decision register plus optional weighted alternatives, criteria, score explanations, selected option, approver and rationale. | Existing decision register is not yet a weighted decision matrix. |
| Board and grid | Same tasks, statuses, multiple assignees, hierarchy and filters. Drag/drop and keyboard alternatives; no lost edits on switching views. | Initial board source exists. Application integration, persistence, concurrent edit handling and accessibility acceptance remain required. |
| Gantt and calendar | Real date-scaled bars, dependencies, critical path, baseline comparison, zoom and month/week/day views with all scheduled tasks. | Initial source and tabular export infrastructure do not establish graphical Gantt/calendar acceptance. |
| Collaboration | Task comments/threads, mentions, attachments, activity history, assigned follow-ups, reusable templates, checklists, recurring tasks and saved views. | Explicit backlog and functional tests required. Do not count raw comments text fields as threaded collaboration. |
| Customization | Custom fields/statuses/types, tags, sorting/filtering, grouping, grid formulas, cross-project reports and permission-aware dashboards. | Benchmark backlog; not yet implemented/validated by this change. Formula evaluation must be sandboxed and deterministic. |
| Automation | Condition/action rules, recurring work, approvals, due reminders and escalation with delivery history. | A saved preference is not proof of reminder delivery. Durable worker/outbox, deduplication, recipient authorization, timezone/quiet hours, cancellation after completion and retry/dead-letter behavior are required. |
| Customer portal | Explicit reviewed-baseline publication, project-specific artifact grants, expiration/revocation and audited read-only access. No internal notes, costs, credentials or private evidence leakage. | Test new links, expired/revoked links, cross-project IDs, disabled sharing and every artifact allowlist separately. A broad project-visible flag is insufficient. |
| Meeting recordings | Durable MP4 uploads/downloads, progress, cancellation, resumability or documented proxy/storage limit; explicit customer release per recording. | Initial module is not yet wired. Malware quarantine/scan verdict, MIME/container validation, quotas, streaming delivery, proxy limits, ACL and download audit are release blockers. |
| Transcription | Private approved transcription with timestamps, speaker uncertainty, language, status and recoverable failures. Proposed actions link to transcript evidence and require PM review before assignment/date commitments. | Endpoint configuration alone does not establish a worker. No "queued" claim without a durable runnable job. Unconfigured state must be honest. |
| Exports | Six individually selectable US Signal-branded XLSX and PDF artifacts: Timeline & Risk, RAID, Decision Matrix, Gantt, Monthly Calendar, detailed WBS. | Check real chart/calendar layout, full task details, correct typed dates/numbers, formulas where appropriate, print areas, pagination, logo and audience redaction. Tables relabeled as charts do not pass. |
| Enterprise operations | Backend role/project/customer/field scope, View-As read-only, optimistic concurrency, idempotency, audit retention, backups, restore, migration rollback and observable service readiness. | Negative authorization tests and recovery/load tests required before enabling the workspace. No capability may be called ready solely because its schema exists. |
| Integrations | Governed APIs/webhooks and existing identity, time, resource, calendar, notification and finance services remain authoritative. | Define owner, payload version, replay/idempotency, scope and failure handling for each integration. No direct uncontrolled provider calls. |

## Performance targets to measure, not advertise as achieved

- Request acknowledgement / durable run ID: p95 <= 2 seconds at agreed UAT load.
- First visible stage update: <= 5 seconds. Stage changes must reflect actual work, not a synthetic countdown.
- Representative already-indexed SOW of up to 20 pages / 100 tasks: initial target p95 <= 120 seconds to validated draft. Record provider/model, hardware, input/output size, queue time and sample count.
- Cold document scan/OCR/indexing is separately measured and visible; do not disguise it as model latency.
- Reopening a saved 1,000-task project: target usable view <= 2 seconds at agreed UAT load; virtualize large grids and do not fetch entire portfolios repeatedly.
- Browser navigation/reload must not create another AI job, lose completed phases, apply an old project response to the newly selected project, or overwrite edits made after the run started.
- No speculative shortcut may weaken evidence, authorization, scan, financial or customer-publication gates.

## Required end-to-end acceptance sequence

1. Start from an authorized PM and an existing project/SOW. Confirm no second SOW upload and no change to the source module.
2. Generate and read back specific WBS tasks in all five phases, with exactly one representation per task and no automatic milestones.
3. Change dates, dependencies, estimates, assignments and statuses. Recalculate, save, reload and compare all views and exports. Test an impossible finish date and show the overrun honestly.
4. Approve/adopt a controlled plan, record authorized actual time and expenses, and reconcile hours/cost/forecast without double counting or hidden overruns.
5. Add/edit/close/archive an issue and review its complete immutable event history. Exercise decisions and linked actions.
6. Deliver due and overdue reminders to an isolated UAT notification sink; verify no duplicate or post-completion alerts and no outbound production/customer messages.
7. Scan a meeting recording, test upload/download interruption and resume, process a transcript, and review action proposals without automatically treating uncertain text as assigned work.
8. Publish only an explicitly reviewed baseline and selected safe recordings/artifacts. Test access, deny cases, revocation, expiry and redaction from an unauthenticated customer browser.
9. Inspect all twelve requested branded exports visually and validate their underlying data and formulas. Spreadsheet data beginning with formula characters must not become executable formulas unless authored by the renderer.
10. Run concurrent edits, stale AI completion, revoked permissions, provider refusal/outage, unavailable financial sources, storage failure, worker restart and rollback tests.

## Release rule

The revamp stays in draft until functional and negative-path evidence exists. A passing unit test, healthy API, schema migration or successful deployment does not prove enterprise acceptance. No production deployment, reminder delivery to real customers, customer publication or parity declaration is authorized by this document.
