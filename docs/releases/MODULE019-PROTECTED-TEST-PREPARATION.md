# Module 019 Protected Test preparation

Status: **user authorized pushing the prepared change**. The current session is proceeding through fresh CI and the existing Protected Test release process. This document and a successful scope test do not independently grant deployment authority or authorize cancelling another run.

## Candidate and intended outcome

- Repository: `ahmedadeyemi-cts/project-time-platform`.
- Existing draft PR: [#1090](https://github.com/ahmedadeyemi-cts/project-time-platform/pull/1090).
- Remote PR branch: `codex/module019-project-focused-workspace`.
- Audited remote feature head: `3c8462a410fa24691870112bb0b8a2ef452bb720`.
- Main incorporated into this local preparation: `9557e1ce52ac2f7781f9deb535698b635e60b253` (includes PRs #1091–#1094).
- Local preparation branch: `codex/module019-release-prep`. Its final SHA is available from `git rev-parse HEAD` in the preparation worktree. Push and release status is recorded on PR #1090.
- Target after authorization: Protected UAT Test, `https://phd-west-test.onenecklab.com/#project-workspace`.

This is a UAT candidate for the simplified engineer/lead workspace. One selection controls tasks, teammates, personal/project hours, documents, and permitted cost/invoice evidence. Full financial attribution is not part of its acceptance claim.

## Exact proposed scope

The 13 feature files are frozen byte-for-byte to the audited remote PR head. Three additional preparation files bring the proposed diff from the incorporated main base to 16 files:

1. `scripts/release-test/validate-protected-test-controller-branches.sh`: a read-only branch/PR registration for this exact Module 019 set.
2. `tests/module019-release-scope.mjs`: checks the main base, existing PR ancestry, exact file set, frozen feature content, and unchanged deployment/approval authority. Negative cases reject missing/extra files, wildcard registration, and removal of the PR binding or a fail-closed check.
3. This release preparation document.

The validator contains the full sorted file list. The registration is included in the user-authorized candidate and must pass fresh CI. It does not approve a deployment, create a new deployment lane, admit arbitrary files, rewrite another feature's candidate manifest, or skip a failed test. All pre-existing branch validations and deployment workflow contents must remain identical to the incorporated main base.

The source update adds no migration, storage relocation, private-runtime configuration, cost-rate permission, invoice permission, or Production change. The existing Protected Test controller still performs its normal baseline checks and migrations; “no new Module 019 migration” does not mean its deployment job skips all migration steps.

## Validation evidence and refresh requirements

At the audited remote head, 12 of 13 pull-request workflows passed. This includes the complete frontend/API build, Module 019 browser/model/SQL behavior, migration and document contracts, security, assignment propagation, Group 3, and runtime navigation. The remaining failure was the release-scope rejection in [run 35374678095](https://github.com/ahmedadeyemi-cts/project-time-platform/actions/runs/35374678095).

The local preparation must pass:

```sh
GITHUB_HEAD_REF=codex/module019-project-focused-workspace PR_NUMBER=1090 node tests/module019-release-scope.mjs
node --test tests/module019-workspace-model.test.mjs
node tests/module019-workspace-sql.test.mjs
node tests/module019-workspace-access.test.mjs
node tests/module019-workspace-browser.mjs
node src/frontend/project-time-web/scripts/validate-module-loading-assignment-propagation.mjs
node tests/validate-systemwide-image-build-controller.mjs
bash tests/test-module019-document-access-repair-085.sh
```

Run the full frontend build from `src/frontend/project-time-web`, and exercise the existing governed-controller CI shell checks using the proposed PR branch/number. PGlite and Playwright paths are supplied by the existing Module 019 behavior workflow. Local browser executables may use `MODULE019_CHROMIUM_PATH`.

Local results are preparation evidence. They do not replace fresh GitHub CI for the new pushed SHA, a fresh .NET build of the merged candidate, or actual-account UAT. If main, the feature head, registration, or file set changes, refresh the proposal and rerun the affected checks before release.

### Completed local validation

- Full frontend build, including its existing gates: passed.
- Seven presentation-model tests, eight hours/allocation SQL assertions, and 93 role/access SQL assertions: passed.
- Desktop/mobile browser scenarios, including denied billing, expired sessions, missing files/totals, and linked intake documents: passed.
- Proposed exact-scope registration and its negative cases: passed.
- The existing governed-controller scope and contract shell steps, run locally in PR mode for #1090: passed.
- Assignment propagation, image-controller, system-wide reliability, utilization scope, deployment concurrency (repository and self-test), query-shape, document-repair, and security checks: passed.
- The new merged candidate has not had remote CI, a fresh .NET compile, or Docker-backed migration tests. Those require the authorized push and GitHub runners. The previously audited feature head passed its backend and migration jobs.
- No actual-account UAT was run against these unpublished changes.

## Sequence after the user gives the go-ahead

1. Read the current PR head, main head, and Protected Test deployment state again. Confirm the remote PR still points to the audited feature head before pushing this prepared history. Preserve newer concurrent work if it has moved.
2. Review the 16-file diff against the refreshed main base. Push the held commits to the existing PR branch only after authorization. Keep the PR in draft until the new SHA has passing feature and release-scope checks and the proposed scope registration has been reviewed.
3. Merge through the existing PR process when authorized and all required checks pass. Do not use a force push, administrator bypass, or a workflow exception to clear a failed gate.
4. Resolve the resulting exact current main SHA. The existing deployment workflow accepts merged main; it does not accept this feature branch as a new deployment lane. Inspect all changes since the installed Test source, since the deployment updates the complete API and frontend and may include other newly merged work.
5. Check Test health, pending/active deployments, environment approvals, and rollback evidence. Do not cancel, replace, or start a competing deployment without authorization. Historic queued runs were still reported during preparation; their state must be resolved through the existing release process before scheduling this change.
6. Use the existing `projectpulse-deploy-test.yml` workflow **from main**, only after authorization, with the inputs below. Do not dispatch this preparation document or use the old feature SHA as `release_sha`.

| Input | Value after authorization |
| --- | --- |
| Workflow ref | `main` |
| `release_branch` | `main` |
| `release_sha` | Exact current merged main SHA verified immediately before dispatch |
| `acceptance_scope` | `full` |
| `qualification_provider` | `none` |
| `recover_private_runtime` | `false` |
| `admission_controller_sha` | Empty for the merged-main lane |

`full` retains the existing system-wide acceptance process; `sow_role` is not the appropriate acceptance claim for Module 019. No custom deployment controller or new auto-dispatch is introduced.

## Installed acceptance

Capture the Test source SHA, API/web immutable image digests, workflow run/attempt, and test results. Use approved secure sign-in; do not put credentials, session tokens, invoice details, or document contents in the PR or logs.

| Persona/scenario | Acceptance evidence |
| --- | --- |
| Engineer with several projects and a shared project | Selector contains only authorized active work; personal tasks/hours match authoritative records; another assigned engineer is named; project totals include all accepted time states without double counting |
| Engineering lead with own and additional teams | Permitted projects, requests, document lists, and downloads agree; an unrelated project/document remains denied |
| PM/coordinator/admin where Module 019 is enabled | Correct role navigation and scope; no stale details after View-As changes; invoice history remains disabled in View-As |
| Real SOW/GSD and intake-linked files | View/download the correct file; confirm file content/size against the authoritative stored document; missing files produce a useful error; same-name versions remain distinct |
| Historical/taskless time and over-allocation | Reconcile total time against the source, including former engineers and pending approval; declined/voided time is excluded; allocation excess is explained without claiming a verified financial overrun |
| Standalone service request and unassigned user | Request files remain available; unknown allocated/logged time is explicit; an unassigned user has a genuine empty state |
| Cost/billing | Permitted invoices match source number/date/status/amount; restricted or failed reads are unavailable rather than zero; hours are not labeled billed or paid |
| Mobile, keyboard, and supported browsers | Select/search, tabs, downloads, modal close/focus, and PDF/native-file behavior work without page overflow |
| Expired session and failed reads | Prior data clears; Retry works; failed loading is not reported as no assigned projects; cost-source failure preserves core project information |

The existing billing predicate does not cover all team-only lead or service-request-only engineer access. Verified internal cost per engineer, invoice currency, current estimate to complete, and effective-user billing preview remain open. These limitations must be understood during UAT; they are not silently waived by deployment preparation.

## Rollback and stop conditions

The existing controller snapshots the installed API/web image digests, source SHA, environment references, and volume mounts before mutation. Release API and frontend together. If deployment or acceptance fails, use that controller's recorded rollback contract to restore the previous Test image pair and verify health/source identity. Do not guess image tags or replace upload storage. Existing migration effects may remain applied; this feature introduces no new migration to reverse.

Stop for failed required CI, changed/unreviewed scope, a different main SHA, unhealthy Test, unresolved deployment ownership, missing rollback evidence, unexpected document access, mismatched time totals, or stale cross-user data. Record the failure and keep the candidate unapproved for broader rollout. Production promotion is outside this preparation.
