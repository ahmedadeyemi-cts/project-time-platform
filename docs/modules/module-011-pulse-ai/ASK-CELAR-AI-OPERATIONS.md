# Ask Celar AI — Operational Intelligence, Troubleshooting, and Defect Orchestration

## Product decision

**Ask Celar AI is the user-facing entry point for every Celar AI capability.** Users should not have to understand which internal module, model, database, retrieval service, monitor, or external provider is involved.

The operating model is:

```text
User
  ↓
Ask Celar AI
  ├── answer an internal Pulse question
  ├── retrieve and cite authorized documents
  ├── answer a governed public question
  ├── troubleshoot a system or module
  ├── open or continue a guided defect questionnaire
  ├── search for an existing Module 076 defect
  ├── add sanitized evidence to a defect
  └── review protected-Test health and automatic-defect policies
```

Module 076 remains the durable defect system of record. Module 078 owns health policy and threshold meaning. Module 083 owns policy-bounded automation and future external adapters. Module 067 owns outbound mail. Module 064 remains the governed AI-provider authority. Module 062 remains the user-identity authority.

This package does not create a second chatbot, a separate troubleshooting application, or a competing defect store.

## User experience

The global Ask Celar AI assistant receives three new operational surfaces:

1. **Troubleshoot** — runs allowlisted read-only diagnostics and returns current evidence.
2. **Defect questionnaire** — asks a governed series of questions and creates a Module 076 record only after the actual user reviews and confirms it.
3. **Health & automation** — displays versioned Test thresholds, monitor status, automatic-defect activation state, and Test-only fault injection.

Every normal Ask Celar AI response also exposes two actions:

- **Troubleshoot with Ask Celar AI**
- **Open guided Module 076 defect**

Natural-language commands such as `open a defect`, `report this issue`, `this is broken`, `troubleshoot`, `diagnose`, and `run diagnostics` open the appropriate Ask Celar AI operational surface rather than navigating the user to an unrelated form.

## Operational API

All routes are beneath the Ask Celar AI namespace:

```text
GET    /api/celar-ai/v1/operations/readiness
POST   /api/celar-ai/v1/operations/troubleshoot
GET    /api/celar-ai/v1/operations/defects
GET    /api/celar-ai/v1/operations/defects/matches
GET    /api/celar-ai/v1/operations/defects/{defectNumber}
POST   /api/celar-ai/v1/operations/defects/{defectId}/evidence
POST   /api/celar-ai/v1/operations/defects/intake-sessions
GET    /api/celar-ai/v1/operations/defects/intake-sessions/{sessionId}
PATCH  /api/celar-ai/v1/operations/defects/intake-sessions/{sessionId}
POST   /api/celar-ai/v1/operations/defects/intake-sessions/{sessionId}/submit
GET    /api/celar-ai/v1/operations/monitor-policies
POST   /api/celar-ai/v1/operations/monitor-policies/{policyCode}/automatic-defects
POST   /api/celar-ai/v1/operations/synthetic-failures
```

The established production chat route remains authoritative:

```text
POST /api/celar-ai/v2/chat
```

The operational routes supplement that route. They do not replace Module 064 provider routing, private RAG, the universal answer reliability gate, or durable conversation history.

## Troubleshooting behavior

Ask Celar AI may run only approved, read-only checks. Initial adapters include:

- Pulse database reachability and a bounded `SELECT 1` check;
- authenticated Oracle Celar AI runtime readiness;
- Module 064 Ask Celar AI route availability;
- exact-host GitHub repository metadata access;
- Module 067 defect-notification outbox health; and
- deployment-managed, same-origin Pulse health endpoints.

A troubleshooting response contains:

```text
Direct conclusion
Evidence observed
Probe status and timestamp
HTTP status and latency when applicable
Sanitized failure code
Likely causes
Limitations and unavailable sources
Recommended next actions
Correlation ID
Existing-defect search action
Guided-defect action
```

The troubleshooting service does not:

- execute unrestricted shell commands;
- permit model-generated SQL;
- expose response bodies containing secrets;
- send internal diagnostic evidence to Claude or OpenAI;
- mutate GitHub, Azure, Oracle, Pulse infrastructure, or a customer system;
- treat a model-generated diagnosis as an authoritative fact without probe evidence.

## Guided defect questionnaire

The questionnaire is conversation-oriented and form-backed. It preserves progress in a durable, owner-scoped intake session for 24 hours.

### Step 1 — Where is the problem?

- environment;
- affected system;
- module;
- route;
- correlation ID;
- release SHA.

### Step 2 — What happened?

- short summary;
- detailed description;
- expected behavior;
- actual behavior.

### Step 3 — Can it be reproduced?

- ordered reproduction steps;
- frequency and timing may be included in the steps or description;
- the user must not enter credentials, session tokens, cookies, or private document content.

### Step 4 — What is the impact?

- category;
- priority;
- business or user impact;
- known workaround.

### Step 5 — Supporting evidence

Ask Celar AI displays the sanitized diagnostic evidence that will be stored. It excludes:

- bearer tokens;
- cookies;
- connection strings;
- passwords and secrets;
- raw prompts;
- raw tool response bodies;
- raw private documents;
- embedding vectors;
- storage paths.

### Step 6 — Review and create

The user sees the complete record, the Module 076 destination, and the default assignment. Durable creation requires:

