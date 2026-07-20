# Module 071 — On-Call Scheduling

Module 071 is the governed ProjectPulse source package for the established US Signal Professional Services on-call schedule. It preserves the operational behavior discovered in `ahmedadeyemi-cts/ussignal@da634f7620c2f76d6129020133f27481232edfbd` while moving authorization, identity selection, public routing contracts, and branding into ProjectPulse.

## Confirmed behavior

- Everyone with a ProjectPulse session can view the schedule and roster.
- Only canonical `MANAGER` and `ENGINEERING_TEAM_LEAD` roles can add, edit, generate, restore, or save schedules and rosters.
- Administrator status alone does not grant Module 071 management authority.
- Engineer selection uses Module 062 stable `app_users.user_id` values and a dropdown sourced from active ProjectPulse identities.
- Coverage starts Friday at 4:00 PM America/Chicago and ends the following Friday at 7:00 AM America/Chicago.
- Dates and assigned identities can be changed at any time by an authorized schedule manager.
- Public, versioned GET APIs expose the current assignment and schedule for external routing.
- The established Monday upcoming notice, Tuesday acknowledgement escalation, and Friday start notice remain the notification contract.
- Email delivery belongs to Module 067 Global SMTP. No direct provider client or text-message path exists in this module.

## Source-package boundary

This release train uses the existing retired external compatibility service service as a compatibility persistence adapter. It introduces no database migration, does not change retired external compatibility service, and does not activate email or scheduled jobs. The authenticated center and versioned public GET routes are registered in current-main source; without approved retired external compatibility service credentials the adapter remains unavailable and makes no external change.

The source becomes runtime-active only after all of the following are separately approved:

1. Module 067 provides the shared mail sender contract.
2. ProjectPulse public-route and scheduler registration is reviewed.
3. retired external compatibility service Access service credentials are provisioned through an approved secret store.
4. The legacy retired external compatibility service notification schedule is retired or coordinated to prevent duplicate email.
5. Module 002/064/066/067/068 overlap evidence passes against the exact release-train base.

## Environment names

Only environment-variable names are documented; values are never committed.

- `retired_oncall_upstream_setting`
- `retired_oncall_service_identity`
- `retired_oncall_service_credential`

The upstream base URL must use HTTPS. The source package never returns either retired external compatibility service Access credential.

## Branding

The React center uses the existing repository-owned US Signal logo data asset and the canonical ProjectPulse US Signal brand tokens: blue, strong blue, cyan, and green. It includes branded hero, navigation, status treatments, public API documentation, and footer without hotlinking an external logo.

## Authorization and external state

- Azure changes: none.
- Database changes: none.
- Entra changes: none.
- retired external compatibility service changes: none.
- Commit, push, and deployment: not performed by this package.

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
