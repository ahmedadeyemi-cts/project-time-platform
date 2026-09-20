# Enterprise completion follow-up — September 20, 2026

## Scope and release status

Follow-up to PRs 1111–1113, based on main `fc10591e4550243603b84af1a42153634228972d`. This review supersedes the September 19 document's acceptance status. The code in this follow-up has not been deployed. The live observations below concern the existing Protected UAT release, not proof of the proposed fixes. This remains a draft until CI, role acceptance and external configuration are complete.

## Complete request matrix

| Request | Current evidence and follow-up changes | Remaining acceptance / configuration |
| --- | --- | --- |
| Engineering autosave and popup | PR 1111 includes debounced, serialized draft writes, save feedback, submission ordering and redesigned popup. Seven behavioral autosave tests pass. | Real engineer and PM: edit/reload, failed save recovery, rapid submission, keyboard/mobile and submitted-day immutability. No live time was changed during this audit. |
| Submission email to employee, manager and PTC | Existing scanner plus PR 1111's server-derived PTC recipients are present. Graph readiness passes. | Current UAT policy is OutboxOnly/TestOnly, with live scheduled delivery disabled. Verify an explicitly addressed email test and actual submission notifications once governed delivery is enabled. No email was sent in this review. |
| Module 020 | Still legacy pre-project intake/evidence. It is no longer Module 039's authoritative intake dependency. | Proposed repurpose: financial corrective actions, owners, due dates, change orders and resolution evidence. Module 022 would detect risk; 020 would track follow-up. Not repurposed in this patch because intake persistence needs a migration decision. |
| Module 021 / 026 | After a full reload, embedded CRM source selection is enabled; Open Module 026 lands on `#crm-integration`. | All four connector cards report Needs setup. SELL identity mapping/import is not ready. Credentials and verified mappings remain external configuration work. |
| Module 022 | Alert workbench now comes first, with one selected project and its financial/action details. Notification rule administration is collapsed below. Estimated cost is labeled as an estimate. | Real financial data and action workflow acceptance; portfolio remains limited to the first 250 authorized records on this screen. |
| Modules 024 / 027 / 028 | Purposes are explained below. | Workflow consolidation is a product decision, not a claim that these modules were removed. |
| Module 029 | Found successful HTTP 200 messages incorrectly rewritten into error banners by the shared error presenter. Restrict that handling to real HTTP failures. Two regression tests pass. | Run installed smoke checks after deployment. Passing these checks is not full UAT. |
| Overbudget | Removed total-project-cost as a labor-budget fallback (avoids adding expenses twice); unavailable required sources now withhold notification totals; notification threshold aligned to 85%. | Existing calculation uses governed billing-rate averages or a budget-derived rate. It is not verified internal actual cost. Reconcile person/effective-date rates, expenses, commitments and change orders before signing off accuracy. |
| Modules 031 / 032 / 038 / 040 | Live audit reproduced unrelated platform, role-admin and workflow panels beneath focused modules. Route visibility and fallback exclusions corrected. | Installed visual acceptance after deployment. |
| Module 036 | Live SuperAdmin AE selector loads. Existing backend ownership/assignment/reporting-hierarchy scope retained. New API pagination and client loading include authorized portfolios beyond 250 projects. | Verify actual AE, sales manager/director/executive identities and negative access; View-As alone is not sufficient proof. |
| Module 039 | Live data loads without the old Module 020 warning, with 055C, closeout and billing links. | Reconcile project completeness and genuine billing blockers using 055C/055D records. |
| Module 040 | Live remaining blocker is missing saved closeout confirmations/billing disposition, not Module 020. New UI avoids a misleading green state before project/lifecycle data loads. | Complete the required project workflow; do not remove server-validated blockers. |
| Module 042 | SELL Quote, Certinia and Salesforce ID/Quote references remain visible. Default table reduced from 15 to 10 columns; Essential columns resets saved preferences. Existing three-step invoice guidance retained. | Create/review a real test invoice, partial/final rules and artifacts. Salesforce ID/Quote uses the existing shared field, not a new independent quote-ID store. |
| Module 055B | Rates remain foreground; collapsed financial context now mounts only when opened. | SELL connection configuration, import and manual-rate edit acceptance. |
| Module 060 | Engineers' read roles were added in PR 1111. Fixed overview actor selection and stale View-As responses. Found a global layout rule hiding contract-header actions even for actual SuperAdmin; preserve interactive headers. | Verify actual engineer read access to all intended contracts and denied writes against published RBAC. View-As writes stay denied. |
| Module 064 admin routing | Decouple routing read-only status from an environment-managed private profile. Ordinary UAT SuperAdmin can persist private-profile overrides using revision checks and mandatory audit; old stored profiles do not silently override deployment settings. | Browser save/reload and concurrent revision tests after migration. Immutable release candidates and pinned releases retain their integrity restrictions. |
| Module 064 Celar health / usage | Live private-model probe returned Available with model/readiness phrase verified. Added explicitly labeled process-local usage display; inference availability and content-storage readiness remain separate. | Usage is not a durable billing ledger. Persistent storage blockers below are real infrastructure issues. |
| Gemini | Optional, disabled-by-default provider with server-side secret/model settings, adapter, routing, health and token usage. Regression tests cover response, refusal and truncated output. | Supply authorized Google API key and approved model; verify actual inference. Structured SOW phase is explicitly unsupported by this adapter. |
| Microsoft Copilot | Optional Copilot Studio agent through Direct Line, disabled by default; server-side credential, route and health controls. | Supply published agent/Direct Line secret and verify responses. This is not a Microsoft 365 Copilot chat API. Token usage is unavailable, not fabricated; structured SOW phase is unsupported. |
| Module 065 email and spacing | Actual SuperAdmin's arbitrary valid test-recipient authority already exists in PR 1111. Live Graph check confirms authentication, Mail.Send and sender resolution. Removed excess page padding. | Explicit recipient delivery and receipt remain untested. Test confirmation, same-origin and environment boundaries remain in force. |
| Module 065 Teams | Added environment-scoped enable/app-ID configuration, same-origin admin writes, revision/audit checks, explicit test, Graph activity-feed delivery and durable delivery outcomes. Hooks reuse server-derived notification recipients. | Teams app installation, consent, app ID and an explicitly addressed delivery test are required. Background notifications obey production-governed boundaries; Teams is disabled initially. No Teams message was sent. |