```text
UserConfirmed = true
ConfirmationText = CREATE DEFECT
ActualUserId = EffectiveUserId
```

The AI may prepare and summarize the record, but the AI is not the requesting or approving authority.

## Default assignment

Every new user-created or machine-created defect defaults to:

```text
Name: Ahmed Adeyemi
Email: ahmed.adeyemi@ussignal.com
Identity source: Module 062 / app_users.user_id
Configuration: PROJECTPULSE_DEFECT_DEFAULT_ASSIGNEE_EMAIL
```

Creation fails closed when that email cannot be resolved to an active Module 062 identity. No Ahmed user GUID is hardcoded.

## Defect numbering and persistence

Migration 084 adds the durable Module 076 operational schema. Defects receive a server-owned identifier:

```text
DEF-{YYYY}-{SEQUENCE:000000}
```

The browser cannot submit an official defect number, creation timestamp, resolution timestamp, resolution duration, reporter identity, or automatic-monitor fingerprint.

Durable source tables include:

- defects;
- comments;
- status events;
- sanitized evidence;
- intake sessions;
- incident occurrences;
- monitor policies;
- probe results;
- suppressions; and
- notification outbox events.

Evidence, events, incident occurrences, and probe results are append-only.

## Permission model

### Ask permission

Every active authenticated user may use the core Ask Celar AI assistant. The underlying module and record scope still applies to every source.

### Defect reads

Ordinary users see only defects they reported or are assigned. Authorized managers and administrators may view all records according to existing Module 076 permissions.

### Defect mutations

- Administrator View-As is read-only.
- The actual user creates a defect after confirmation.
- Evidence may be added only by the reporter, assignee, or an authorized defect manager.
- Automatic defects use a governed monitoring service identity, never an AI identity.

### Monitor activation

Only administrators, release managers, security administrators, or users with the approved management permissions may enable per-policy automatic defect creation in protected Test.

Production automatic activation is rejected by source policy even when an environment variable is set incorrectly.

## Automatic monitoring

Two controls must be enabled before a monitor may create a durable defect:

1. deployment-level Test flag:

```text
PROJECTPULSE_CELAR_AI_AUTOMATIC_DEFECTS_ENABLED=true
```

2. the individual versioned policy row:

```text
machine_creation_enabled=true
```

Every seeded policy starts in observe-only mode. Synthetic failure injection also requires a separate Test-only flag:

```text
PROJECTPULSE_CELAR_AI_SYNTHETIC_FAILURES_ENABLED=true
```

## Deduplication, recovery, and flapping

A machine incident uses a stable SHA-256 fingerprint derived from:

```text
environment + component + policy + release SHA
```

The fingerprint prevents more than one active machine defect for the same governed incident. Additional failures append occurrences, evidence, and comments to the existing defect.

Initial controls include:

- at most ten new automatic defects per hour;
- three consecutive successful probes before recovery;
- fifteen minutes of stable service before automatic resolution;
- user-created defects are never automatically resolved;
- recurrence during the flapping window reopens the same machine defect;
- repeated flapping escalates priority;
- idempotent notification keys prevent duplicate mail events.

## Notification contract

The defect transaction writes an outbox event. It does not send mail directly.

- defect opened or reopened: active manager audience and default assignee;
- defect resolved: default assignee and original reporter;
- flapping escalation: active manager audience and default assignee.

Module 067 owns delivery and retry behavior.

## GitHub boundary

The initial package performs a read-only repository health check against the exact allowlisted repository:

```text
ahmedadeyemi-cts/project-time-platform
```

It does not create a GitHub issue. Future GitHub mirroring requires the separately governed Module 083 GitHub adapter, least-privilege installation identity, signed webhook processing, delivery deduplication, loop prevention, and a protected secret reference.

A GitHub outage is recorded first in Module 076. Mirroring waits until GitHub is available.

## Fault injection

Protected Test may simulate allowlisted failures without taking a real service offline. The harness records synthetic probe evidence and evaluates the same threshold, deduplication, assignment, outbox, recovery, and privacy logic used by normal monitoring.

Production cannot enable or execute the synthetic harness.

## Current implementation status

This branch contains the source, migration, rollback, UI, tests, and CI. It does **not** by itself:

- apply migration 084;
- deploy the API or frontend;
- enable automatic monitoring;
- enable a monitor policy;
- run synthetic failures;
- create a Production defect;
- install or change Oracle services;
- create a GitHub App;
- activate GitHub issue mirroring;
- send Module 067 mail;
- change Production.

## Protected Test activation sequence

1. Merge the universal answer reliability dependency after all checks pass.
2. Merge this package after source, API, frontend, migration, and privacy tests pass.
3. Apply migration 084 to protected Test only.
4. Deploy the exact merged API and frontend images.
5. Verify Ask Celar AI readiness and Module 076 inventory.
6. Run troubleshooting probes in observe-only mode.
7. Create a user-confirmed questionnaire defect and verify default assignment.
8. Verify permission-scoped reads and View-As blocks.
9. Enable the Test-only synthetic harness.
10. Test each allowlisted scenario with automatic creation still disabled.
11. Enable selected Test monitor policies one at a time.
12. Verify threshold crossing, deduplication, notification outbox, recovery, flapping, and rate limiting.
13. Disable automatic creation and synthetic failure flags after UAT unless continuing an approved Test observation period.
14. Require separate approval before any Production design or activation.
