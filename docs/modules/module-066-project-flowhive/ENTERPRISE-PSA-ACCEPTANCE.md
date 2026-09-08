# FlowHive enterprise PSA acceptance contract

Date: 2026-09-08
Status: implementation in progress; NOT a deployment, live-acceptance or product-parity declaration.
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

## Single completion register

This table is the authoritative completion register for the enterprise PSA scope. A
passing build, schema migration, synthetic browser fixture or healthy endpoint is
not a live acceptance result. `Candidate` identifies the current application
revision; `Deployed revision` remains `None` until the canonical Protected UAT/Test
controller has completed and the deployed API and web image digests have been
recorded. The register must be updated in the same change that records each live
acceptance result.

Current candidate: `f0f1953545cbd00d51c317fb23b62dc04cf9355a`.

| Requirement | Implementation location | Automated evidence | Deployed revision | Live acceptance | Remaining blocker |
|---|---|---|---|---|---|
| Existing-SOW discovery and detailed five-phase AI WBS | `ProjectPlanningDocumentResolver.cs`, `ProjectFlowHiveAiPlannerOrchestrationModule.cs`, `ProjectFlowHiveExecutablePlanBuilder.cs`, `ProjectFlowHivePsaWorkspace.jsx` | `FlowHiveDetailedPlannerTests`, `FlowHiveExecutablePlanTests`, `flowhive-psa-react-browser.py`; CI FlowHive run `34246254087` passed `executable-wbs`, `execution-database`, `planner-browser` and `planner-layout` | None; candidate only | Not run. No live-provider or authorized existing-SOW result | Execute against the authorized existing SOW and prove Plan, Design, Implement, Validate and Release quality with no duplicate upload or customer-data leakage |
| Bounded, observable, measured AI execution without regeneration loops | `ProjectFlowHiveAiPlannerWorker.cs`, `ProjectFlowHiveExecutionPolicy.cs`, `ProjectFlowHiveAiRequestFactory.cs`, `flowhive-planner-operation.js` | 47 native scope/operation/admission tests passed; browser suites prove reload and recovery do not start another AI operation; CI execution and source-evidence jobs passed | None; candidate only | Not run. Synthetic fixtures are not live-provider evidence | Record real configured model, provider, latency, queue time, input/output size, stage evidence and one authorized generation |
| Anti-placeholder and representative-SOW quality | `ProjectFlowHiveDetailedPlanBuilder.cs`, `ProjectFlowHiveExecutionPolicy.cs`, `FlowHiveDetailedPlannerTests/Program.cs` | Native WBS and operation tests reject generic scaffolds, invalid dependencies, stale results and unsafe replacement; browser suite proves invalid results do not replace working copy | None; candidate only | Not run | Measure semantic coverage and task detail against a representative authorized SOW with PM acceptance |
| Milestone-preserving regeneration, explicit review, preview and safe application | `ProjectFlowHiveAiPlannerReviewModule.cs`, `ProjectFlowHivePlannerReview.cs`, `ProjectFlowHivePlannerReview.jsx`, `tests/flowhive-psa-review-browser.py` | Fresh reviewer suite passed `FLOWHIVE_REVIEW_BROWSER_ASSERTIONS_PASSED=107`, including local-edits and read-only permission assertions; full page suite passed | None; candidate only | Not run | Prove live proposal review, schedule preview, saved receipt, canonical adoption boundary, immutable history and unchanged milestone/task identities |
| Automatic dates/times, dependencies, calendars and resource scheduling | `ProjectFlowHiveScheduleEngine.cs`, `CalendarCapacityModule.cs`, `ProjectFlowHiveExecutablePlanBuilder.cs`, `docs/modules/module-066-project-flowhive/SCHEDULE-ENGINE.md` | Executable WBS and planner layout checks passed; migration and scope checks passed | None; candidate only | Not run | Validate holidays, leave, resource leveling, intraday time, timezone authority, impossible dates and real resource availability |
| Unified editable WBS, Kanban, timeline, Gantt and monthly calendar | `ProjectFlowHivePsaWorkspace.jsx`, `ProjectFlowHiveCenter.jsx`, `project-forge/ProjectForgeViews.jsx`, `project-flowhive-planner-layout.css` | Planner browser/layout checks and full page browser suite passed the implemented planner path | None; candidate only | Not run | Prove one authoritative editable task graph across all views, including persistence, accessibility, concurrent edits, real graphical Gantt and calendar behavior |
| Canonical task adoption and FlowHive/Project Forge consistency | `PostgresProjectFlowHivePlanRepository.cs`, `ProjectForgeFlowHiveSyncPortal.jsx`, `ProjectForgeInteractiveModule.cs`, `ProjectFlowHivePlanningContracts.cs` | Scope/admission tests reject stale project, source and revision adoption; shared project-document planning and ProjectPulse checks passed | None; candidate only | Not run | Prove stable planning IDs map idempotently to canonical tasks and assignments without duplicating tasks or time records |
| Authoritative project hours and forecasts | `ProjectFinancialTruthModule.cs`, `FinancialOperationsContracts.cs`, `FinancialOperationsSourceLoader.cs`, `UnifiedProjectFinancialWorkspace.jsx` | System-wide reliability and financial-operation validation checks passed for existing authority boundaries | None; candidate only | Not run | Reconcile approved budget, actual time, estimate-to-complete and forecast with authoritative timesheets; restricted values must remain unknown, not zero |
| Authoritative costs, revenue, commitments and margin | `FinancialOperationsReportEngine.cs`, `FinancialOperationsRepository.cs`, `ProjectFinancialTruthReportingBridge.cs`, `FinancialOperationsRecoveryWorkspace.jsx` | Financial source and reconciliation validators passed where applicable | None; candidate only | Not run | Prove currency/as-of semantics, no AI-generated rates, no revenue-to-cost fallback and no commitment double counting |
| Immutable RAID history and decision matrix | Existing project-control modules plus `ProjectPlanningCollaborationModule.cs`; decision UI and export paths remain to be mapped in the FlowHive register | Reliability, collaboration and shared-planning checks passed; no live append-only RAID/weighted-matrix receipt | None; candidate only | Not run | Implement and verify immutable actor/time/prior/new/reason history for RAID and weighted decision alternatives, approval and rationale |
| Collaboration and reviewed meeting action items | `ProjectPlanningCollaborationModule.cs`, `validate-project-planning-collaboration.mjs`, collaboration frontend modules | Collaboration and shared document planning CI checks passed | None; candidate only | Not run | Complete threaded comments, mentions, attachments, follow-ups, templates, checklists, recurring tasks and PM-reviewed meeting actions |
| Customization and enterprise work controls | Existing planning, permissions and workspace modules; FlowHive integration points require explicit mapping | Authorization, collaboration, scope and reliability validators passed | None; candidate only | Not run | Complete custom fields/statuses/types, tags, formulas, reports, dashboards, View-As and permission-aware behavior with deterministic sandboxing |
| Automation and isolated due/overdue reminders | `ProjectNotificationQuietHoursService.cs`, notification modules, `docs/69-personalization-holidays-reminders-plan.md` | Existing notification and authorization validators only; no live delivery receipt | None; candidate only | Not run | Deliver to an isolated UAT recipient, prove deduplication, quiet hours, cancellation, retry/dead-letter and no customer or Production notification |
| Secure customer sharing | `CustomerDeliveryAcceptanceModule.cs`, `ProjectFlowHivePsaArtifactRenderer.cs`, `docs/modules/module-066-project-flowhive/ARTIFACTS-AND-SHARING.md` | Customer-delivery authorization and source-boundary checks passed where applicable | None; candidate only | Not run | Prove reviewed-baseline publication, artifact grants, expiry/revocation, unauthenticated deny cases, redaction and access audit |
| MP4 recordings and selected customer downloads | `CustomerDeliveryAcceptanceModule.cs` and customer delivery artifacts; recording implementation is not yet wired to FlowHive | No end-to-end recording receipt | None; candidate only | Not run | Implement durable MP4 upload/download, scan verdict, MIME/container validation, progress, cancellation, resume, quotas, ACL and explicit release |
| Private transcription and reviewed action proposals | Customer delivery/transcription integration boundary; no durable FlowHive transcription worker is currently claimed | No live transcription worker receipt; endpoint configuration alone is insufficient | None; candidate only | Not run | Implement durable runnable job, timestamps, speaker uncertainty, language/status, recovery and PM review before assignment or date commitments |
| Six US Signal-branded XLSX/PDF exports | `ProjectFlowHivePsaArtifactRenderer.cs`, `ProjectFlowHiveArtifactRenderer.cs`, `AnalyticsBrandedExportBuilder.cs`, `Module025SowGsdDocumentExporter.cs` | Export/source validators passed where applicable; no twelve-artifact visual acceptance receipt | None; candidate only | Not run | Produce and visually inspect Timeline and Risk, RAID, Decision Matrix, Gantt, Monthly Calendar and detailed WBS in both XLSX and PDF with typed data, formulas, pagination, branding and redaction |
| Enterprise operations and release safety | FlowHive authorization modules, migrations `103`, `104`, `105`, rollback scripts, release-control workflows and `tests/flowhive-psa-admission.test.mjs` | Real PostgreSQL CI receipt: `FLOWHIVE_PSA_MIGRATIONS_103_104_105=APPLIED_AND_VERIFIED`, `PRODUCTION_MUTATION=NONE`; release-control PR880 checks pass | None; candidate only | Not run | Merge reviewed PR880, integrate current main, then prove deployed image identity, rollback, backups/restore, concurrency, audit retention and readiness in Protected UAT/Test |
| Governed integrations and authoritative identity/time/resource/calendar/finance services | `IntegrationEventGatewayModule.cs`, `MicrosoftIntegrationModule.cs`, `CrmErpIntegrationModule.cs`, `CalendarCapacityModule.cs` | Integration and collaboration validators passed for existing boundaries | None; candidate only | Not run | Define owner, versioned payload, replay/idempotency, scope, failure handling and live receipts for every integration; no uncontrolled provider calls |
| Performance and measured acceptance targets | `docs/modules/module-066-project-flowhive/BOUNDED-PLANNER-VALIDATION.md`, `SCHEDULE-ENGINE.md`, operation and browser tests | Deterministic tests pass; no agreed-load p95 or real-provider measurement has been recorded | None; candidate only | Not run | Measure acknowledgement, first stage, representative-SOW completion, reload and 1,000-task usability at agreed UAT load without weakening evidence or authorization |

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
