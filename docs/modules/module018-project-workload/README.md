# Module 018: PM project workspace

Module 018 now follows Module 019's selected-project presentation. It uses the same workspace CSS primitives while retaining Project Manager responsibilities and the existing server-side scope.

## Experience

- Compact Module 018 header, refresh action, project search, active/closed/all filter, and one managed-project selector.
- PM Team Leads and broad-access roles retain the PM selector returned by the workload API. Ordinary PMs receive their own workload without a manager selector.
- Selected-project cards show recorded allocations, logged time, resource count, and task count. Unknown detail values remain unavailable rather than becoming zero.
- Project context, Team & hours, Financials, Expenses, and Documents follow the selected project.
- Portfolio counts, quarterly completion, status distribution, and server-returned risk highlights remain available in the expandable Portfolio overview. Risk highlights are not represented as an exhaustive risk count.
- Negative engineer allocation balances are shown as hours over allocation.
- Closed projects remain available through the status filter.

## Data and scope

The existing `/api/project-management/workload` response supplies selection options, scope, portfolio counts, and workload information. Additional information uses `/api/project-financials/projects/{projectId}?workspace=pm` with the selected PM ID when applicable. Requests use existing authentication and effective-user headers. This change adds no API, database migration, access grant, or write operation.

Each response is associated with its PM scope, refresh generation, and selected project. Stale asynchronous reads cannot populate the newer selection. Identity changes remount the workspace. Pending downloads are discarded after selection or identity changes. Document downloads retain the existing role-scoped endpoint.

Financials and expenses reuse the existing presentation components through named exports; Sales and Rate Card behavior remains unchanged. Partial source failures are explicit, and dependent financial/expense/document sections are withheld instead of presenting failed reads as zero or an empty authoritative result. Financial estimates retain their existing calculations and an explicit distinction from internal labor costs and invoicing. This PR does not introduce actual invoice history or verified internal cost.

The Group 3 build injector no longer prepends a duplicate financial portfolio to Module 018. Its validation checks now require the single PM workspace. Module 019 and Module 020 implementations are unchanged.

## Validation

- Full frontend `npm run build`, including existing validation gates.
- Group 3 contract validation: 40 checks.
- `tests/module018-workspace-browser.mjs`: mocked API browser checks for project selection/search/status consistency, closed projects, team over-allocation, PM scope changes and delayed-response isolation, effective-user headers, document downloads/denials, financial and expense display, partial-source handling, detail denial, workload retry, keyboard focus, and mobile/light/dark presentation.
- Browser tests accept `MODULE018_PLAYWRIGHT_PATH` and `MODULE018_CHROMIUM_PATH` overrides, following the existing Module 019 browser-test pattern.

Authenticated PM, PM Lead, and PTC/Administrator acceptance against real project data remains a Protected UAT step. No merge or deployment is included in this PR.
