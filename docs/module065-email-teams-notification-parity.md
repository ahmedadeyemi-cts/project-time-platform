# Pulse email and Teams notification parity

## Purpose and delivery boundary

Every implemented Pulse business email sender is audited here and paired with a Teams path. This is channel parity, not a replacement for email. Existing policy/recipient resolution remains authoritative. A Teams problem never replays a successful email or blocks the email transport; an email transport failure does not suppress an independently queued Teams request.

The change is a PR candidate, not a production activation. The existing Teams enabled setting, environment-specific Microsoft services identity, recipient boundary, policy enablement, current source/recipient authorization, and normal deployment gates still apply. Both source and services boundaries must permit live delivery. Test-only/locked and View-As events cannot become live business Teams messages. The explicit single-recipient Module 065 Teams Test remains separate.

## Audited paths

| Business notification family | Source and preserved audience | Teams integration |
| --- | --- | --- |
| Time submitted | Existing timesheet/day events: engineer confirmation plus the actual next-stage approvers, not every future approver at once | Common enterprise dispatch and independent per-recipient queue |
| Time approved, rejected, final approval, approval overdue | Existing event-policy recipients and approval-stage sequencing | Same queue; stale approval-stage requests suppressed before delayed sends |
| Existing manual non-submission/email action | Existing `/api/time-compliance/email-notifications/send` real-send branch, approved recipient safety review, engineer and its resolved manager/PTC copies | Native per-run/per-engineer event; current preview recipients and safety-review expiry rechecked; email-only provider tests and outbox-only actions excluded |
| New scheduled non-submission and escalation | ENGINEERS group, engineer reminder; escalation to current manager and PTC with engineer copied | New opt-in governed policies, using both existing email delivery and Teams queue |
| Service-request/task close and reopen | Existing Module 001A policy and PTC/project recipient resolution | Inherited common dispatcher; no new unrelated closure audience |
| Project closeout, cost/financial thresholds and scheduled project notices | Existing server-resolved routing, recipients and dispatch IDs | Common dispatcher; retired browser-supplied raw closeout transport cannot bypass governance |
| FlowHive assignments and reminders | Existing assignment, configured advance-day, due-day and overdue rules; assignee and any policy-governed PM copy | Same event; current assignment, completion/cancellation, working-plan source, preferences and quiet hours revalidated |
| FlowHive automatic first draft | Existing draft-readiness event and authorized PM | Current draft/project/PM checked again before delayed delivery |
| SOW/GSD ownership and coverage handoffs | Existing immutable handoff event and current recipient resolution | Inherited durable dispatcher |
| SOW/GSD published in SELL | Existing published submission and AE/Inside Sales/SA recipients | Native queue alongside original SELL email receipt; no SELL replay |
| Expense submission/PM review | Existing canonical upload-confirmation and PM-review enterprise policies | Retires the duplicate direct SMTP/Graph/Brevo expense sender; preserves upload email-status projection |
| Qualifications/expiration | Existing source scanner and policy recipients | Inherited common dispatcher |
| Entra client-secret expiry | Existing versioned, unacknowledged recipient list and cadence | Queued independently; rotation/acknowledgement stops stale Teams reminders; credential values are not messages |
| Scheduled Analytics reports | Existing individualized authorized report recipients | Scoped report-ready notice and authenticated Pulse link; PDF/XLSX stays on the existing email attachment channel |
| Work Register identity action | Existing trusted PTC/PMO/project-management role/profile recipients | Native queue plus governed Module 065 email adapter instead of raw SMTP |
| Other valid native/signed enterprise events | Existing enabled policies and trusted producers | Inherit the common dispatcher automatically; a catalog row without a working upstream producer is not a new event source |
| Upcoming holiday | Every active, login-enabled Pulse user; active nonfloating weekday company holidays | New opt-in policies; one private message per user at configured advance-day windows |
| Month-end PM reminder | Existing PROJECT_MANAGEMENT notification group | New opt-in last-configured-weekday-of-month policy |

`tests/notification-parity/coverage.json` is the machine-readable sender inventory. Its guard rejects new raw email transport owners or uninventoried central-adapter callers. Protocol and PostgreSQL tests, not textual matches alone, validate channel independence, queue behavior and reminder production.

## Exact recipients, privacy and shared destinations

A business-email mirror is a private individual notification to each authorized email recipient, including CC/BCC without exposing those address lists. Addresses are case-insensitively deduplicated. External or inactive/unresolvable tenant addresses remain governed by the email sender but receive a visible Teams suppression rather than a fabricated identity or broadened group.

No group chat is created and no group/channel membership is inferred from the word “cost.” The existing cloud flow's group-chat/channel branches are not modified. Shared-destination delivery must have its own approved destination and end-to-end acceptance; a personal test does not prove it. This parity path deliberately favors the exact email audience over posting personnel/financial data into a potentially wider shared chat.

## Reliable queue and status

Migration 128 creates immutable event snapshots, per-recipient queue rows, persisted attempt timestamps and an append-only operator-action audit. Each event is namespaced by its trusted source ID. Duplicate events cannot append recipients, overwrite content, revive previews, or resend an accepted event. Company-wide audiences are not silently truncated to an envelope's 100-recipient bound: they are individual queued jobs.

