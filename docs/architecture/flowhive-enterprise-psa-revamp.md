# FlowHive enterprise PSA revamp

Status: draft implementation and release contract, 2026-09-19.
Owner request: Ahmed Adeyemi. Module 066. Target: Protected UAT after review.

## Product outcome

A PM selects a project's authoritative SOW and receives executable, project-specific
work in Plan, Design, Implement, Validate, and Release. A forecast explains when
that work can finish given dependencies, effort, staffing, calendars, and approved
scope. Engineers, PMs, Account Executives, Solution Architects, and leadership see
the same project facts through their authorized role views.

This is a coordinated redesign, delivered in reviewable increments. The first
increment is not the completed PSA release. Keep its PR draft while the full release acceptance gates remain incomplete. Do not represent
unimplemented integrations as available, successful, or delivering messages.

## Current implementation inventory

Already on main at c15ef12d5ce1bc54c15d8b31c87a50daa94bad17:

- Executable WBS builder rejects generic scaffolds, validates current citations,
  creates phase-native tasks once, validates dependencies, and preserves effort.
- Reviewed regeneration preserves existing work through an explicit merge preview.
- Working copies, immutable versions, baselines, schedule preview, Gantt, Kanban,
  financial controls/readback, RAID, exports, and governed customer sharing.
- Meetings contains recordings. Task reminders explicitly report unavailable.
- Schedule calculation explicitly remains a weekday preview, not a capacity-aware
  commitment. A calendar display does not change that scheduling authority.

Implemented in this first increment:

- Canonical Account Executive name/identity and visible source document count in
  the scoped portfolio; search includes the AE. No duplicate contact directory.
- Empty-document AI Planner guard; existing server evidence gates remain authoritative.
- Compact WBS description previews and collapsed milestone section.
- Read-only project-team calendar, current week by default, previous/next week,
  month navigation, date picker, team-member filter, and refresh.
- Server resolves PM/AE/SA, canonical assignments, active planning collaborators,
  and saved WBS assignees inside an authorized project. Request cannot inject
  unrelated mailbox IDs. Existing FlowHive project authorization is reused.
- Microsoft Graph batches at most 20 distinct mailboxes; query range at most 42
  days with exclusive end. Event subjects, locations and bodies are not returned.
  Missing credentials, consent, mailbox errors and partial failures remain unknown.
- UTC is explicit throughout this increment; timezone selection is still required.

## Full release requirements and acceptance gates

### Planning and AI assistance

- [x] Preserve the existing executable WBS and reviewed-regeneration contracts.
- [ ] Demonstrate real SOW-to-WBS output in Protected UAT using an approved project.
- [ ] Add SOW deliverable coverage report: mapped tasks, exclusions, assumptions,
      missing facts and unanswered questions. Every committed deliverable is covered.
- [ ] Support intentionally inapplicable phases without manufacturing tasks; preserve
      all five phase headings and require a PM reason when a phase has no work.
- [ ] Task/subtask hierarchy, stable IDs, multiple predecessors, lag/lead, effort,
      duration, assignments, priority, status, acceptance and source references.
- [ ] AI explains proposed task estimates and confidence; source facts are separated
      from estimates. Missing evidence cannot silently become a successful template.
- [ ] AI progress summaries, blocker explanations, status-report drafts, risk and
      dependency suggestions, and proposed next actions with project citations.
- [ ] AI proposed changes show a diff and impact. Publishing, assigning, updating
      customer commitments, or sending communications requires the appropriate user action.
- [ ] Preserve Module 064 provider ordering, health-based skipping, bounded execution,
      cancellation, progress state, and error visibility. Avoid a separate AI router.
- [ ] Regeneration never overwrites actual time, completed tasks, approved baselines,
      manual work, or existing assignment identity without reviewed reconciliation.

### Forecast and resource management

- [ ] Calculate dates from dependencies, effort, allocations and working calendars.
- [ ] Incorporate holidays, PTO, meetings, change windows, external wait time and
      cross-project assignments without counting the same unavailable hours twice.
- [ ] Distinguish effort hours, elapsed duration, working duration and wait time.
- [ ] Show critical path, float, staffing conflicts, baseline/current/forecast dates,
      finish variance and the exact drivers of change.
- [ ] What-if scenarios remain isolated until approved. Leveling does not silently
      reassign engineers or move a published customer date.
- [ ] Preserve baseline and financial history, audit changes and support rollback.

### Team and role experience

- [x] Show AE alongside PM in portfolio and selected-project context.
- [ ] PM overview: health, upcoming work, overdue work, unassigned work, blockers,
      decisions, milestones, forecast finish, budget burn, and stakeholder ownership.
- [ ] Engineer My Work: authorized assigned tasks, priorities, dates, acceptance,
      progress updates, blockers, evidence and task-linked timesheet entry.
- [ ] AE view: project health, delivery commitments, customer decisions, scope
      changes and commercial risk; retain existing financial permission boundaries.
