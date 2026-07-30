# Module 065 — Microsoft Entra Client-Secret Expiration Governance

## Purpose

Module 065 records and governs the expiration date of the Microsoft Entra application client secret used by ProjectPulse. The expiration-governance surface is intentionally separate from the secret itself.

ProjectPulse never accepts, displays, returns, logs, or stores the client-secret value through this surface. Administrators record only:

- application and environment name;
- a non-sensitive secret label;
- a non-sensitive version identifier;
- expiration date and time;
- reminder and critical-warning thresholds; and
- a required change reason.

## Lifecycle

1. An actual Administrator, Super Administrator, Integration Administrator, or explicitly authorized user publishes a new expiration profile.
2. ProjectPulse creates a new immutable generation and snapshots every active Project Team Coordinator recipient.
3. Beginning 30 days before expiration, the hourly evaluator determines which unacknowledged recipients are due for another reminder.
4. Each due recipient receives an individually governed notification through the existing Module 065 delivery boundary.
5. Acknowledgement is individual. It stops recurring reminders only for that recipient and profile generation.
6. At seven days before expiration, every authenticated user receives a red global warning at login and during the session.
7. Acknowledgement does not dismiss the critical global warning. Only publishing an updated version or expiration date starts a new generation and clears the old imminent-expiration state.
8. Profiles, recipient snapshots, acknowledgements, reminder events, and audit events are append-only evidence.

## Recipient and acknowledgement model

The recipient list is snapshotted when a profile is published. This preserves who was responsible at the time of the expiration decision even if later role assignments change.

Each recipient has one acknowledgement record per profile generation. A recipient cannot acknowledge on another user’s behalf. Administrator View-As is read-only and cannot publish or acknowledge.

## Reminder delivery

The reminder worker:

- runs hourly in each application revision;
- uses a PostgreSQL advisory lock so only one revision evaluates reminders at a time;
- creates one claim per recipient and reminder interval;
- uses the existing Module 065 mail delivery service and Test/Production recipient boundary;
- records the dispatch and sanitized delivery result;
- never places the client-secret value in subject, body, metadata, or logs; and
- continues reminders until that specific recipient acknowledges.

A manual **Evaluate reminders now** action uses the same guarded evaluator. It does not bypass the configured delivery boundary.

## Global warning

The all-user warning begins at the configured critical threshold, which defaults to seven days. It remains active when the secret is expired. The UI polls the read-only status endpoint periodically and refreshes when a new profile is published.

The warning states that ProjectPulse authentication and Microsoft Graph services are at risk and links users to Module 065. Only authorized administrators can change the profile.

## APIs

```text
GET  /api/entra-secret-expiration/status
GET  /api/entra-secret-expiration/profile
PUT  /api/entra-secret-expiration/profile
POST /api/entra-secret-expiration/acknowledge
POST /api/entra-secret-expiration/reminders/run
```

All write endpoints use the actual ProjectPulse session and reject Administrator View-As.

## Migration 056 objects

```text
entra_secret_expiration_profile_versions
entra_secret_expiration_state
entra_secret_expiration_recipients
entra_secret_expiration_acknowledgements
entra_secret_expiration_reminder_claims
entra_secret_expiration_reminder_events
entra_secret_expiration_audit_events
```

The singleton state pointer and reminder claims are operationally mutable. Profile versions, recipient snapshots, acknowledgements, reminder events, and audit events are immutable.

## Activation boundary

The source package does not apply migration 056, send a live reminder, rotate a Microsoft secret, call Microsoft Graph, change Azure, deploy Test or Production, or modify a client-secret value. Those actions remain separate migration, configuration, and release operations.
