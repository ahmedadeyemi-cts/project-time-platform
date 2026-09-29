# FlowHive collaboration and request validation review (PR 1212 extension)

## Requested outcomes and current delivery boundary

The PR extends the existing customer-sharing/Project Home changes with multi-person assignments, a project team/contact directory, visible Critical Path Method information and unsent meeting planning. It is source in a draft pull request, not an installed UAT release. Migration 131 requires review and normal release application before contacts or meeting drafts can be used. No customer information, customer access, meeting invitation, credential or production configuration was changed during development.

## HTTP 400 findings: confirmed versus reproduced

The supplied browser trace confirms PUT `/api/project-flowhive/projects/{project}/working-copy` returned 400. Earlier evidence confirms the customer-sharing action's PUT `/controls` also returned 400. The original rejected JSON and response body were not supplied. The exact live offending field and a complete count of live errors cannot be proven from those request/status lines. No production telemetry query or destructive live reproduction was performed.

Actual ASP.NET binding tests reproduce a 400 when an optional DateOnly receives an empty string. The existing controls record also rejects an empty-string nullable budget. Fractional values for whole-working-day durations, malformed dates and invalid UUIDs are separate verified failure cases. These are transport binding failures before a typed handler can explain the input. Unknown extra metadata alone is not asserted to be the cause.

| FlowHive path | Source finding and disposition |
| --- | --- |
| Working-copy save | Raw form state was posted to a typed record. The new client helper normalizes cleared optional dates/IDs, validates numeric types and preserves full task detail. The server authenticates first and uses narrow optional-empty compatibility with a field-specific error, rather than globally relaxing JSON or project security. |
| Financial controls | The prior PR change uses an explicit ten-field request. Empty nullable money becomes null; invalid numbers are rejected locally. Customer-sharing enablement no longer posts this form. |
| Validate, calculate, immutable save | These shared plan-bearing POSTs now use the same transport normalization. Logical validation, cycles, impossible constraints and invalid estimates remain visible and are not suppressed to make a test green. An editable working copy can retain semantic issues for review; an immutable valid version/baseline is still governed separately. |
| Board schedule and artifact exports | They posted the same raw plan through a separate helper. They now normalize that plan too, and Board displays every task assignee rather than only the last assignment. |
| Customer links and baseline approval | Reviewer-approved baseline, selected plan/project, expiration and role requirements stay mandatory. A rejected unreviewed link is not a defect to bypass. |
| Meeting recording upload | Existing multipart/MP4, signature, size and title validation remains separate from planning a meeting. Invalid media should remain rejected. |
| Team calendar and reminder settings | Existing bounded date ranges, recognized timezone, finite lead-day list and governed delivery-boundary checks remain intentional. External contacts' private calendars are not assumed available. |
| RAID and status reports | Existing required titles, summary content, project scope and role checks remain. The common response parser now retains structured fields and correlation references where supplied. |

The visible error region names the field/WBS item and keeps unsaved edits. A stale row-version returns a conflict; it is not automatically overwritten or retried. The request reader rejects null/scalar task-list rows and bounds input rather than letting downstream code dereference invalid rows.

## Multi-person task assignments

Each WBS task now has a search/filter picker with independent add/remove selections. Internal users retain Module 062 IDs; external people use a separate projectContactId with resourceUserId null. Existing assignees on the selected task and all other tasks are preserved. Per-person hours/allocation are editable. A new person starts at zero planned hours so adding another engineer does not silently duplicate the entire task estimate. Existing hours and allocations are never redistributed automatically.

WBS, Board and Project Home use all assignees. A real external contact counts as assigned work but never as an internal user's My work. External names are resolved from active contacts in the selected project before working-copy or immutable persistence. Forged names, dual internal/external IDs, other-project contacts and inactive contacts cannot create a new external assignment. Existing internal assignment notification rules continue to consume internal IDs only; contact creation/assignment does not send external email or Teams messages.

## Project team and customer information

Project Home shows canonical PM, AE, SA, effective project assignments and active planning collaborators with available names, role/title, email and phone. A client-supplied WBS identity is not sufficient to disclose an unrelated person's directory details. The roster read is project-authorized.

`Create customer info` saves one project contact at a time: name/email required; phone, title and organization optional; Customer, Vendor or Partner type. It is a person record, not a new customer organization or application account. There are at most 15 active external contacts per project. Duplicate active email addresses are disallowed, the cap is enforced inside a database project lock and a trigger, and updates require the expected row version. Archive preserves history and is blocked while the contact remains referenced in the current WBS. Remove that assignment and save the WBS first.

All contact/meeting writes require the existing project governance permission and active-project lock. Engineering-only collaborators and View-As cannot administer contacts or meeting drafts. Contact data is not inserted into the customer-shared plan or internal identity directory. The old client-level contact table and its own existing limits are unchanged.

## Critical Path Method is already implemented

The existing deterministic schedule engine already has forward/backward passes, dependencies FS/SS/FF/SF, lead/lag, early/latest starts, total/free float, critical flags and cycle rejection. This change surfaces that calculation on Project Home and above the WBS, adds Critical tasks only filtering and per-task critical/float indicators, and links to Timeline & risk.

A diamond regression distinguishes the longer controlling branch from the shorter branch and verifies four days of total float. A dependency cycle still fails. The engine remains a weekday preview: company holidays, individual leave, resource calendars and resource leveling are NOT applied. WBS changes invalidate the displayed calculation until recalculated; stale results must not be treated as a committed finish. All tasks being critical can be correct when the plan is fully serial; the dependency model should be reviewed rather than changing flags by hand.

## Meetings: draft planning, not live invitations

The implemented first stage offers `Plan meeting` from Project Home and the Meetings tab. It selects current project team members and active project contacts, records start/end, resolved browser timezone, location/approved meeting URL and a customer-visible agenda. The stored record is explicitly draft with invitationSent=false. An authorized PM can download a tentative, UTF-8-folded calendar file and review it in the calendar application. Current attendee scope is checked again at export. No attendee lookup accepts a raw arbitrary email address and no mail/calendar provider is called.

Direct Outlook invitations, new Teams join links, recurring series, RSVP tracking, calendar conflict resolution, rescheduling and cancellation are NOT implemented by this PR. Those need a separate governed calendar/event workflow with explicit Send confirmation, provider authorization, idempotent event identity and retained send/cancel outcomes. The Teams notification cloud flow is not a substitute for calendar-event creation. External availability remains unknown unless separately authorized.

## Test evidence and rollout acceptance

Local tests exercise real components and API binding with synthetic fixtures, not the live tenant. The new C# suite also runs actual store methods against disposable PostgreSQL in CI. It checks financial/data preservation, contact limits, concurrency, row-version conflicts, project isolation, no account creation, invalid attendee scopes, calendar escaping, transaction rollback and evidence-preserving migration rollback.

Before release: review migration 131 and its restricted-role grants; reconcile the normal protected-release source scope; require all relevant exact-head CI checks. Then use controlled UAT project fixtures for save/reload, two internal plus one external assignment, removal of just one person, contact limit and update conflict, View-As/engineer denial, stale project response, clearing an optional date, specific invalid-duration feedback, CPM calculation, meeting draft/export and no unintended invitation. No live customer sharing should be enabled merely to test a WBS save.
