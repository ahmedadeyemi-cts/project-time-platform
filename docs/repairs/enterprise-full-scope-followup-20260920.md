# Full module repair acceptance ledger

Base: main `fc10591e4550243603b84af1a42153634228972d`, including PRs 1111–1113.
This follow-up is not a declaration that the complete requested release is ready.
PR 1111's successful deployment acceptance covered SOW exports, not all these workflows.

## Coverage and completion criteria

| Request | Current source evidence | Outstanding acceptance / work |
| --- | --- | --- |
| Time autosave and popup | PR1111 debounce, serialized persistence, failed-close protection, identity/week guards, compact popup. Seven executable handler/queue tests pass. | Signed-in desktop/mobile persistence and submitted-day immutability. |
| Submission email to engineer/PM, manager, PTC | Existing submitter confirmation and manager notification retained. This follow-up replaces all-PTC CC with the active coordinator for each project on the submitted day. | Enabled policies and actual three-recipient delivery/deduplication. Administrative days have no project coordinator. |
| 020 purpose and reuse | On-page explanation distinguishes historical intake evidence from canonical 055D creation and 055C maintenance. | Recommended possible repurpose: Scope & Budget Change Control, tracking requested scope changes, cost impact, approvals and approved baseline revisions. Another alert/action queue would duplicate 022 and 031. Existing intake evidence is retained. No repurpose is falsely claimed. |
| AI priority and Celar visibility | PR1111 health display and optional-endpoint resilience retained. Separate active worktree `fix/enterprise-completion-20260919` contains unpublished AI governance/providers/Teams changes. | Integrate reviewed committed work, test actual SuperAdmin save/reload and runtime selection. A disabled release-managed editor is not a completed fix. |
| 021 → 026, embedded SELL, CRM source/session | PR1111 route and embedded source control retained; follow-up invalidates delayed save/preview/import responses and reload callbacks on identity change. | Installed source selection/persistence, failed-session recovery and negative role tests. |
| 022 usability | Search/status filter and one selected project review replace expansion of every project; routing settings move behind an optional disclosure and load only when opened; source errors and existing acknowledgement/resolution remain. Calculation explanation exposes estimate limitations. | Visual acceptance with real projects; verify action persistence and scope. |
| 024 / 027 / 028 purposes | On-page purpose and next-step guidance added to each module. | Confirm workflow links in installed application. |
| 029 API errors | Uses the authoritative session-aware API transport, bounded timeout, identity invalidation, honest API readiness results. | Authenticated API/permission readiness. Browser checks are not release approval. |
| Systemwide overbudget | Shared budget classification uses 85%; requires both labor and expense budgets; missing values remain unknown; historical time survives ended/unassigned contributors in notification calculations; declined time excluded consistently; non-USD expenses require conversion rather than being summed as USD. | Full actual cost reconciliation remains incomplete: effective internal rates, PM/engineering costs, purchase commitments, approved changes and remaining-work estimates need authoritative sources. Billing-rate forecasts remain estimates. |
| 031 / 032 / 038 / 040 focus | PR1111 route isolation retained. | Signed-in installed layout checks for each route. |
| 036 AE workspace and scope | PR1111 backend ownership/assignment/reporting hierarchy retained. Follow-up loads all authorized portfolio pages, preserving server scope and discarding stale identity pages. | AE own/assigned/unrelated, leader dropdown, direct detail/document negative tests, and real-role UAT. |
| 039 / 040 dependencies | PR1111 removed frontend Module020 dependency; current billing/closeout source search finds no legacy intake table dependency. | Trace live 055C/055D projects and genuine remaining server blockers. |
| 042 invoice workflow and references | Follow-up labels choose/review/generate stages, keeps SELL/Certinia/Salesforce references visible in selected project independently of saved columns, reduces default columns, collapses advanced source/output controls and adds keyboard project selection. | Saved invoice, partial/final prerequisites, history and downloads. Salesforce ID/Quote displays the existing Salesforce reference; no new quote field is claimed. |
| 055B rate simplicity | SELL/manual foreground, rate-selection rules collapsed, optional financial workspace mounts only when opened. | SELL/manual source authority and save/import tests. |
| 060 engineer read-only contracts | PR1111 read roles retained. Follow-up removes job-title/department-based management grants: an engineer titled Systems Administrator does not gain write access. | Published RBAC grants, all-contract read coverage and every mutation denied to engineer role. |
| 064 authoritative AI, Celar, Gemini, Copilot | Separate unpublished implementation identified and preserved. | Combined implementation/CI plus live health/usage, private storage/inference, priority and privacy refusal tests. |
| 065 mail tests, layout, Teams | PR1111 allows actual SuperAdmin arbitrary valid test addresses. Separate unpublished Teams implementation identified. | Integrate/review Teams storage/transport/routing; tenant configuration/permissions, explicit test address/channel, delivery receipt and top-space visual acceptance. |

