# Module 019: one selected project workspace

This PR starts the engineer/engineering-lead redesign requested on September 18, 2026. Baseline: `8e0364ee310eee64c8d39bef4daaf71efc5aed78` on `main`.

## Experience

1. Search and select one assigned project or standalone service request.
2. Read personal available hours, current project allocations, total logged time, and the allocation balance.
3. Use Tasks & hours, Project team, Documents, Cost & billing, or Project context without navigating a second portfolio.

The second financial-workspace mount, readiness-card grid, cross-project document catalogue, source-health grid, extraction flags, formula panels, and technical scope labels are removed from the default engineering page. The shared PM, Sales, and Rate Card workspaces continue using their existing component. The build injector explicitly leaves the new Module 019 layout alone.

The existing assignment table is retained within the selected project. Engineers initially see their own tasks; team/manager scopes initially see all project tasks. Everyone authorized for that project can inspect its teammates and logged-hours contribution. Documents keep their original server-authorized download route, authenticated headers, filename handling, and native download fallback. Different document IDs with the same filename remain separate versions.

## Hours and authorization

- The unchanged `LoadProjectsAsync` scope query supplies the only project IDs accepted by the new assignment and team-hours reads. No client-supplied project list is trusted.
- Team visibility expands from individual assignment rows to teammates on already-authorized projects. It does not grant access to another project or to internal cost rates.
- Historical/taskless time and former assignees remain in project usage. Declined/rejected/voided time is excluded. Logged time includes entries awaiting approval and is labelled accordingly.
- Multiple allocation rows for one engineer/task do not multiply its logged time. Project usage comes from the dedicated project/user aggregation, not a sum of repeated task totals.
- A resource-request allocation fallback distributes only the unallocated balance among current rows missing task hours. It does not multiply the request total through the assignment join or add the full request allocation on top of explicitly assigned task hours.
- Available hours means current allocated hours minus logged hours. It can be negative. It is not an approved contract budget, task completion percentage, or current estimate to complete.
- Standalone requests display requested hours and explicitly unknown allocated/logged time until linked to a project. They match documents using intake IDs.

## Cost and billing boundary

The existing financial source calculates estimates using commercial rate averages or budget-derived rates. It supplies no verified internal labor-cost basis and no current estimate to complete. Consequently, the new view does not promote its unexplained `over_budget` flag into a definitive actual-cost warning. Users with commercial visibility can expand an explanation of the legacy estimate/variance. Restricted amounts use one clear access explanation rather than eight empty cards.

Invoice history is fetched separately from the existing authorized `/api/billing/projects/{projectId}/invoices` read. Each record shows number, date, status, and recorded amount. Draft/void/finalized states are not summed into an invented billed total; delivery/payment is not inferred. The existing invoice and financial DTOs do not supply currency, so the UI does not invent USD or combine their amounts. Failed or restricted reads never become zero billed.

The existing invoice handler resolves the signed-in session actor. Module 019 therefore disables invoice requests in View-As rather than exposing administrator results while previewing an engineer. A future effective-user-aware billing projection can close that remaining preview gap.

**Still required before claiming full financial attribution:** an approved internal-cost source with rate purpose, effective date, currency, and historical authority; a current estimate to complete; invoice currency and issued/void/credit semantics; effective-actor billing support. This PR shows hours contribution and existing authorized invoice evidence, not an inferred dollar cost per engineer.

## Validation and rollout

- Full frontend `npm run build` (including existing gates).
- `node --test tests/module019-workspace-model.test.mjs`.
- `node tests/module019-workspace-sql.test.mjs` with isolated PGlite 0.5.8 PostgreSQL execution.
- `node tests/module019-workspace-browser.mjs` with Playwright and synthetic API fixtures, desktop/mobile, scope switching, download, invoice isolation, and optional-source failures.
- Existing Module 019 document-access and Group 3 workspace checks.
- Existing Module 019 CI compiles the .NET backend; its added behavior job runs the focused tests.

No migration or new storage location. No merge or deployment is requested by this PR. The API and UI should be released together through the existing Protected Test process, after review. Authenticated Engineer and Engineering Lead UAT, actual document downloads, and customer invoice checks remain necessary. Reverting the PR restores the previous presentation; there is no database rollback.
