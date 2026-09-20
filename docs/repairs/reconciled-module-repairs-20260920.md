# PR 1115 reconciliation after PR 1116

Based on main 18f192c9 (PRs 1116 and 1117 included). This follow-up carries the remaining application fixes from PR 1115 without replacing the merged AI, Teams, View-As or deployment recovery work.

## Implemented

- Timesheet submission confirmation retains the employee recipient and copies active Project Managers and Project Team Coordinators assigned to projects on the submitted day. Existing people-manager notification remains separate. Duplicate addresses are normalized, and administrative-only time does not broadcast to unrelated project teams. These server-derived recipients also flow through the existing Teams notification integration when enabled.
- Module 021 ignores stale source-save, preview and import responses after identity changes.
- Module 029 preserves actual HTTP status and JSON from availability handlers, uses the authoritative session client, and invalidates stale checks. The merged global HTTP 200 presentation fix remains.
- Budget classification uses a shared 85% boundary, requires complete nonnegative budget components, retains historical time after assignments end, excludes manager/PM-declined time, and withholds unsupported-currency totals. Merged required-source failure handling remains.
- Portfolio APIs retain PR 1116's `hasMore` contract and add `nextOffset`. Shared client loading covers every authorized page, deduplicates projects and rejects nonadvancing or partially failed pagination. Module 022 uses the same loader and resets on identity changes.
- Module 022 presents one project and an explanation of the estimate; routing administration mounts only when expanded.
- Module 042 receives its missing standalone route exclusion, sequential invoice steps, selected-project reference summary, keyboard-accessible project selection and advanced-option disclosures. PR 1116's Essential columns reset remains.
- Module 055B foregrounds SELL/manual rates and collapses priority guidance. Merged deferred financial loading remains.
- Module 060 derives contract privileges from active roles, never job-title text. PR 1116's effective-identity handling and View-As write denial remain.
- Module 065 removes the empty directory-host reservation; merged shell spacing and Teams configuration remain.
- Modules 020, 024, 027 and 028 explain their purpose in the page. Module 020 remains historical intake evidence; this release does not repurpose its persistence. Scope and budget change control would require a separately designed approval and baseline workflow.

## Validation

The release runs the existing provider/migration/autosave suites, ten budget cases, four portfolio cases, two real HTTP endpoint cases, and five disposable-PostgreSQL notification recipient scenarios. Recipient scenarios cover PM/PTC copies, unrelated PTC exclusion, inactive users, duplicate roles/entries, different days/timesheets, administrative time, PM-as-submitter deduplication and the existing people-manager notification.

Source registration pins this follow-up's files and content against its reviewed main baseline. No production deployment, database migration, protected candidate or deployment workflow changes are included.

## Acceptance limits

These are rate-based forecasts, not verified internal labor costs. Actual person/effective-date cost authority, commitments, approved changes and remaining work still need reconciliation. Email/Teams receipt depends on configured transport, recipient boundaries and tenant consent. This change does not send a live message or enable delivery by itself. Protected UAT deployment and real-role/visual acceptance must be reported separately from successful CI.
