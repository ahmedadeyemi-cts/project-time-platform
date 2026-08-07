# Group 4 — Project Cost Routing and Configurable Notifications

## Purpose

Group 4 coordinates Modules 018, 022, 023, 032, 041, and 065 around one governed project-notification contract. It is reconciled directly with current `main`, which already contains the Group 2B platform abstraction, the Group 3 authoritative project-financial contract, Module 064 provider-health work, and the Pulse AI foundation.

- **Module 018** consumes project notification status in the Project Manager workspace.
- **Module 022** owns cost-alert routing rules and financial-trigger evaluation.
- **Module 023** owns configurable schedules, timezones, quiet hours, escalation timing, and delivery boundaries.
- **Module 032** is activated as the **Notification Delivery Monitor** productivity workspace.
- **Module 041** owns closeout-message content and invokes the Group 4 routing contract.
- **Module 065** remains the only owner of mail-provider configuration, credentials, sender identity, recipient boundary, and external delivery.

This package contains source and migration preparation only. It does not apply migration 050, send email during development, deploy, alter Azure, modify Container Apps, change Module 065 credentials, or read retired Module 067 configuration.

## Migration 050

`050_project_notification_routing_and_schedules.sql` creates:

1. `project_cost_alert_routing_rules`
2. `project_notification_schedules`
3. `project_notification_dispatches`
4. `project_notification_dispatch_recipients`
5. `project_notification_delivery_attempts`
6. `project_notification_configuration_audit`

Delivery attempts and configuration-audit records are immutable. The rollback removes only Group 4-owned schema, permissions, feature-catalog entries, and its schema-migration record.

Migration 050 is present in source but has not been run against Azure, test, production, or any connected ProjectPulse database.

## Module 022 — Cost Alert Routing Rules

Nontechnical administrators can configure rules for:

- percentage of hours used;
- percentage of labor budget used;
- percentage of expenses used;
- forecasted total cost;
- approaching budget;
- over budget;
- missing financial information; and
- failed project-data refresh.

Each rule records the metric, comparison, threshold, unit, severity, recipient roles, optional escalation manager, escalation timing, delivery boundary, enabled state, and immutable change history.

The evaluator consumes authoritative projects, assignments, time entries, current non-deleted Module 005 expenses, governed rates, forecast, and variance. Missing data remains explicit; no budget, rate, cost, forecast, or recipient is fabricated.

## Automatic recipient derivation

Recipients come from authoritative server-side project relationships:

| Recipient | Authoritative source |
|---|---|
| Project Manager | `projects.project_manager_user_id` |
| Assigned engineers | `project_assignments.user_id` |
| Solution Architect | `projects.solution_architect_user_id` |
| Account Executive | `projects.account_executive_user_id` |
| Project Team Coordinator | `projects.project_coordinator_user_id` |
| Optional escalation manager | Governed routing-rule configuration |

Browser-provided recipient lists are not delivery authority. Duplicate or invalid addresses are suppressed, and every dispatch records the recipient derivation source.

## Module 023 — Notification Scheduling

Administrators can configure:

- schedule type;
- day of week;
- local time;
- timezone;
- weekly reminder;
- Monday reminder;
- month-end reminder;
- days before month-end;
- escalation timing;
- quiet-hours start and end;
- enabled state; and
- Test-only, production-governed, or locked delivery boundary.

The scheduler is bounded and multi-replica safe. A PostgreSQL advisory lock permits only one API replica to evaluate due schedules. Migration absence, database failure, or an existing lock causes a fail-closed exit without delivery.

Quiet-hours dispatches are deferred until the configured local quiet-hours end.

## Module 032 — Notification Delivery Monitor

Module 032 is the productivity enhancement selected from the available 031–035 range. It provides one operational inbox for:

- project notification dispatches;
- automatically derived recipients and derivation evidence;
- Module 065 provider readiness;
- Test-only, production-governed, and locked boundaries;
- queued, held, sent, failed, and suppressed states;
- immutable delivery attempts;
- source-specific diagnostics;
- authorized release and retry;
- active routing rules and schedules; and
- failed project-financial sources.

