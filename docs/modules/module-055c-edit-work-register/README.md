# Module 055C — Manage Existing Projects

Module 055C is the existing-project workspace for the Work Register. It does
not create new projects.

## Authorized editors

- A Project Manager (`PROJECT_MANAGER` or `PROJECT_MANAGEMENT`) may edit only
  projects where that user is the assigned Project Manager.
- A Project Management Lead (`PROJECT_MANAGEMENT_LEAD`,
  `PROJECT_MANAGEMENT_TEAM_LEAD`, or `PM_TEAM_LEAD`) follows the same
  assigned-project rule.
- A Project Team Coordinator (`PROJECT_TEAM_COORDINATOR`), Administrator
  (`ADMINISTRATOR`), or Super Administrator (`SUPER_ADMINISTRATOR`) may edit
  every project.

Other authorized viewers may search and inspect Work Register information, but
the backend denies their mutations. View-As is always read-only.

## Audited changes

Project setup, lifecycle, task assignment, multi-engineer roster, document,
change-order, and purchase-order saves record the actual actor, timestamp,
reason or summary, changed fields, and old/new values in
`work_register_change_history`. Project details expose that evidence in the
Audit tab.

Project setup preserves and updates the existing SOW signed date and estimated
end date. Contract types use the canonical labels **Time and Material** and
**Fixed Price**; legacy `TM`, `T&M`, and `FP` values are normalized without
creating duplicate choices. Invalid historical or partial date changes are
skipped during audit replay so they cannot violate the project date range.

## Governed launch ownership

Module 055C is the approved project-context starting point for two follow-up
workflows:

- **Start Project Closeout** transfers the selected project to Module 040,
  which remains the governed closeout-readiness workflow.
- **Request Partial Invoice** will transfer the selected project through Module
  039 billing readiness and then Module 042 invoice preparation. Accounting or
  Billing retains final issuance authority.

The Module 040 closeout handoff is implemented. The partial-invoice handoff
remains separate follow-up work.

## Database boundary

Migration `035_work_register_055c_055d_split.sql` formalizes the base 055C edit
permission and guarantees the audit table and indexes. Migration
`036_work_register_role_scope_and_closeout_handoff.sql` aligns Administrator
grants and permission metadata with the resource-scoped policy. Migration
`037_work_register_dates_and_contract_types.sql` persists the shared date
contract and consolidates recognized contract-type variants. Migrations are
never applied by the application at runtime.
