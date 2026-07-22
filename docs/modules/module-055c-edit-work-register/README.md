# Module 055C — Edit Work Register

Module 055C is the existing-record workspace for the Work Register. It does
not create new Work Register records.

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

## Database boundary

Migration `035_work_register_055c_055d_split.sql` formalizes the 055C edit
permission and guarantees the audit table and indexes. The migration source is
created but is not applied by the application at runtime.
