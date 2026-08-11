# Module 083 — Ask Celar AI Defect and External-Adapter Activation

## Relationship to Ask Celar AI

Ask Celar AI provides the user-facing troubleshooting and defect experience. Module 083 does not add another AI interface. It supplies the policy, idempotency, kill-switch, approval, adapter, and immutable-evidence controls required when a future action crosses the Pulse boundary.

## Initial package boundary

The initial Ask Celar AI operations package performs these Pulse-internal writes:

- Module 076 defect transaction;
- Module 076 evidence and lifecycle append;
- Module 076 notification outbox write;
- Module 078 probe, policy, and suppression records.

It performs no external mutation.

The following remain deferred:

- GitHub issue creation or update;
- GitHub workflow dispatch;
- Azure deployment or rollback;
- Oracle VM or service mutation;
- external notification delivery;
- autonomous repair execution.

## Existing Module 083 adapters

The autonomous control plane already catalogs:

```text
github
canary
azure_container_apps
azure_observability
module_076
module_065
celar_ai
```

This package consumes the conceptual ownership of those adapters but does not activate them. Module 083 remains fail-closed, dry-run-only, and governed by its global kill switch until a separate adapter implementation and activation is approved.

## Module 076 adapter contract

A future active Module 083 `module_076` adapter may support:

```text
defect_create
defect_update
repair_evidence_link
```

Required controls:

- exact Module 076 route allowlist;
- actual service identity;
- no AI identity as requester or approver;
- immutable idempotency key;
- environment and repository allowlists;
- bounded payload and sanitized evidence;
- no private document or secret content;
- default assignment policy;
- append-only execution evidence;
- global kill switch;
- rate limits;
- rollback or compensating-action design.

The initial in-process Test monitor does not depend on this external adapter. It writes through the Pulse-owned Module 076 service boundary.

## GitHub adapter contract

A future GitHub mirror requires:

- a least-privilege GitHub App or approved installation identity;
- repository allowlist limited to `ahmedadeyemi-cts/project-time-platform`;
- issue read/write only when needed;
- Actions read or dispatch separated from issue permissions;
- protected secret-store reference;
- no credential exposed to an AI model;
- exact request purpose;
- delivery and idempotency key;
- webhook signature validation;
- installation and repository validation;
- retry and rate-limit behavior;
- loop prevention between Module 076 and GitHub;
- reconciliation evidence;
- kill-switch support.

GitHub unavailability never prevents the Pulse Module 076 record from being created. Mirroring is an asynchronous secondary action.

## Availability incident policy

Module 083 may eventually coordinate:

```text
observe
classify
create_issue
notify
propose_repair
```

The AI may classify or summarize evidence, but deterministic policy decides whether an action is allowed. A model cannot request or approve an external mutation.

## Approval classes

Separate approval remains required for:

- Production changes;
- database migrations;
- security controls;
- infrastructure changes;
- secret creation or rotation;
- external adapter activation;
- GitHub issue or workflow writes;
- automated repair execution.

## Test activation sequence

1. Complete Ask Celar AI operations and Module 076/078 Test UAT without an external adapter.
2. Validate Module 083 dry-run plans for `create_issue` and `notify`.
3. Create an independent least-privilege adapter identity.
4. Store credentials in the approved protected secret store.
5. Enable read-only GitHub health first.
6. Validate rate limits and repository access.
7. Enable issue-write dry run with request capture but no external call.
8. Validate exact payload, redaction, idempotency, and loop prevention.
9. Activate Test issue mirroring for one allowlisted defect.
10. Validate Module 076-to-GitHub and GitHub-to-Module 076 reconciliation.
11. Disable the adapter after UAT unless an approved Test observation period continues.
12. Keep Production external execution disabled pending separate operating acceptance.

## Out-of-band watchdog

An external watchdog is required before Pulse can reliably report a complete Pulse API or database outage. The watchdog must be implemented as a separate, non-AI, least-privilege service with a bounded local incident spool and a replay path.

It is not active in this package. An approved watchdog implementation must include:

- signed service authentication;
- replay protection;
- timestamp and nonce validation;
- SQLite or equivalent bounded encrypted spool;
- no tokens, prompts, private documents, or raw response bodies in the spool;
- deduplication with Module 076 fingerprints;
- retry and backoff;
- kill switch;
- installation and rollback scripts;
- protected Test UAT before Production consideration.
