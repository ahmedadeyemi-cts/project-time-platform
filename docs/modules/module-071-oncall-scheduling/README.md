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

This release train uses the existing Cloudflare service as a compatibility persistence adapter. It introduces no database migration, does not change Cloudflare, and does not activate email or scheduled jobs. The authenticated center and versioned public GET routes are registered in current-main source; without approved Cloudflare credentials the adapter remains unavailable and makes no external change.

The source becomes runtime-active only after all of the following are separately approved:

1. Module 067 provides the shared mail sender contract.
2. ProjectPulse public-route and scheduler registration is reviewed.
3. Cloudflare Access service credentials are provisioned through an approved secret store.
4. The legacy Cloudflare notification schedule is retired or coordinated to prevent duplicate email.
5. Module 002/064/066/067/068 overlap evidence passes against the exact release-train base.

## Environment names

Only environment-variable names are documented; values are never committed.

- `PROJECTPULSE_ONCALL_UPSTREAM_BASE_URL`
- `PROJECTPULSE_ONCALL_ACCESS_CLIENT_ID`
- `PROJECTPULSE_ONCALL_ACCESS_CLIENT_SECRET`

The upstream base URL must use HTTPS. The source package never returns either Cloudflare Access credential.

## Branding

The React center uses the existing repository-owned US Signal logo data asset and the canonical ProjectPulse US Signal brand tokens: blue, strong blue, cyan, and green. It includes branded hero, navigation, status treatments, public API documentation, and footer without hotlinking an external logo.

## Authorization and external state

- Azure changes: none.
- Database changes: none.
- Entra changes: none.
- Cloudflare changes: none.
- Commit, push, and deployment: not performed by this package.
