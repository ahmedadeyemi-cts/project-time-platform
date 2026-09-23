# Protected UAT preparation and activation gates

This checklist is intentionally NOT marked complete by source tests or compilation.
Only the foundation library and presentation component are in the current PR.

## Before merging the foundation

- [ ] API compiles with the real Module064 bridge and existing build-generation steps.
- [ ] Runtime tests pass: deny defaults, Test-only, View-As, source/permission changes,
  unknown tools, bounds, duplicates, clarification, explicit interruption and handoffs.
- [ ] UI projection tests and component bundle pass; existing frontend builds unchanged.
- [ ] All other applicable final-head CI passes without disabling checks.
- [ ] Security review accepts the read/propose-only boundary and conservative private context.
- [ ] Business owners review the role/capability mapping; educational aliases grant nothing.
- [ ] Current main and concurrent PM/billing PR1151 are reconciled before release.

## Required integration before a live pilot

- [ ] Register a current-owning-module authority adapter; deny unknown action/role/scope.
- [ ] Add typed adapters to actual Engineering/PM assignment, document and project services.
- [ ] Add recipient resolution and recipient-side record/field authorization.
- [ ] Implement production PostgreSQL store, atomic revision/audit commits, deduplicated
  start requests, worker leases, cancellation, retention and interrupted-run handling.
- [ ] Allocate/review the real migration number against current main and all open PRs.
  Exercise actual predecessor SQL, replay and rollback; do not manufacture ledger entries.
- [ ] Register authenticated endpoints and a worker only behind a default-off Test flag.
- [ ] Ensure role/module availability middleware covers new routes, including antiforgery
  protections for cookie-authenticated writes. Resolve actor and scope server-side.
- [ ] Add a minimal server projection; never expose raw AgentRun or tool bodies.
- [ ] Wire 019 and 018 to the reusable panel with context-change abort and fresh authorization.
- [ ] Build Agent Operations separately from Module064; do not duplicate provider order.
- [ ] Cancellation and role/source changes invalidate pending proposals and stored views.
- [ ] Record actual route revisions/decisions, aggregate provider usage/cost and child budgets.
  The initial model-call limit counts router requests, not every internal provider attempt.
- [ ] Keep cross-role control evidence private until an explicit external payload is qualified.
- [ ] Test external adapters and outbox delivery before adding any action beyond proposals.

## Pilot acceptance with synthetic project records and distinct real role identities

- [ ] An assigned Engineer can read only assigned project/task evidence.
- [ ] Another Engineer/team cannot read that run or its evidence, even with its run ID.
- [ ] The PM sees only assigned projects; PM leadership scope uses actual managed teams.
- [ ] Engineering managers retain the exact teams they oversee; leads do not inherit approvals.
- [ ] System SA and Collaboration & Networking SA scopes remain separate.
- [ ] Project Coordinator cannot use PTC capabilities; PTC scope does not grant unrelated data.
- [ ] Finance/Billing/Audit fields are filtered by their actual permissions.
- [ ] Super Administrator View-As cannot trigger or resume an agent action.
- [ ] A mid-run role revocation, source update or kill switch prevents subsequent tools/output.
- [ ] Retrieved prompt injection cannot add tools, recipients, approvals or external disclosure.
- [ ] One engineer blocker proposal is reviewable by the currently assigned, authorized PM.
- [ ] A changed PM assignment invalidates a pending recipient; no cross-role privilege transfer.
- [ ] Handoff approval is source-version-specific and cannot be made by the originating agent.
- [ ] Duplicate triggers, client retries and restarts do not duplicate writes or reset budgets.
- [ ] Missing receipt is reported as uncertain, not sent/published/approved.
- [ ] Long runs survive a worker restart and browser navigation using qualified durable storage.
- [ ] Keyboard, mobile, light/dark and stale-session browser tests pass in the real workspaces.
- [ ] Existing time, SOW, assignment, financial and deployment workflows remain functional.

## Release and rollback

Deploy only the exact reviewed merged SHA via the existing Protected UAT controller.
Do not modify deployment concurrency, approvals or release admissions for this feature.
Verify actual migration receipts, installed API/web identities and the above pilot cases.
Keep user-generated proposals separate from business records and approved artifacts.
Disable the feature flag to stop new goals, cancel/expire outstanding claims safely, and
retain audit evidence. Do not delete existing business data or globally revoke role access.
A future production rollout requires a separate decision after UAT acceptance.

## Subsequent business milestones

- SOW/GSD: exact Service Scope, all five phases, expanded overview, exclusions, shared
  task hours, save/reload/export and SA review. Genuine model output, not a template.
- FlowHive: current approved document versions, valid cited WBS, deterministic schedule,
  PM review and no automatic approved-baseline mutation.
- Delivery health: scoped resource/time/risk/financial explanations; ordinary due-date
  reminders remain deterministic; project closure stops relevant agent subscriptions.
- Controlled actions: reuse SELL/notification/assignment/approval services; no arbitrary
  SQL, shell, provider URLs, rate changes, invoice posting or production deployment tool.
