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

The checked-in cron declarations and live Cloudflare trigger evidence disagree. Module 071 defines the intended business schedule as 08:00 America/Chicago on Monday, Tuesday, and Friday, making daylight-saving behavior explicit. Runtime scheduling remains deferred.
