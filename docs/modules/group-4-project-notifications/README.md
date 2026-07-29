# Group 4 — Project Cost Routing and Configurable Notifications

## Scope

Group 4 coordinates Modules 018, 022, 023, 032, 041, and 065 around one governed project-notification contract.

- **Module 018** consumes project notification status in the Project Manager workspace.
- **Module 022** owns cost-alert routing rules and financial trigger evaluation.
- **Module 023** owns configurable schedules, timezones, quiet hours, escalation timing, and delivery boundaries.
- **Module 032** is activated as the **Notification Delivery Monitor** productivity workspace.
- **Module 041** owns closeout-message content and invokes the Group 4 notification contract.
- **Module 065** remains the only owner of mail-provider configuration, credentials, sender identity, recipient boundary, and external delivery.

This package is source and migration preparation only. It does not apply migration 050, send email, deploy, alter Azure, modify Container Apps, change Module 065 credentials, or read retired Module 067 configuration.

## Dependency and branch model

Group 4 is stacked on the validated Group 3 project-financial source branch so routing rules consume the same project financial truth rather than create a competing calculation system. During integration, Group 3 must merge first or Group 4 must be rebased normally onto a main branch containing the Group 3 contract. No force-push over newer work is authorized.

## Migration 050

`050_project_notification_routing_and_schedules.sql` creates:

1. `project_cost_alert_routing_rules`
2. `project_notification_schedules`
3. `project_notification_dispatches`
4. `project_notification_dispatch_recipients`
5. `project_notification_delivery_attempts`
6. `project_notification_configuration_audit`

Delivery attempts and configuration audit records are immutable. The rollback removes only Group 4-owned schema, permissions, and feature-catalog entries.

Migration 050 is present in source but was not run against Azure, test, production, or any connected ProjectPulse database as part of this package.

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

Each rule records:

- metric;
- comparison;
- threshold;
- threshold unit;
- severity;
- automatically derived recipient roles;
- optional escalation manager;
- escalation timing;
- Test-only, production-governed, or locked delivery boundary;
- enabled state; and
- immutable configuration history.

The evaluator consumes projects, assignments, time entries, current non-deleted Module 005 expenses, and governed project rates. Missing data is explicit. The evaluator does not fabricate a budget, rate, cost, forecast, or recipient.

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

Browser-provided recipient lists are not accepted as delivery authority. Duplicate or invalid addresses are suppressed. Dispatch evidence records the derivation source for every recipient.

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

The in-process scheduler is bounded and multi-replica safe. It acquires a PostgreSQL advisory lock so only one API replica evaluates due schedules. If migration 050 is absent, database connectivity is unavailable, or the advisory lock is held by another replica, the scheduler exits without changing state.

Quiet-hours schedules are deferred to the local quiet-hours end rather than delivered. Timezone calculations fall back to UTC only when a configured timezone cannot be resolved.

## Module 032 — Notification Delivery Monitor

Module 032 is the productivity enhancement selected from the available 031–035 range.

Its specific purpose is a single operational inbox for:

- project notification dispatches;
- automatically derived recipients and derivation evidence;
- Module 065 provider readiness;
- Test-only, production-governed, and locked boundaries;
- queued, held, sent, failed, and suppressed states;
- immutable delivery attempts;
- source-specific diagnostics;
- release and retry actions for authorized roles;
- active routing rules and schedules; and
- failed project-financial sources.

The monitor avoids forcing Project Managers, Project Team Coordinators, Accounting, and administrators to inspect separate outbox, provider, project, and audit screens.

Modules 034 and 035 were not reused because the repository already assigns them to Dashboard and Navigation Labeling and Guided Project Intake Launch. Module 033 remains reserved.

## Module 041 — Closeout notification compatibility

The historical route remains available:

`POST /api/project-closeout/email/send`

Group 4 intercepts that route before the legacy implementation. The replacement behavior:

1. resolves the authenticated actual and effective user;
2. resolves the authoritative project;
3. enforces project scope;
4. ignores browser-provided recipient authority;
5. derives the project team on the server;
6. records a durable dispatch and recipients;
7. delegates delivery readiness and transport to Module 065; and
8. returns dispatch, boundary, provider, and audit evidence.

Group 5 remains responsible for Module 041 closeout data, source recovery, access attribution, and workspace behavior. Group 4 owns only routing and delivery.

## Module 065 delivery boundary

Group 4 calls Module 065’s environment-specific runtime configuration and supports its governed Microsoft Graph or Microsoft 365 SMTP transport.

Live delivery requires all of the following:

- the configured profile matches the running Test or Production environment;
- Module 065 has a complete sender and provider configuration;
- the provider is not locked;
- the configured and effective recipient boundary is `production_governed`; and
- the caller has non-View-As delivery authority.

A `test_only` or `locked` boundary records dispatch and attempt evidence without sending external email. Group 4 never accepts credentials in a request and never returns secret values.

No Group 4 source reads retired Module 067 configuration.

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

### Role intent

| Role group | Intended access |
|---|---|
| Super Administrator, Administrator, Project Team Coordinator | View and manage rules, schedules, dispatches, closeout routing, and delivery |
| Project Managers and PM leads | View rules, schedules, closeout routing, and server-scoped dispatch evidence |
| Accounting, Billing, Finance, Executive | View project routing, schedules, closeout routing, and delivery evidence |
| Engineering, engineering leads, Sales, Inside Sales | View server-scoped Module 032 delivery evidence |
| Solution Architect | View cost rules and server-scoped delivery evidence |
| Managers | View server-scoped delivery evidence |

Backend project scope continues to apply even when a role has the Module 032 view permission.

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

The package requires additive changes to:

- `ProjectTime.Api.csproj` for compatibility middleware and endpoint registration;
- frontend `package.json` for the installer and validator; and
- generated frontend integration into `App.jsx` and `module-availability-registry.js` for Module 032.

The application shell and registry are generated idempotently during predevelopment and prebuild. The branch does not directly rewrite their canonical source files.

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
