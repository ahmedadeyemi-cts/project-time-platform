# FlowHive and Approval predeployment review — 2026-09-29

Release decision: **not ready for deployment**. This is a partial authenticated UAT review, not a certification of every workflow. PR #1217 remains a draft.

## Environment and boundaries

- UAT: `phd-west-test.onenecklab.com`, authenticated local administrator, no View-As active.
- Refreshed the previously open frontend before mutation tests; refreshed asset was `index-CEvimG6R.js`.
- Session Intelligence displayed API revision `ca-phd-test-api-westus3--m025d-36629856182-1` and source label `045d17949e72c1b1623ff1f1ec33139efafece7c`. These are observed labels, not proof that the installed application matches the PR. Candidate acceptance must independently reconcile installed provenance.
- AlloSource project `0ea25cb8-1a7f-4baf-ba7b-2dd76215be49` was inspected without saving changes. Mutation tests used Demo Operations Readiness (`2a733955-1456-4851-979d-01524e9ac1e3`) with clearly synthetic values.
- No time-approval confirmation, customer link, reviewed baseline, invitation, Teams meeting, or access grant was issued.

## Live observations

| Workflow | Result | Evidence / limit |
| --- | --- | --- |
| Portfolio and selected-project loading | Passed | Projects and scoped delivery teams loaded. |
| AlloSource WBS readback | Passed | Working-copy revision 74, 21 executable tasks, phase date rollups. |
| AlloSource schedule and critical path | Passed | Calculate schedule displayed July 14–October 8, 63 working days and 21 critical tasks. This observation preceded the frontend refresh. |
| Demo WBS creation | Passed locally in UI | Five-phase draft created; no working copy existed. |
| Task finish editing | Failed | Selecting July 1 cleared the displayed finish immediately. PR contains a retention/recalculation fix, not yet installed. |
| Demo working-copy save | Failed | Same root `$` validation message as reported; reference `188385bf-9d74-428c-a80b-b1131871cc04`. No saved working copy reported. |
| Demo schedule calculation | Passed | The edited synthetic draft calculated a schedule after the failed save. |
| Customer-info save | Failed | Synthetic name/email and optional blank phone/title returned generic validation. Diagnostic `PP-1B420015`; contact count remained 0. |
| Financial-controls save | Failed | Synthetic notes with optional amounts blank returned HTTP 400. Rendered diagnostic identified `/api/project-flowhive/projects/2a733955-1456-4851-979d-01524e9ac1e3/controls`. |
| RAID create | Passed | One synthetic risk appeared in the register. Retained test record: `UAT QA synthetic risk 2026-09-29`. No cleanup deletion was attempted. |
| Meeting draft | Inconclusive | Form exercised with synthetic title, dates and Demo Manager attendee. No saved draft was visibly confirmed; do not count as passed or as a diagnosed 400. No invitation action exists in this tested flow. |
| Approval current-week queue | Passed for read | No current-week units; review disabled when selection empty. |
| September month queue | Passed for read/select | One authorized manager unit, eight hours; select-all selected exactly that unit. |
| Approval preview | Passed to confirmation | Review selected approvals opened explicit Confirm approval / Keep reviewing controls. Confirm approval was never clicked. |
| Approval preview cancellation / PM stage | Unverified | Browser connection timed out while leaving the preview; final page state could not be read. |

The browser control service subsequently timed out on state reads and its documentation interface. Consequently live role switching, mobile/theme review, full reload persistence, version/baseline/customer-sharing completion and approval write-path tests were not completed.

## Changes and automated follow-up

- Preserve the PR's date-retention, autosave concurrency/conflict, contact parsing and compact Pulse-themed approval changes.
- Pause working-copy saves while a manual or automatic AI operation is active. Otherwise the new autosave can change the expected row version while generation is running. Resume autosave after a proposal completes without adopting its tasks.
- Show when saving waits for AI, and retain a support correlation reference for non-JSON HTTP failures.
- Extend the real React regression to keep AI active beyond the autosave debounce and verify that the original work and milestone survive proposal generation, later autosave and reload.
- Update the database date regression to assert the requested UTC creation-date fallback, explicit-start precedence and continued evidence-readiness enforcement.
- Add actual security-middleware / typed-binding checks for browser-shaped contact and financial-control DTOs. These distinguish DTO compatibility from the still-unresolved installed failures.

## Remaining release gates

1. Identify the installed request-path cause of the working-copy, contact and controls failures; parser hardening alone is not proof of resolution.
2. Retest successful saves and reloads against the exact candidate, including blank optional fields, stale versions and cross-project switching.
3. Verify version → review → baseline → governed sharing using an isolated fixture; retain authorization and self-approval protections.
4. Complete Manager, PM/coordinator and PTC role coverage, View-As read-only checks and preview cancellation. Use synthetic time for approval mutations.
5. Require current CI and protected release admission. The previous head had FlowHive browser/database failures plus existing admission/scope failures. No scope allowlist or release protection is relaxed by this review.

The PR was reconciled with main `9796b4cb` during this follow-up. No deployment was started.

## Follow-up — 2026-09-30 UTC

- PR head at inspection: `435931f720b5ad77baea9f19e6c180fec523071c`, still draft.
- Browser service responds again, but Pulse displays sign-in; the earlier authenticated session is unavailable. No new live mutation or acceptance claim was made.
- Diagnosed three failed jobs: governed scope rejects the unregistered PR inventory; admission contracts send this application branch through a historical control-only check; frontend fails three historical admission fixture assertions against newer main.
- Register this PR's exact inventory, hashes, branch, PR number and fixed main baseline using the existing repository pattern. Deployment controllers, admission runtime, candidate/approval records, migrations and Production authority remain frozen.
- Reuse the historical fixture runner with all assertions retained and source-blob verification. It supplies no current-candidate or installed acceptance evidence.
- Incorporated merged security follow-up PR #1218 without conflicts.
- Latest three protected deployment runs were complete when inspected; no deployment was dispatched.
- All five remaining release gates above still apply, including authenticated working-copy/contact/financial-control persistence, role/self-approval and baseline/sharing verification.