## What the unclear modules do

- **024 Sales Intake:** captures the pre-sale request and proposal/quote/SOW evidence.
- **027 Signed Handoff:** moves the signed package toward delivery and requests PTC notification.
- **028 AI Time Entry:** assists with customer-facing time-entry descriptions; it does not submit hours.

Recommended project flow remains 024 → 027 → 055D creation → 055C maintenance. Module 028 could be consolidated into the timesheet assistant in a separately reviewed navigation change.

## Confirmed live UAT configuration gaps

The browser initially retained an older application bundle. A full reload was necessary to see the deployed PR 1111 controls. Reloading does not fix the additional bugs addressed here.

Celar inference passed, but persistent private content storage reports an unverified/temporary upload root. Platform configuration must mount real shared persistent storage, set `PROJECTPULSE_UPLOAD_ROOT` to that mount, and set `PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT=true` only after verification. The legacy variable alone is not evidence. Automatic document admission is disabled and there are no authorized ready SOW/GSD records. This patch does not disguise these conditions as healthy.

Microsoft Graph readiness passes for the configured sender. Actual mode is OutboxOnly, recipient boundary TestOnly, and scheduled live delivery is disabled. A readiness result is not proof of receipt.

## Teams behavior and operational limits

Teams uses Microsoft Graph activity-feed notifications, not channel webhooks. Each intended recipient needs the application installed and appropriate application/RSC consent. Configuration uses the active Module 065 tenant credentials; the browser never receives secrets. Test delivery requires an actual admin, active Test profile, unlocked recipient boundary and the exact confirmation phrase. Only actual SuperAdmin can address another tenant user.

A durable unique claim is saved before sending. Failed or unknown Teams outcomes do not replay already finalized email. Unknown outcomes are not automatically retried. The integration is an initial delivery path: it does not include a separate background recovery worker or a retry dashboard. Graph acceptance still requires recipient-side confirmation.

## Validation and migration requirements

- .NET 10 API compilation and 16 provider/routing behavioral checks passed locally.
- Nine JavaScript checks passed: seven autosave/queue behaviors and two API error presenter cases.
- Full frontend production build and repository source/bundle validators passed locally.
- Governed image/deployment-controller validation passed.
- New CI runs migrations 112–114 twice against disposable PostgreSQL and checks opt-in defaults, constraints, preserved private-profile behavior and delivery uniqueness. Local PostgreSQL was unavailable, so no local database-test success is claimed.
- Protected Test's existing migration runner now includes 112–114 and verifies their schema evidence. Production initialization inventory includes their hashes with review-required status; this does not authorize production execution.

Before readiness: CI must pass; deploy the reviewed candidate through Protected UAT; verify each remaining acceptance item above; configure external services and persistent storage. This review does not certify every requested feature complete.

## CI follow-up

The dedicated enterprise completion CI passed provider/route behavior, autosave/error behavior and disposable PostgreSQL migration checks on the initial PR head. Legacy branch-specific source checks required a new exact PR 1116 registration. The registration pins the complete change inventory and hashes, rejects unlisted files/content changes, and retains production deployment/candidate manifests unchanged. It does not deploy this draft. Existing protected-environment, review and release admission requirements remain.
