# Module 076 Data and Identifier Contract

## Identifier allocation

- Canonical format: `DEF-{YYYY}-{SEQUENCE:000000}`.
- Allocation happens once, atomically, in the same transaction that creates the durable defect.
- The sequence is concurrency-safe and cannot be supplied or changed by a client.
- Failed transactions do not create a visible defect.
- GitHub issue numbers are external links, not ProjectPulse defect IDs.

## Required durable entities

The activation design requires separately reviewed entities for:

1. defects;
2. append-only status and assignment transitions;
3. comments;
4. GitHub issue/delivery links and deduplication;
5. notification outbox events; and
6. sanitized audit evidence.

No schema or migration is included in the current source checkpoint.

## Core columns

| Field | Ownership | Rule |
|---|---|---|
| Defect ID | Server | Atomic and immutable |
| Status | Server workflow | Open, In Progress, Blocked, Resolved, Closed, Reopened |
| Description | Reporter/authorized editor | Bounded and sanitized |
| Category | Reporter/triage | Governed allowlist |
| Priority | Reporter/triage | Critical, High, Medium, Low |
| Assignee | Module 062 identity | Ahmed by default; stable user ID; authorized reassignment |
| Raised By | Actual session or signed GitHub actor | Immutable origin plus display snapshot |
| Source | Intake adapter | Help, tracker, GitHub, Claude-through-GitHub, ChatGPT-through-GitHub |
| Date Added | Server | UTC at durable creation |
| Date Resolved | Server | UTC at first Resolved or Closed transition; cleared on reopen with history retained |
| Resolution Time | Server | `dateResolved - dateAdded` |
| Comments | Append-only | Author, time, bounded text, source, edited state |

Deletes are not part of the normal lifecycle. Incorrect records are closed with auditable reason; retention and purge require a separately approved policy.
