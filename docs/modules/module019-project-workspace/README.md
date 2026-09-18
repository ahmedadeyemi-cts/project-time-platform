# Module 019: one selected project workspace

This PR starts the engineer/engineering-lead redesign requested on September 18, 2026. Baseline: `8e0364ee310eee64c8d39bef4daaf71efc5aed78` on `main`.

## Experience

1. Search and select one assigned project or standalone service request.
2. Read personal available hours, current project allocations, total logged time, and the allocation balance.
3. Use Tasks & hours, Project team, Documents, Cost & billing, or Project context without navigating a second portfolio.

The second financial-workspace mount, readiness-card grid, cross-project document catalogue, source-health grid, extraction flags, formula panels, and technical scope labels are removed from the default engineering page. The shared PM, Sales, and Rate Card workspaces continue using their existing component. The build injector explicitly leaves the new Module 019 layout alone.

The existing assignment table is retained within the selected project. Engineers initially see their own tasks; team/manager scopes initially see all project tasks. Everyone authorized for that project can inspect its teammates and logged-hours contribution. Documents keep their original server-authorized download route, authenticated headers, filename handling, and native download fallback. Different document IDs with the same filename remain separate versions.

## Hours and authorization

- The server-side `LoadProjectsAsync` scope query supplies the only project IDs accepted by the new assignment and team-hours reads. No client-supplied project list is trusted.
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
- `node tests/module019-workspace-access.test.mjs` executes the actual project, document-list, document-download, and request SQL across role-scope fixtures; 93 assertions cover authorized/denied access and the old metadata cutoffs.
- `node tests/module019-workspace-browser.mjs` with Playwright and synthetic API fixtures, desktop/mobile, scope switching, download, invoice isolation, and optional-source failures.
- Existing Module 019 document-access and Group 3 workspace checks.
- Existing Module 019 CI compiles the .NET backend; its added behavior job runs the focused tests.

No migration or new storage location. No merge or deployment is requested by this PR. The API and UI should be released together through the existing Protected Test process, after review. Authenticated Engineer and Engineering Lead UAT, actual document downloads, and customer invoice checks remain necessary. Reverting the PR restores the previous presentation; there is no database rollback.

## Readiness audit follow-up

The follow-up audit fixes an existing inconsistency: explicit additional-team grants now apply to project documents, downloads, and service requests as they already did to project visibility. Engineering-visible restrictions still apply to indirect team access. A manager/lead can also retrieve engineering-visible documents for a project managed by their team. A direct assignment retains the established document-access policy.

Intake-only documents are included under a selected project when an authorized resource-request record explicitly links that intake to the project. Matching never relies on names or project-code text, and a conflicting document project ID is not overridden.

The old 100-project and 250-document/request SQL cutoffs are removed from authorized metadata reads. Larger portfolios no longer silently lose selectable work or files. No document binary is included in the overview. This is a completeness fix; large-scale production performance still requires measurement and may warrant paginated metadata or project-specific loading.

The assignment-propagation gate now checks the selected-project rendering and stable identifier rather than the removed cross-project table. Browser coverage additionally verifies lead defaults, an unassigned user, missing time totals, missing files, denied billing, and expired-session retry. A failed overview clears prior data and is not presented as an empty portfolio.

### Remaining acceptance checks

| Area | Automated evidence | Still required |
| --- | --- | --- |
| Engineer tasks and team hours | Project filtering, multiple engineers, repeated allocations, former engineers, taskless hours, declined time, missing totals | Compare actual assigned/logged hours with the authoritative time records for one shared project |
| Lead/manager visibility | Own/additional-team SQL scope, inactive grant denial, matching document list/download predicates | Sign in as an Engineering Lead with a real team grant; verify permitted and unrelated projects |
| PM and broader roles | Managed-project and broad-scope SQL fixtures | Authenticate as PM/coordinator/admin where Module 019 is enabled; confirm navigation and role mapping |
| Documents | Authenticated browser download, intake/project linkage, same-name versions, missing-file handling, SQL authorization | Download real SOW/GSD files from the persistent upload volume; check file contents and browser PDF preview |
| Billing | Successful/denied responses, record statuses, no stale invoice on failure, no billing fetch in View-As | Confirm real invoice records against billing; decide the intended team-lead and request-only engineer access |
| Accessibility and layout | Keyboard/native controls, modal Escape/focus handling, mobile overflow | Screen-reader and supported-browser testing, especially embedded PDF behavior |
| Availability and scale | Cost failure preserves core workspace; larger metadata fixture exceeds old cutoffs | Production-sized latency checks; the core overview remains one request and a failed core database read requires Retry |

The existing billing project predicate accepts broad billing roles, project managers/coordinators, or direct project assignment. It does **not** mirror Module 019 additional-team or service-request-only access. Consequently some authorized workspace users can see hours/documents but receive the explicit invoice-access message. This PR does not broaden billing authorization implicitly. A reviewed effective-user-aware billing read model should resolve that mismatch before promising billing visibility to every engineer and lead.

These fixtures validate SQL predicates and UI behavior; they do not replace authenticated integration/UAT of session middleware, actual role assignment, deployed data, upload storage, or billing. Keep this PR as draft until the required live acceptance checks and financial-data decisions are complete.
