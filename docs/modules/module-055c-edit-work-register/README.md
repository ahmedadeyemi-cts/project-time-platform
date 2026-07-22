# Module 055C — Manage Existing Projects

Module 055C is the existing-project workspace for the Work Register. It does
not create new projects.

## Authorized editors

- Project Manager (`PROJECT_MANAGER` or `PROJECT_MANAGEMENT`)
- Project Management Lead (`PROJECT_MANAGEMENT_LEAD`,
  `PROJECT_MANAGEMENT_TEAM_LEAD`, or `PM_TEAM_LEAD`)
- Project Team Coordinator (`PROJECT_TEAM_COORDINATOR`)

Other authorized viewers may search and inspect Work Register information, but
the backend denies their mutations. View-As is always read-only.

## Audited changes

Project setup, lifecycle, task assignment, multi-engineer roster, document,
change-order, and purchase-order saves record the actual actor, timestamp,
reason or summary, changed fields, and old/new values in
`work_register_change_history`. Project details expose that evidence in the
Audit tab.

## Governed launch ownership

Module 055C is the approved project-context starting point for two follow-up
workflows:

- **Start Project Closeout** will transfer the selected project to Module 040,
  which remains the governed closeout engine.
- **Request Partial Invoice** will transfer the selected project through Module
  039 billing readiness and then Module 042 invoice preparation. Accounting or
  Billing retains final issuance authority.

These launch controls are documented decisions and are not implemented in
draft PR #55. They remain separate current-main follow-up work.

## Database boundary

Migration `035_work_register_055c_055d_split.sql` formalizes the 055C edit
permission and guarantees the audit table and indexes. The migration source is
created but is not applied by the application at runtime.
