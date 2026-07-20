# Existing Source Asset Mapping

Read-only discovery used the attached GitHub archive and current `ahmedadeyemi-cts/ussignal` `main` commit `da634f7620c2f76d6129020133f27481232edfbd`.

| Existing behavior | Source evidence | Module 071 disposition |
|---|---|---|
| Full schedule save and version history | `functions/api/admin/oncall/save.js` | Preserved through validated schedule save |
| Friday rotation generation | `functions/api/admin/oncall/autogenerate/index.js` | Preserved as unsaved preview using 16:00-to-07:00 Central windows |
| Roster management | `functions/api/admin/roster/*` | Preserved with Module 062 identity IDs |
| Public current routing | `functions/api/oncall.js`, `functions/api/oncalltoday/index.js` | Consolidated under versioned public APIs |
| Monday upcoming email | attached cron worker | Preserved as Global SMTP notification policy |
| Tuesday acknowledgement escalation | attached cron worker and live trigger evidence | Preserved as Global SMTP scheduler policy |
| Friday start email | attached cron worker | Preserved as Global SMTP notification policy |
| Deduplication, dry-run, force, heartbeat, audit | worker and notification handlers | Required for the authorized scheduler integration phase |
| Direct provider delivery | several worker and Pages handlers | Replaced by Module 067 Global SMTP dependency |
| Multiple inconsistent schedule keys | `schedule`, `ONCALL:SCHEDULE`, `ONCALL:CURRENT` | Compatibility adapter reads canonical public shape; provider consolidation remains a cutover task |

## Time normalization

The checked-in cron declarations and live retired external compatibility service trigger evidence disagree. Module 071 defines the intended business schedule as 08:00 America/Chicago on Monday, Tuesday, and Friday, making daylight-saving behavior explicit. Runtime scheduling remains deferred.

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