One worker per environment is admitted by a database advisory lock, with a conservative budget of 20 attempts per five minutes. A queue can therefore remain waiting for a large holiday audience. Monitor it and qualify the desired tenant-wide volume before production. This budget is application-level headroom, not a guarantee about all other flows sharing a Microsoft connection.

Before sending, the worker reloads current services/Teams configuration, active tenant user, event policy and current source/recipient scope. Reassignment, completed work, revoked role, stopped policy, submitted time, rotated secrets and expired safety review suppress stale deliveries. Current quiet hours defer applicable FlowHive delivery. Pure email text is HTML-escaped and byte-bounded for the existing shared Compose. Authenticated application links do not carry credentials or expose attachment bytes; production never falls back to a Test host when its public base URL is missing.

The outbox reports Waiting, Waiting to retry, Submitting, Accepted by Microsoft, Not sent by policy, Needs attention, and Needs reconciliation. `accepted` establishes HTTP/Graph acceptance only; Power Automate run completion and the visible Teams message are the end-to-end evidence. The existing low-level delivery history is retained for compatibility.

Only definite rate-limit rejection retries automatically, with a finite attempt budget. A timeout, interrupted send or ambiguous 5xx outcome is not blindly replayed. Administrative **Retry Teams only** requires same-origin authorization, no View-As, unchanged failed row/attempt count, unexpired/current source, and an audit entry. It cannot retry accepted, suppressed or unknown rows and never calls an email transport.

## New reminder settings: no silent activation

Migration 129 installs these policies disabled and Test-only, preserving prior admin choices on conflict:

- TIME_NOT_SUBMITTED: Monday 06:00 America/Chicago, ENGINEERS group, prior completed Sunday–Saturday week.
- TIME_NOT_SUBMITTED_ESCALATION: Monday 08:00 America/Chicago, current direct manager and PTC, engineer copied.
- COMPANY_HOLIDAY_UPCOMING: 7 and 1 days before a qualifying holiday, 06:00 America/Chicago, every active application user individually.
- PM_MONTH_END_REMINDER: last Friday at 08:00 America/Chicago, PROJECT_MANAGEMENT group.

Times and audiences are configurable through the existing enterprise policy trigger/recipient settings. Review the notification-group membership and holiday applicability for this organization before enabling. The legacy reminder-rule active flags remain a gate for corresponding defaults. Non-submission checks preserve the existing weekly timesheet-status definition; validate it against the organization's daily approval process and approved leave/eligibility rules during UAT. Holiday policy describes active Pulse users, not every account in Microsoft Entra.

The older Time Compliance dry-run screen and outbox-only actions remain non-sending. No historical dry-run queue or catalog-only notification is promoted. Do not run both the optional new reminder scheduler and an external/manual legacy scheduler for the same window without selecting the intended owner; they have separate deliberate source event identities.

## Rollout and acceptance

1. Review and pass the existing API/frontend/security checks, expanded MicrosoftTeamsDeliveryTests, inventory guard and disposable NotificationParityDatabaseTests. No live API workers or Microsoft transports are used in these tests.
2. Obtain normal migration/release approval. Apply and verify migration 128 before deploying the parity worker. Apply migration 129 only when adopting the optional new reminder sources. Existing configs are not changed by either migration.
3. Leave the working Power Automate OAuth audience/service principal and Compose message bindings unchanged. This payload keeps the existing envelope fields. PR #1201's additive source-label polish is separate; use its documented Compose adoption when that PR is approved.
4. Test an authorized representative event for each row in the inventory, using controlled UAT recipients. Confirm email result independently, Teams outbox status, Power Automate run, actual message text and application-link permissions.
5. Include failure cases: email transport fails but Teams proceeds; Teams fails but email succeeds; repeated event; concurrent workers; To/CC duplicate; inactive user; role removed; completed/reassigned FlowHive task; submitted time before escalation; holiday removed; quiet hours; large audience;429; interrupted send; Teams-only retry; locked/Test-only/View-As suppression.
6. Approve and enable production policies only after those results are recorded. Do not use a source CI pass as tenant delivery or production readiness evidence.

The Module 065 UI displays queue totals, per-recipient diagnostics and guarded Teams-only retry. GET `/api/microsoft-integration/teams/outbox` and POST `/api/microsoft-integration/teams/outbox/retry` use existing administrator authorization and environment scope.

## Rollback

Disable Teams delivery to stop the worker's live sends without disabling email. Disable/lock new reminder policies to stop their producers. A code rollback uses the normal approved release process. Migration 128's rollback refuses to remove nonempty notification evidence; retain/archive it through an approved procedure. Migration 129's rollback is non-destructive and preserves policy/event history. No secrets, tenant settings, Module 064 provider settings, production toggles or existing email history are changed by this PR.

## Validation commands

```sh
python3 tests/notification-parity/check-coverage.py
dotnet run --project tests/MicrosoftTeamsDeliveryTests -c Release
# Only with the explicitly named local disposable PostgreSQL fixture:
PARITY_POSTGRES_FIXTURE=YES dotnet run --project tests/NotificationParityDatabaseTests -c Release
node --test tests/teams-workspace/state.test.mjs
```

Microsoft reference: https://learn.microsoft.com/en-us/connectors/teams/ and https://learn.microsoft.com/en-us/power-automate/teams/send-a-message-in-teams.
