# Module 076 — Defect Intake & Resolution Tracker

Module 076 is the governed ProjectPulse defect register. It provides one US Signal-branded center for defects reported from:

- ProjectPulse Help;
- the Module 076 intake center;
- GitHub;
- Claude through GitHub; and
- ChatGPT through GitHub.

Every durable defect will receive a server-assigned ID in `DEF-{YYYY}-{SEQUENCE:000000}` format. The record includes status, description, category, priority, identity-backed assignee, reporter, source, affected module/route, date added, date resolved, server-calculated resolution time, comments, and GitHub linkage.

## Default ownership

The default assignee is Ahmed Adeyemi. Source resolves Ahmed through Module 062 by the configured email `PROJECTPULSE_DEFECT_DEFAULT_ASSIGNEE_EMAIL`, with `ahmed.adeyemi@ussignal.com` as the approved default. No user GUID is hardcoded. Ahmed, managers, administrators, Project Managers, and Project Team Coordinators can receive reassignment authority through the actual ProjectPulse session; View-As never transfers mutation authority.

## Notification contract

- After a defect is durably opened, an idempotent event is queued to the active manager audience.
- After the first durable Resolved or Closed transition, an idempotent event is queued to the original reporter.
- All delivery belongs to Module 067 Global Mail through a transactional outbox.
- Module 076 has no direct SMTP, Brevo, Microsoft Graph, Claude, or OpenAI client.

## Current source boundary

This checkpoint is **complete source, fail-closed runtime**. Read-only policy and identity endpoints are registered. Mutation-shaped endpoints return `423 Locked` before reading a request body. The source does not add a table, migration, repository adapter, outbox write, GitHub webhook secret, scheduled job, external notification, AI execution, deployment, or external-system change.

The front end can prepare and review a local intake draft. It cannot claim that a defect was saved, numbered, emailed, or synchronized until the separately governed persistence and integration phase is authorized.

## Required activation sequence

1. Approve a reviewed database schema and migration for defects, comments, transitions, source links, idempotency, and outbox events.
2. Implement a transactional repository that atomically allocates IDs, saves the defect, and writes notification events.
3. Connect Module 067 delivery for manager-open and reporter-resolution messages.
4. Provision a GitHub App or signed webhook secret through an approved secret store.
5. Validate repository allowlisting, signature verification, bounded payloads, delivery-ID deduplication, actor attribution, and rate limits.
6. Run role, negative-access, replay, date, notification, and reconciliation tests.
7. Obtain separate commit, merge, deployment, database, secret, SMTP, and GitHub activation authority as applicable.

## Protected behavior

Modules 002, 056E, 059, 062, and 064–074 remain protected. Module 076 uses Module 062 identity, Module 064 for any future AI triage, Module 067 for outbound mail, and the global Module 059 session contract without replacing their ownership.
