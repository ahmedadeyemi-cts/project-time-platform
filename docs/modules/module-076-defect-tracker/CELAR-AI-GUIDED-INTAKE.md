# Module 076 — Ask Celar AI Guided Defect Intake

## Ownership

Module 076 is the durable defect system of record. **Ask Celar AI is the primary user experience** for troubleshooting, guided intake, duplicate search, and sanitized evidence attachment.

The separation is intentional:

```text
Ask Celar AI
  → asks questions
  → runs authorized diagnostics
  → prepares and validates the draft
  → obtains actual-user confirmation
  → calls the governed Module 076 transaction

Module 076
  → allocates the official defect number
  → stores reporter and assignee identities
  → stores lifecycle and append-only evidence
  → queues notification events
  → exposes permission-scoped records
```

## Default owner

Every defect is initially assigned to the active Module 062 identity resolved by:

```text
PROJECTPULSE_DEFECT_DEFAULT_ASSIGNEE_EMAIL
```

Approved fallback:

```text
ahmed.adeyemi@ussignal.com
```

The expected display name is `Ahmed Adeyemi`. A GUID is never hardcoded. When the email is not an active Pulse identity, creation fails closed.

## Defect contract

The server allocates:

```text
DEF-{YYYY}-{SEQUENCE:000000}
```

A record contains at least:

- official defect number;
- title and description;
- category and priority;
- lifecycle status;
- source channel;
- environment;
- affected system, module, and route;
- expected and actual behavior;
- reproduction steps;
- business impact and workaround;
- actual and effective reporter identity;
- Module 062-backed assignee identity;
- correlation ID and release SHA;
- machine/user origin;
- occurrence and flapping counts;
- server timestamps and resolution duration;
- revision number;
- sanitized metadata.

## Questionnaire states

```text
draft
ready_for_review
submitted
cancelled
expired
```

Only the actual owning user may read or change an intake session. The database requires `actual_user_id = effective_user_id`, so Administrator View-As cannot create or submit a defect.

## Required user confirmation

A user-created defect is not saved merely because Celar AI recommends it. Submission requires the browser to send:

```text
UserConfirmed: true
ConfirmationText: CREATE DEFECT
ExpectedRevision: current intake revision
```

The server then revalidates the record, resolves the reporter and assignee, allocates the number, saves the defect and evidence, appends the creation event, and queues the notification inside one transaction.

## Sanitized evidence

The evidence table enforces:

```text
contains_secret = false
raw_private_content_stored = false
```

Allowed evidence is a bounded, sanitized summary such as:

- probe name and status;
- HTTP status;
- latency;
- correlation ID;
- failure code;
- observed timestamp;
- release SHA;
- approved source reference.

Prohibited evidence includes:

- authorization headers;
- bearer tokens;
- cookies;
- passwords;
- connection strings;
- raw private document text;
- raw prompts or complete conversations;
- embedding vectors;
- unrestricted log or database dumps.

## Record visibility

Ordinary users may view defects where they are the actual reporter or current assignee. Existing Module 076 management permissions allow authorized management and administrative roles to view all records.

A user cannot obtain broader defect visibility by using View-As, changing browser fields, or asking a public AI provider.

## Automatic incidents

Machine-created defects use source channel `availability_monitor` or `watchdog_replay`. They are attributable to a governed monitoring service, not an AI model.

Machine defects:

- use the same Ahmed default assignment;
- carry a stable fingerprint;
- deduplicate repeated failures;
- append each occurrence;
- may resolve only after the configured recovery policy passes;
- may reopen and escalate when the incident flaps;
- never cause a user-created defect to close automatically.

## Notification outbox

Module 076 writes notification events atomically with the relevant lifecycle change. Module 067 delivers them later.

The outbox has unique idempotency keys. A repeated request cannot send duplicate open, reopen, escalation, recovery, or resolution notices.

## GitHub integration

Module 076 is authoritative even when GitHub is unavailable. Initial source includes no GitHub issue write.

A future mirror may be activated only through the governed Module 083 GitHub adapter after:

- exact repository and installation allowlisting;
- least-privilege credentials;
- webhook signature validation;
- delivery-ID deduplication;
- source attribution;
- rate limiting;
- loop prevention;
- reconciliation tests.

## Migration and rollback

Migration 084 creates the durable operational schema. Its rollback refuses to remove the schema whenever durable defects, comments, evidence, intake sessions, occurrences, probe results, suppressions, or notification records exist.

A clean unused installation can be rolled back. A used system requires an explicit data-retention and migration-forward decision.

## Test acceptance

Protected Test acceptance requires:

- one complete questionnaire defect;
- Ahmed default assignment resolved from Module 062;
- server-generated defect number;
- correct reporter identity;
- View-As mutation blocked;
- ordinary-user record scope enforced;
- sanitized diagnostic evidence stored;
- manager/assignee notification queued;
- no GitHub issue created by the transaction;
- duplicate submit returns the existing idempotent record;
- Module 076 register displays the created defect;
- all operations remain available from Ask Celar AI.