## Answers about module purpose

- **020** keeps pre-project requests, original evidence, aging and resource handoff. It should not be a required read dependency for established project financials. A useful possible replacement is Scope & Budget Change Control: requested change, budget/schedule impact, approval and versioned baseline update. This needs a separate implemented workflow and historical intake migration; another financial action queue would duplicate Module031.
- **024 Sales Intake** captures a pre-sale customer request/opportunity and proposal/quote evidence.
- **027 Signed Handoff** packages the signed SOW and supporting files for delivery acceptance, ownership and project creation.
- **028** generates a reviewable time description. It neither sets hours nor submits time. The same assistance is available in the timesheet; this standalone entry can be retained for compatibility.

## Cost calculation findings

The historical `planned_total_project_cost` column is explicitly computed as engineering plus PM labor in migration `019m-ag-customer-directory-intake-cost-foundation.sql`. Despite its name, that source does not include travel; it must not be treated as a complete total when expense budget is missing.

Current rate-based forecast is `(logged hours + max(allocated hours - logged hours, 0)) × governed average billing rate + current expenses`. Budget is known labor plus known expenses. Variance is budget minus forecast. Both consumers now classify using the same 85% boundary and unknown-value rules. This improves consistency but does not convert billing rates into verified internal costs. The UI says so explicitly.

## Validation

- .NET 10 Release build passed with zero errors; existing repository warnings remain.
- Production frontend build and its source/compiled-route gates passed.
- Ten executable budget boundary cases passed, including missing components, exact 85%, zero budget and negative invalid budget.
- Eleven Node tests passed: seven existing real-handler/queue autosave tests and four new portfolio paging/failure/identity tests.
- Git whitespace check passed.
- No live email, Teams message, invoice, customer mutation, migration, merge, or deployment performed by this follow-up.

## Integration and release gate

Keep this work reviewable without copying another chat's uncommitted files. The active enterprise-completion branch overlaps financial source/paging changes as well as owning AI/Teams work; reconcile explicitly before merge. The full request is not complete until every outstanding workflow above is implemented and verified. Passing compilation or a SOW-only UAT run cannot close that gate.

## Authenticated installed-main findings (September 20)

An authenticated session became available during this work. In a separate browser tab:

- Module021 source selector loaded and was editable; Open Module026 navigated to the correct CRM workspace. SELL mapping is still unverified and synchronization disabled; no source setting was changed.
- Module065 non-delivery test passed Microsoft Graph authentication, sender mailbox resolution and Mail.Send application-role verification. Test-only boundary still suppresses normal delivery. No message sent.
- Module029 reproduced a non-JSON availability response. The backend GET route bindings discarded IResult; explicit result-returning delegates now fix this. Two real Kestrel HTTP tests verify 401 JSON without a session instead of empty successful responses. Diagnostic status labels avoid the global error presenter misclassifying HTTP200 text as a failure.
- Module031 still showed dashboard/status/role cards in the installed build. This follow-up now structurally gates dashboard cards and role administration and excludes standalone031/032/040/042 from the legacy fallback.
- Module065 had two 112px assistant/empty-host reservations before its detached portal. Scoped CSS removes that excess space before the content.

These observations describe installed main, not post-deployment acceptance of this PR. The new fixes require deployed verification.
