# Enterprise module repair review: September 19, 2026

## Status

**Draft implementation; not all requested issues are fixed.** Validated against main `7cd57991` (including merged PR #1108). No merge, deployment, live inference, emails, invoices, or customer data changes. Module 018 changes were merged into this repair branch without conflicts.

The full frontend validation/build and three draft-write queue tests passed. Backend compilation, database authorization, real-role browser acceptance, provider health and email delivery remain unverified. This workspace has no .NET SDK. The connected Protected UAT browser displays the sign-in page; previous chat credentials are not an authenticated session here.

## Complete request checklist

| Area | Code changes / findings | Remaining acceptance |
| --- | --- | --- |
| Time autosave | Added 1.2-second editing debounce, serialized writes, queued-write identity/week cancellation, saved/failure feedback, unload warning, and submit/save waiting for drafts. Background responses do not rehydrate current typing. Failed save or continued typing keeps the popup open on Close. Generated application still permits incomplete draft descriptions; submission validation remains. | Persist/reload, deletion, offline/retry, rapid edits/submission, week/user change, immutable submitted days, engineer and PM. |
| Time popup | Compact header, persistent actions/footer, readable description area, two-column location fields, mobile collapse and live save status. | Signed-in desktop/mobile, dark mode and keyboard/focus acceptance against supplied screenshot. |
| Submission email | Existing scanner already queues submitter confirmation and manager approval request. Added PTC CC via the existing server-derived timesheet recipient strategy. | Worker/policy enabled, actual submitter/manager/PTC delivery, retries, deduplication. No email sent. |
| 020 | Currently preserves upstream request evidence, aging, task/resource handoff. 055D creates projects; 055C maintains them. No repurpose in this draft. | Proposed purpose and dependency migration below. |
| 021 to 026 | Fixed incorrect `#crm-erp-integrations` link to registered `#crm-integration`. | Confirm one Module 026 workspace in installed app. |
| 021 source control | Embedded React-owned control instead of global floating panel. Authoritative session-aware API, session-ready retry, View-As invalidation, stale response protection and Retry action. | Manual/SELL/other CRM selection, persistence, denied roles, expired sessions. |
| 022 | Added clear action guidance and forecast explanation. Existing filters and alert workflow retained. | Broader visual redesign and real-project UX acceptance remain. |
| 024 / 027 / 028 | Current purposes documented below. No data retirement. | Decide normal navigation after intake simplification. |
| 029 | Browser smoke checks now require JSON, reject explicit not-ready/degraded/error state, show authorization/API diagnostics, and time out after 15 seconds. Removed `/health` because it can test the web tier instead of the API. | Signed-in endpoints and readiness payload acceptance. Not a replacement for release UAT. |
| Overbudget | Shared service uses average billing rates or budget-derived rate, not verified internal labor cost. Missing expense budget no longer becomes zero in total budget/variance. Unhealthy required sources withhold forecast/committed/variance. API identifies forecast estimate authority. | Full project-cost reconciliation remains; see caveats below. |
| 031 / 032 / 040 | Standalone pages excluded from legacy fallback. Dashboard/timesheet/utilization panels, role admin and workflow cards now have explicit route visibility. | Installed browser proof of no unrelated modules or lost controls. |
| 036 | Removed duplicate legacy intake/customer dashboard and its reads. One selected-project workspace with search, status and leader AE dropdown. Backend includes owned/assigned projects and active reporting hierarchy for leaders. Identity changes discard delayed reads. | Unrelated/assigned AE, sales manager/director/executive, View-As, negative access, document endpoints, correct reporting hierarchy and portfolios beyond 250 projects. |
| 038 | Common route isolation prevents background legacy panels/cards/role admin under Certify. | Certify content, source errors and page-height acceptance. No sync/credentials changed. |
| 039 | Removed full legacy intake-overview dependency. Retains workspace, customers, Certify and server billing candidates. Removed build-time reinjection of Module 020 label. | 055C/055D project coverage, candidate completeness and genuine blocker preservation. |
| 040 integration | Focus repaired; genuine server closeout/billing blockers retained. | Trace actual remaining blockers. This PR does not simply clear them. |
| 042 | Existing SELL Quote, Certinia ID and Salesforce ID preserved; SELL/Salesforce default visible. Removed build-time masking/renaming of SELL. Three-step invoice guidance; recovery controls collapsed. | Saved column preferences, partial/final creation, immutable artifacts, export, duplicates. No new separate Salesforce quote field added. |
| 055B | Collapsed project-financial context to foreground rate controls. | SELL/manual rate editing/import and broader simplification. Collapsed content still loads at mount. |
| 060 | ENGINEER/ENGINEERING added to backend read role sets and navigation metadata; route removes unrelated customer/report permission gate. Write authorities unchanged. | Published scoped RBAC may still deny access. Inspect/version read grants without bypassing explicit denies. Verify all balances and negative writes. |
| 064 Celar | Dedicated server-verified availability, timestamp/failure code and separate document-runtime readiness. Optional consumer/knowledge failure no longer discards route editor. Selecting a provider swaps positions; local fallback remains final. | Live probe freshness, skip behavior, storage repair and usage reconciliation. No live runtime repair claimed. |
| 064 order | Root cause: protected release manifest pins routing; backend rejects mutations even for Super Admin. Enabling only UI would not change runtime order. | **Not fixed:** versioned admin routing policy compatible with active-release attestation, candidate immutability and privacy/refusal boundaries. |
| Gemini / Copilot | No fake operational targets or unsupported credential controls added. | **Not implemented:** Gemini adapter/health/usage/secret contracts; identify intended Microsoft Copilot service and supported integration. |
| 065 | Actual Super Administrator can test any syntactically valid recipient. Other admins keep self/allowlist. Same-origin, View-As denial, Test-only, confirmation phrase and durable evidence retained. Audit identifies Super Admin recipient authority. | Actual permission/readiness and explicitly addressed delivery test; received-mail confirmation; top spacing after route repairs. No mail sent. |

## Unclear modules and recommended simplification

- **020:** Pre-project intake evidence, aging, task preparation and resource handoff, not the canonical project record.
- **024 Sales Intake:** Opportunity/customer request plus proposal, quote, SOW/GSD and other evidence before delivery acceptance.
- **027 Signed Handoff:** Signed package transition requiring SOW, retaining evidence and requesting PTC notification through Module 065.
- **028 AI Time Entry:** Standalone customer-facing description assistant; does not submit time or change hours. Overlaps the timesheet assistant.

Suggested flow: 024 pre-sale intake, 027 signed package, 055D project creation, 055C maintenance. Consider removing 028 from normal navigation after consolidating its assistance into time entry, preserving historical links.

A useful potential purpose for 020 is a **Project Financial Action Center**: owner, corrective action, due date, change order, and resolution evidence for project financial risk. Module 022 detects/presents alerts; 020 would manage follow-up. This is a proposal, not an implemented repurpose. Existing 024/027 intake persistence must be mapped before retiring upstream logic.

## Financial audit

Existing formulas: planned hours from assignments; used hours from non-declined time; remaining = max(planned minus used, 0). Estimated labor uses average positive governed hourly billing rates, or labor budget divided by planned hours. Forecast = (used + remaining) times that rate, plus recorded project expenses. Budget = labor + expense budget, now requiring both. Variance = budget minus forecast; negative is forecast overrun.

Billing rates are not necessarily actual internal labor costs. Averaging does not honor person/time-effective rate assignment. A budget-derived rate is circular for validating budget performance. `planned_total_project_cost` currently participates as a labor-budget fallback; determine whether it already includes expenses to avoid double counting. Reconcile engineer/PM costs, expenses, approved change orders, commitments and remaining estimates before declaring system-wide overbudget correct. This draft does not fabricate cost data or rewrite financial history.

## Verification

- `node --test tests/timesheet-draft-writer.test.mjs`: 3 passed (slow-write order/submission, failure/retry, identity/week cancellation).
- `npm run build -- --configLoader native`: complete source validation and production bundle passed, including compiled FlowHive/Work Register contracts. Native loader accommodates symlinked dependencies here; no build gate removed.
- `git diff --check`: passed.
- Backend build/database/integration and signed-in visual acceptance: pending.

## Before ready for review

Complete unresolved AI policy/adapters, inspect Module 060 published permissions, run backend CI and real-role negative tests, authenticate Protected UAT securely, compare installed version with candidate, verify every row above and reconcile financial-source limitations. Keep the PR draft until these are resolved or explicitly separated in scope. Deployment is separate.

## Current-main validation and release decision

September 19: merged main `7cd57991` locally. Full frontend production build and three queue tests pass; `git diff --check` passes. Release remains blocked: application-level autosave week navigation/submit race coverage is missing; backend compilation, effective contract-read permissions, signed-in role acceptance, AI routing authority/adapters and complete cost reconciliation remain unresolved. No merge to main or Protected UAT deployment is approved by these results. Local Git push could not authenticate; attempting publication through the connected GitHub app for CI review only.

## CI repair follow-up

API Release compilation and frontend production builds passed in GitHub Actions for c7598981. Source-stability failures came from the tracked generated financial service; it is now synchronized. Added exact package registration for the existing release-scope validators, preserving deployment controls. Seven autosave tests now exercise the real application handlers as well as serialization: newer typing retains the popup, offline saves retain the selected week, submission locks before waiting, other days save before day submission refresh, and identity changes cancel waiting submissions. Full frontend build passes after these fixes. This evidence does not resolve the remaining AI integration, cost-model or signed-in acceptance items above.