- [ ] SA view: design questions, technical dependencies and change review.
- [ ] Leadership view: portfolio capacity, forecast variance and project health.
- [ ] Accessible dense grid, frozen columns, filters, sorting, task details panel,
      consistent empty/error states and responsive layouts.

### Email and Microsoft Teams

- [ ] One durable project event authority with independent email and Teams delivery
      records, retries, backoff, deduplication, suppression reason and delivery history.
- [ ] Assignment/reassignment: notify new assignee only when the change is published.
      Draft generation and unchanged baseline publication must not send duplicates.
- [ ] Reminders: 3 calendar days before due, due day, and first day overdue in the
      project's timezone; configurable later escalation and digest cadence.
- [ ] Late assignment inside the reminder window sends the assignment immediately;
      do not emit stale three-day reminders for a date that already passed.
- [ ] Changed due date cancels obsolete pending reminders. Completion, cancellation,
      reassignment, project closure and revoked access are rechecked before delivery.
- [ ] Include PM on overdue/escalation; AE receives relevant commercial, milestone,
      customer decision and scope events rather than every engineering update.
- [ ] Add blocked tasks, material schedule changes, approvals, document/SOW version
      changes, RAID changes, meeting updates, budget thresholds and closeout events.
- [ ] Event preferences by project, recipient, severity and channel, with quiet hours,
      digest options and documented escalation rules. Required assignment email stays on.
- [ ] Email uses Module 065 transport and recipient policy; do not add direct SMTP.
- [ ] Teams personal activity notifications use an installed Teams app and approved
      Graph permissions; a channel workflow is a separate optional destination.
- [ ] Teams cards link to the authorized task/project. Interactive updates reauthorize
      the actor on the server and are not trusted merely because a card was delivered.
- [ ] Map users by canonical Entra identity; configure team/channel IDs per project.
      Resolve recipients at delivery, validate channel membership for project data,
      redact private finance fields, and avoid customer cross-project disclosure.
- [ ] Teams tenant/app installation, consent, destination and transport readiness are
      explicit. An email connection alone does not make Teams delivery available.
- [ ] Protected UAT records/suppresses outbound delivery unless the existing governed
      test policy explicitly permits an approved test destination. No unsolicited sends.
- [ ] Offline retries and duplicate worker claims are covered by integration tests;
      queue acceptance is not displayed as confirmed end-user delivery.

Teams implementation references checked on 2026-09-19:

- https://learn.microsoft.com/en-us/graph/teams-send-activityfeednotifications
- https://learn.microsoft.com/en-us/microsoftteams/platform/webhooks-and-connectors/how-to/add-incoming-webhook

Prefer the installed app for personal notifications. If channel Workflows are used,
configure continuity/co-owners and current authentication requirements. Do not
introduce a new dependency on retiring Microsoft 365 connectors. Selection of a
transport does not authorize tenant configuration or live notification sends.

### Meetings and calendar

- [x] Project-scoped week/month availability navigation and member filter.
- [x] Explicit unknown availability, safe free/busy display and partial failure handling.
- [ ] Configurable timezone, user working hours, PTO and conflict-aware suggestions.
- [ ] Meeting creation, required/optional attendees, invitations, RSVP, cancellation,
      rescheduling, recurrence, online meeting link and external customer participation.
- [ ] Detect changed availability before booking; do not automatically create meetings
      merely because the PM views the calendar.
- [ ] Link meetings, minutes, recordings, decisions and action items to the project/WBS.
- [ ] Private transcription/action extraction with review before publishing tasks.
- [ ] Calendar refresh/webhook reconciliation, subscription renewal, observable token
      failures and tenant-specific identity configuration verified in Protected UAT.

### Documents, finance and operations

- [x] Portfolio document count and a no-document planner guard.
- [ ] Portfolio approved SOW/GSD version, processing/scan failure and AI-ready status.
      Document existence and usable planning evidence are different states.
- [ ] Estimate/budget/baseline/actual/remaining/forecast cost reconciliation, role-aware
      financial access, rates/currency, expense and timesheet integration.
- [ ] Scope change requests with impact, customer approval, baseline revision and
      handoff to existing billing/ConnectWise SELL workflows where appropriate.
- [ ] Project acceptance, handoff and closure checklists with accountable ownership.
- [ ] Full tenant/project/role isolation, stale-write handling, immutable audit,
      job observability, accessible exports and explicit integration health.

## Validation and rollout

First increment must compile backend and production frontend, preserve the existing
executable planner tests, and exercise calendar date ranges, cross-midnight events,
unknown calendars, week/month navigation, refresh and project-switch races.

Protected UAT must additionally verify SQL against the real migration set, project
scope for PM/engineer/AE/SA/admin/View-As, private-event redaction, calendar permissions,
20+ mailboxes and partial Graph failures. No external messages are sent by this
increment. Email/Teams workers and full capacity forecasting are not implemented by
these display/calendar changes and remain explicit release blockers above.

Rollout must preserve existing project IDs, task IDs, time entries and baseline
history. No destructive test-data cleanup or production deployment is part of this PR.
