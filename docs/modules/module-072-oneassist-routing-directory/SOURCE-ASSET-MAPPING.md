# Existing Source Asset Mapping

Read-only discovery used the attached combined On-Call/OneAssist archive and current `ahmedadeyemi-cts/ussignal` `main` commit `da634f7620c2f76d6129020133f27481232edfbd`.

| Existing behavior | Source evidence | Module 072 disposition |
|---|---|---|
| Customer/PIN editor | `app.js` OneAssist section | Preserved with ProjectPulse authorization |
| Five-digit and uniqueness validation | admin save handlers | Preserved on client and server |
| CSV/XLSX import | admin UI JavaScript | Preserved through server-side preview parser without adding a frontend dependency |
| CSV and IVR CSV downloads | `app.js` | Preserved |
| Full public directory | `functions/api/ps-customers/index.js` | Preserved under versioned public API |
| PIN resolution | `functions/api/customers/index.js` | Preserved under versioned public API |
| retired external compatibility service KV persistence | `ONCALL:PS_CUSTOMERS` | Preserved temporarily through compatibility adapter |
| PIN described as authentication | legacy comments | Corrected: PIN is a public routing identifier and cannot authenticate a person |

No PIN values from the attachment, runtime retired external compatibility service KV, screenshots, or source environment were copied into ProjectPulse.

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