Modules 034 and 035 were not reused because the repository already assigns them to Dashboard and Navigation Labeling and Guided Project Intake Launch. Module 033 is assigned to Project Forge and integrates with this notification contract through governed Module 065 source events.

## Module 041 — Closeout notification compatibility

The historical route remains available:

`POST /api/project-closeout/email/send`

Group 4 intercepts that route before the legacy direct-mail implementation. The replacement resolves the actual and effective user, resolves and authorizes the project, ignores browser-provided recipient authority, derives the project team on the server, records a durable dispatch, and delegates delivery readiness and transport to Module 065.

Group 5 remains responsible for Module 041 closeout data, source recovery, access attribution, and workspace behavior. Group 4 owns notification routing and delivery only.

## Module 065 delivery boundary

Live delivery requires all of the following:

- the configured profile matches the running Test or Production environment;
- Module 065 has a complete sender and provider configuration;
- the provider is not locked;
- the configured and effective recipient boundary is `production_governed`; and
- the caller has non-View-As delivery authority.

A `test_only` or `locked` boundary records dispatch and attempt evidence without external mail. Group 4 never accepts credentials in a request and never returns secret values.

Before Module 065 is invoked, the shared delivery service atomically moves one
eligible dispatch to `sending`. Concurrent tabs, API retries, and scheduler
workers observe that in-flight claim and do not call the provider. Event-key
upserts preserve both `sending` and `sent`, so a repeated Module 027 handoff
cannot reset an active claim or produce a duplicate PTC notification.

## Permissions

Migration 050 adds:

- `VIEW_COST_ALERT_ROUTING_RULES`
- `MANAGE_COST_ALERT_ROUTING_RULES`
- `VIEW_NOTIFICATION_SCHEDULES`
- `MANAGE_NOTIFICATION_SCHEDULES`
- `VIEW_NOTIFICATION_DELIVERY_MONITOR`
- `MANAGE_NOTIFICATION_DELIVERY`
- `VIEW_CLOSEOUT_NOTIFICATION_ROUTING`
- `DELIVER_PROJECT_NOTIFICATIONS`

| Role group | Intended access |
|---|---|
| Super Administrator, Administrator, Project Team Coordinator | View and manage rules, schedules, dispatches, closeout routing, and delivery |
| Project Managers and PM leads | View rules, schedules, closeout routing, and project-scoped dispatch evidence |
| Accounting, Billing, Finance, Executive | View routing, schedules, closeout routing, and delivery evidence |
| Engineering, engineering leads, Sales, Inside Sales | View server-scoped Module 032 delivery evidence |
| Solution Architect | View cost rules and server-scoped delivery evidence |
| Managers | View server-scoped delivery evidence |

Backend project scope continues to apply even when a role has Module 032 view permission.

## APIs

- `GET /api/project-notifications/routing-rules`
- `PUT /api/project-notifications/routing-rules/{ruleId}`
- `GET /api/project-notifications/schedules`
- `PUT /api/project-notifications/schedules/{scheduleId}`
- `GET /api/project-notifications/module-065-readiness`
- `POST /api/project-notifications/evaluate`
- `GET /api/project-notifications/dispatches`
- `GET /api/project-notifications/delivery-monitor`
- `POST /api/project-notifications/dispatches/{dispatchId}/release`
- `POST /api/project-notifications/dispatches/{dispatchId}/retry`
- `POST /api/project-notifications/run-due`
- `POST /api/project-notifications/closeout/queue`

## Shared-file overlap

The package makes additive changes to:

- `ProjectTime.Api.csproj` for compatibility middleware and endpoint registration;
- frontend `package.json` for the idempotent installer and validator; and
- `admin-runtime-stability-ci.yml` so unrelated migration-bearing PRs run its regression checks without being misclassified as the admin-runtime source branch.

The application shell and module registry are generated idempotently during predevelopment and prebuild. Canonical `App.jsx` and `module-availability-registry.js` are not committed by Group 4.

## Explicit exclusions

- no Module 038 layout change;
- no Certify behavior change;
- no direct Module 067 configuration read;
- no second mail credential system;
- no browser-authoritative recipient list;
- no Azure or Container Apps operation;
- no migration execution;
- no merge;
- no deployment; and
- no FlowHive implementation.
