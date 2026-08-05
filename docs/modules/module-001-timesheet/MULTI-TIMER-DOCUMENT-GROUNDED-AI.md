# Module 001 Multi-Timer and Document-Grounded AI Description Contract

## Purpose

This package replaces the single 12-hour Start / Stop Timer experience with a production-governed workspace that can run as many as five distinct activity timers for the authenticated user. It also makes the existing Generate AI Suggestion action private-document-aware for project tasks and service requests.

The package does not merge, deploy, apply migration 057, change Azure resources, configure an AI provider, or authorize an autonomous business write.

## Engineer experience

The activity control is a searchable, grouped checkbox picker rather than an exposed collection of buttons. It searches customer, project, task, task code, request number, and non-project category across these groups:

- Requests / Service Requests
- Project Tasks
- Non-Project Time

The engineer may select only as many new activities as there are available timer slots. A selected activity is displayed as a removable chip. An activity that already has a running timer remains visible but is disabled and marked **Running**.

A batch start uses one server timestamp for every selected activity. The timer workspace then presents one card per active activity, each with its own elapsed clock, editable description, Generate AI Suggestion action, Stop button, and Discard button. Stop All is also available.

## Server invariants

Migration 057 and the V2 API enforce the following rules independently of the browser:

1. No more than five timer rows may be in `RUNNING` state for one user.
2. The same assignment or non-project category cannot have two running timers for the same user.
3. A batch start is serialized by a per-user PostgreSQL advisory transaction lock.
4. Every timer in one batch receives the same authoritative UTC start timestamp.
5. Each timer has a 24-hour or 86,400-second safety limit and rounds upward once to a 15-minute boundary, up to 1,440 minutes.
6. Individual stop and discard operations use row-version checks.
7. Stop All requires the submitted timer set to equal the complete server-side running set. Every conversion occurs in one transaction. If one conversion is blocked, the transaction rolls back and no timer is partially stopped.
8. Existing immutable timer-audit evidence remains authoritative.
9. The existing 24-hour total per Timesheet day remains in force. Simultaneous timers do not bypass it.

The historic single-timer routes remain available for compatibility, while the Module 001 browser uses the V2 active-set, history, batch-start, individual-stop, atomic-stop-all, and individual-discard routes.

## Document-grounded Generate AI Suggestion

The engineer's rough note remains the primary evidence of work actually performed. The AI action may improve terminology and scope alignment, but it cannot infer that unreported work occurred.

For an authorized project task or service request, ProjectPulse first resolves the project from the selected row and checks the private, permission-scoped document index. The current grounding order gives the strongest weight to:

1. Statement of Work (SOW)
2. Global Solution Design (GSD)
3. Architecture and design documents
4. Orders and order forms
5. Quotes and proposals
6. Other active engineering-visible supporting project documents

Migration 057 normalizes every active, engineering-visible project document as eligible for the shared project AI context and queues documents that are not yet ready through the existing private processing pipeline. The same private document index supports the current Timesheet, Help/Search, and FlowHive project-generation capabilities; no new public raw-document route is introduced.

Service requests receive the same behavior when their row contains a request number or resolves to the associated project. Request identity, project code, task details, and the engineer note are included in the retrieval question so the private service can select relevant SOW, GSD, and supporting evidence.

Retrieved source text remains inside the private ProjectPulse RAG boundary. The public-provider fallback does not receive the Engineer note or selected row identity. The backend detects a bounded set of activity and technical-domain signals and emits only fixed, server-authored category labels plus a generic work classification. No captured token or substring is copied, so lowercase or unlabeled names remain private. If no safe factual category can be derived, the request fails closed to the governed local template. Source access is reauthorized for the effective user before prompt assembly. The returned description is a suggestion only and must be reviewed and explicitly applied by the Engineer.

AI cannot change hours, dates, classification, project, task, request, allocation, timer state, save state, submission, approval, or customer commitment.

## Mobile mode and time-entry modal

The Mobile mode checkbox is restored in a statically generated React-owned toolbar slot. Its setting persists in local storage and applies a stacked Timesheet presentation with larger touch targets.

The time-entry details window keeps its existing fields and AI assistant, but its header uses a contained grid so **Submit this day** and **Close** remain inside the dialog. On small screens, or whenever Mobile mode is selected, it becomes a full-height sheet with contained full-width actions.

## Migration and rollback

Forward migration:

`057_module_001_multi_timer_document_grounded_ai`

Prerequisites:

- `041_module_001_timesheet_timer_and_task_association`
- `052_pulse_ai_private_document_runtime`
- `053_pulse_ai_private_rag_orchestration`

The rollback is fail-closed after multi-timer use, timer data above the historic 12-hour limit, project-AI queue evidence, immutable queue events, or eligible project-document policy data exists. A reviewed data-conversion plan is required instead of silently deleting or collapsing operational evidence.

## Validation evidence

The package includes:

- focused frontend and backend source-contract validation;
- 24-hour rounding tests;
- PostgreSQL migration idempotency and invariant tests;
- five-running-timer and sixth-timer rejection tests;
- duplicate-target rejection tests;
- project-document policy propagation and queue tests;
- guarded rollback verification;
- full API and frontend production builds in the package CI workflow.
