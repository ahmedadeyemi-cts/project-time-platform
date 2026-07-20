# Module 071 Capability Matrix

| Requirement | Capability | Source status | Runtime status |
|---|---|---|---|
| RES-015 | On-call schedule visibility | Implemented | Registration deferred |
| RES-015 | Manager and Engineering Team Lead management | Implemented and server-enforced | Registration deferred |
| RES-015 | Identity dropdown and stable IDs | Implemented against Module 062 identity tables | Registration deferred |
| RES-015 | Friday rotation preview | Implemented | Registration deferred |
| RES-015 | Versioned public routing API | Implemented | Public-route allowlist deferred |
| RES-015 | History and restore | Compatibility adapter implemented | Upstream credentials not configured by source |
| RES-015 | Monday, Tuesday, Friday email policy | Contract documented | Module 067 scheduler integration deferred |
| GOV-015 | Central module ownership | Governance update prepared in the dedicated tracker worktree | Not committed |
| RBAC-019 | Least-privilege actions | Exact canonical role checks implemented | Registration deferred |

The module does not introduce a database artifact, direct mail provider, scheduled background registration, secret value, retired external compatibility service mutation, or deployment action.

## PROJECTPULSE_NATIVE_POSTGRESQL_MIGRATION_031

- Source parent: `603538ad408b70b3e6a26ff2f4f162599fa1cabf`
- Migration source: `database/migrations/031_modules_071_072_native_persistence.sql`
- Rollback source: `database/rollback/031_modules_071_072_native_persistence_rollback.sql`
- Module 071 persistence: ProjectPulse PostgreSQL schedule, roster, acknowledgement, and history tables
- Module 072 persistence: ProjectPulse PostgreSQL routing directory and immutable revision tables
- Platform Administrator authority: explicit
- View-As write authority: blocked
- External compatibility runtime dependency: removed
- Migration applied: no
- Database changed: no
- Deployment performed: no
