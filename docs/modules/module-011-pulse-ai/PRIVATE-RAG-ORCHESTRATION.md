# Pulse AI Live Private RAG Orchestration — Phase 011D

## Purpose

Phase 011D connects the durable private document runtime from Phase 011C to the
three initial Pulse consumers:

- Module 001 Timesheet suggestions;
- Pulse Help and Search; and
- Module 066 FlowHive project-plan drafting.

The phase performs current server-side authorization, private lexical and
semantic retrieval, prompt-assembly reauthorization, private-model reasoning,
source citation, confidence assessment, detailed answer composition, answer
audit, and controlled user feedback.

Raw SOW, GSD, architecture, design, order, quote, proposal, contract, customer,
financial, employee, section, chunk, embedding, and prompt content remains
inside the approved private Pulse boundary.

## Source checkpoint

| Field | Value |
|---|---|
| Module | `011 — Pulse AI` |
| Phase | `011D — Live Private RAG Orchestration` |
| Branch | `feature/module-011-private-rag-orchestration-20260729` |
| Exact base | Phase 011C validated head `d12c5034840d8ece00bbc176fc69e524b8d7e963` |
| Migration | `053_pulse_ai_private_rag_orchestration` |
| Module 064 change | None |
| Claude/OpenAI private-context call | None |
| Azure or Entra mutation | None |
| Deployment | None |

This phase does not modify the independently owned Pulse AI Help usability PR
#277 files. The global Help experience may adopt the private Help/Search API
after independent reconciliation and user-experience review.

## End-to-end architecture

```text
Pulse user or consuming module
        |
        v
Session, actual user, effective user, permission, project and record scope
        |
        v
Purpose-specific retrieval plan
        |
        +---- Product Help knowledge contract
        |
        +---- Private document lexical search
        |
        +---- Optional private query embedding and semantic ranking
        |
        v
Current authorization before ranking
        |
        v
Diverse candidate selection
        |
        v
Second authorization pass before prompt assembly
        |
        v
Approved private Pulse AI inference endpoint
        |
        v
Schema validation, citation validation and confidence gate
        |
        v
Detailed cited answer or reviewable Timesheet / FlowHive draft
        |
        v
Answer run, citations, retrieval evidence and controlled feedback
```

## Migration 053

Migration 053 creates:

- `pulse_ai_answer_runs`
- `pulse_ai_answer_citations`
- `pulse_ai_answer_feedback`
- `pulse_ai_retrieval_events`

### Answer runs

An answer run records:

- feature and purpose;
- actual and effective user;
- optional project;
- question hash and governed request filters;
- detail level;
- private model and prompt contract version;
- retrieval mode and counts;
- source-document and source-version counts;
- confidence, source coverage and citation coverage;
- structured answer JSON when permitted by retention policy;
- missing evidence, conflicts, warnings and source health;
- privacy evidence;
- correlation ID, diagnostics and data-as-of time.

The answer run is an audit and feedback artifact. It is not permission evidence
for future retrieval. Every new request reevaluates current authorization.

### Citations

Each citation points to the exact private chunk, source document, document
version, project, category, page or sheet, citation anchor, source checksum,
chunk checksum and source-processing time.

The browser receives citation metadata, but not private chunk text or embedding
vectors.

### Feedback

Users may accept, edit, reject or report a response. Feedback never becomes
training data automatically. `training_candidate` is always false when normal
feedback is submitted. A separate dataset-review workflow must sanitize,
version, approve and promote a training candidate.

### Retrieval events

Retrieval events capture:

- current user and project scope;
- feature;
- candidate, authorized-candidate and returned counts;
- lexical, semantic or hybrid mode;
- correlation ID;
- sanitized authorization and retrieval evidence.

Retrieval events are immutable.

## Retrieval authorization sequence

1. Resolve the current effective user and active roles/permissions.
2. Resolve the requested project only inside that user’s current project scope.
3. Select only active chunks from the active document version.
4. Require active, engineering-visible source documents in `ready` processing
   state.
5. Exclude rejected, revoked and superseded document versions.
6. Apply project, purpose and document-category filters.
7. Apply Project Manager, direct assignment, engineering-resource-request, or
   broad-scope authorization before scoring.
8. Calculate lexical and optional private semantic relevance.
9. Select a bounded, diverse set of candidates.
10. Requery current source, version and authorization state immediately before
    prompt assembly.
11. Fail closed when the second authorization pass removes every candidate.

The index and historical authorization snapshot are never the authorization
authority.

## Hybrid retrieval

### Lexical retrieval

PostgreSQL full-text search uses the generated `search_vector` from migration
052. This supports exact project terms, model names, task codes, technical
phrases, contract terms and natural-language concepts.

### Semantic retrieval

When a configured private embedding endpoint passes private-endpoint policy,
Pulse creates a private query vector using the same approved embedding model as
the document chunks. Server-side cosine similarity is calculated only after the
current authorization filter has produced eligible candidates.

### Score fusion

The configured lexical and semantic weights are normalized to one. The default
source values are:

```text
lexical weight: 0.45
semantic weight: 0.55
```

The service limits the number of chunks per document to reduce a single long
document dominating the answer. Sources below configured evidence thresholds
are excluded from model context.

## Private model boundary

The inference adapter uses an OpenAI-compatible JSON request only as a protocol.
The configured endpoint must pass the existing private-endpoint policy:

- loopback;
- RFC1918/private IP;
- IPv6 local/private address; or
- explicitly approved private host or suffix.

The following source configuration does not enable the endpoint:

```text
PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED=false
```

The private model receives:

- purpose-specific system instructions;
- the current user request;
- bounded private source chunks;
- explicit citation IDs;
- a required structured-output schema;
- prompt-injection and unsupported-claim rules.

It does not receive unrestricted database credentials, provider credentials,
raw storage paths or permission-grant authority.

### Required response behavior

Help/Search answers include, when material:

- direct conclusion;
- executive summary;
- scope and filters;
- detailed analysis;
- source evidence;
- deterministic calculation explanations;
- known, unknown, stale, unavailable and unauthorized distinctions;
- assumptions and conflicts;
- limitations;
- risks and implications;
- recommended actions;
- Pulse navigation;
- citations, data-as-of time and confidence.

No model may invent a source, project record, metric, date, permission, completed
action, financial value or system state.

## Module 001 Timesheet behavior

The existing Timesheet suggestion service now attempts the private RAG path
first.

The Engineer never enters a project ID, task ID, assignment ID, category ID, or
document ID. Module 001 carries the selected row's opaque identifiers in the
request. The backend resolves those identifiers against the effective user and
work date, replaces all display labels with database-owned values, and rejects
stale, conflicting, ambiguous, or unauthorized selections before retrieval or
generation. The resolved project ID then scopes SOW/GSD retrieval automatically.

### Evidence precedence

1. Engineer rough note — primary evidence of work actually performed.
2. Selected project, task, Regular Task or Request / Service Request.
3. Current assignment and project scope.
4. Current authorized SOW evidence.
5. Current authorized GSD evidence.
6. Other current authorized engineering-visible documents.

The private model may improve terminology and scope alignment. It may not use a
SOW or GSD to claim that unreported work occurred.

### Human controls

Pulse AI cannot:

- change hours;
- change work date;
- change normal/after-hours type;
- change project, task, request, category or allocation;
- save the row;
- submit the Timesheet; or
- approve the Timesheet.

The Engineer reviews and explicitly applies the proposed description.

### Fallback behavior

- If private evidence and the private model are ready, use the private result.
- If private evidence exists but the private model is unavailable, preserve a
  sanitized readiness/evidence warning and continue through the configured
  provider route using only fixed, server-authored activity/domain categories
  derived inside the backend and a generic work classification. Never send the
  Engineer free text, captured tokens/substrings, row identity, document text,
  summaries, citations, filenames, or embeddings to Claude or OpenAI. If no safe
  category can be derived, fail closed to the governed local template.

### Provider order and runtime activation

Module 064's persisted capability route is authoritative. For a SOW-grounded
Timesheet response, `celar_ai` must be the first target because raw private
documents are eligible only for the approved private Celar endpoint. Placing
Claude or OpenAI first intentionally produces a sanitized, non-document request;
neither provider is allowed to receive raw SOW/GSD content.

The following runtime controls are independent of provider credentials and
route order:

- `PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION=true` authorizes eligible,
  sanitized Timesheet fallbacks to Claude or OpenAI.
- `PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED=true` authorizes the
  separate Celar enterprise external-reasoning fallback path.
- `PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY` must remain a stable base64-encoded
  32-byte key so stored provider and private-target secrets survive revisions.
- `PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED=true` enables private Timesheet RAG.
- The private Celar profile must be enabled and contain an approved private
  OpenAI-compatible endpoint, the exact model/deployment name, the required
  private-host allowlist, and an approved authentication secret or identity.

Provider probes prove credentials and endpoint reachability. They do not prove
that runtime policy has authorized normal generation. Module 064 therefore
reports both provider health and the external-generation policy state, while
Timesheet responses include sanitized per-target decision codes.

### Production readiness

Before enabling SOW grounding in an environment:

1. Apply migrations 052, 053, and 061.
2. Mount a shared, private, writable volume and point
   `PROJECTPULSE_UPLOAD_ROOT` to that mount. Do not use revision-local `/tmp`
   storage.
3. Reconcile or re-upload files referenced by historical absolute storage paths.
4. Enable the private processing worker and a real document-specific malware
   scanning integration.
5. Configure OCR where required and either private embeddings or the explicitly
   approved lexical-only completion path.
6. Mark the current SOW engineering-visible and Timesheet-context eligible,
   then process it to a ready indexed version.
7. Save and test the private Celar profile. The test must return
   `private_model_available` before relying on SOW-grounded generation.
- If no private document evidence exists, the non-document provider route may
  use only a purpose-built category capsule produced from a fixed backend
  vocabulary. It receives no Engineer free text, customer/project/task/person
  identity, date, location, SOW, GSD, architecture, contract, pricing,
  financial, extracted-document, private-chunk, or embedding content.

Every customer-facing suggestion requires a factual Engineer rough note. The
service requests two to four complete professional sentences (approximately 75
to 150 words when supported), removes bullets/Markdown, preserves sentence
boundaries, and refuses to invent testing, validation, completion, delivery, or
customer impact merely to make the response longer.

## Help and Search behavior

Pulse AI combines:

- governed direct product knowledge where available; and
- current authorized private project-document evidence.

A direct product answer may explain modules, workflows, permissions, buttons,
fields and procedures without a model call. It may not invent live record
status.

Document-grounded answers require private evidence and, by default, an approved
private model. If the evidence gate fails, Pulse returns an explicit
`insufficient_evidence` result rather than a surface-level guess.

The API is source-complete for private Help/Search. Adoption by the separately
owned global Help UI is a follow-up integration decision.

## FlowHive behavior

Pulse AI produces a structured draft containing:

- objective;
- WBS tasks and descriptions;
- estimated duration assumptions;
- required roles;
- predecessors;
- milestones and acceptance evidence;
- dependency notes;
- assumptions;
- risks;
- out-of-scope items;
- open questions;
- source conflicts; and
- citations.

The language model does not establish authoritative calendar dates. The
structured task and dependency model is intended for the deterministic FlowHive
schedule engine, which applies working days, holidays, dependency types, lead
or lag, critical path, float and capacity evidence.

The result is always a draft. Pulse AI does not:

- baseline the plan;
- assign a person;
- reserve capacity;
- publish to a customer;
- modify a contract; or
- commit a customer date.

The Project Manager and Engineering must modify and validate the plan before any
separately authorized baseline approval.

## API surface

```text
GET  /api/pulse-ai/v1/rag/readiness
POST /api/pulse-ai/v1/rag/help-search
POST /api/pulse-ai/v1/rag/timesheet-suggestion
POST /api/pulse-ai/v1/rag/flowhive-plan
GET  /api/pulse-ai/v1/rag/answers/{answerRunId}
POST /api/pulse-ai/v1/rag/answers/{answerRunId}/feedback
```

The answer-audit endpoint requires the audit permission and current record
scope. Feedback is blocked during View-As.

## Permission model

Migration 053 adds:

- `ASK_PULSE_AI_HELP_SEARCH`
- `USE_PULSE_AI_TIMESHEET_GROUNDING`
- `USE_PULSE_AI_FLOWHIVE_PLANNING`
- `VIEW_PULSE_AI_ANSWER_AUDIT`
- `SUBMIT_PULSE_AI_FEEDBACK`

Permissions do not expand module, project, document, record or field scope.

## Configuration

```text
PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED
PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT
PROJECTPULSE_PRIVATE_INFERENCE_MODEL
PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN
PROJECTPULSE_PULSE_AI_RAG_MAX_CHUNKS
PROJECTPULSE_PULSE_AI_RAG_MAX_CANDIDATES
PROJECTPULSE_PULSE_AI_RAG_MAX_CONTEXT_CHARACTERS
PROJECTPULSE_PULSE_AI_RAG_MAX_QUESTION_CHARACTERS
PROJECTPULSE_PULSE_AI_RAG_MAX_ANSWER_CHARACTERS
PROJECTPULSE_PULSE_AI_RAG_MAX_OUTPUT_TOKENS
PROJECTPULSE_PULSE_AI_RAG_MIN_EVIDENCE_SCORE
PROJECTPULSE_PULSE_AI_RAG_MIN_CONFIDENCE
PROJECTPULSE_PULSE_AI_RAG_LEXICAL_WEIGHT
PROJECTPULSE_PULSE_AI_RAG_SEMANTIC_WEIGHT
PROJECTPULSE_PULSE_AI_RAG_REQUIRE_PRIVATE_MODEL
PROJECTPULSE_PULSE_AI_RAG_PERSIST_ANSWER_TEXT
```

The private model token must use the approved runtime secret mechanism. It is
never returned through Module 011, logs or answer audit.

## Security controls

- Authorization is applied before scoring and again before prompt assembly.
- Prompt-injection text remains untrusted evidence.
- Model output citation IDs are validated against the retrieved source set.
- Raw source chunks and vectors are not returned by public APIs.
- Raw documents are not sent to Claude or OpenAI.
- Module 064 is not used for the private source context.
- Feedback never becomes training data automatically.
- Retrieval evidence is immutable.
- Answer audit remains permission and project scoped.
- View-As cannot submit feedback.
- No unrestricted model-generated SQL is executed.

## Source activation boundary

This package does not:

- apply migration 053 to Test or Production;
- apply or enable migration 052 if it is absent;
- configure or create the private inference endpoint;
- configure or create the private embedding endpoint;
- enable the Phase 011C worker;
- change Module 064;
- call Claude or OpenAI;
- create Azure, Entra, Container App, networking, DNS, Key Vault or storage
  resources;
- train or fine-tune a model;
- deploy an API or web revision;
- baseline a FlowHive plan; or
- save or submit a Timesheet.

Migration, private infrastructure, secret configuration, worker activation,
source merge and deployment are separate approvals.

## Acceptance criteria

Before activation:

1. Migration 052 and 053 apply, verify, roll back and reapply cleanly.
2. Unauthorized chunks never enter lexical, semantic, reranking or prompt
   context.
3. Prompt-assembly reauthorization removes revoked candidates.
4. Private endpoint policy rejects public inference and embedding destinations.
5. Raw document, chunk, vector, prompt and secret values are absent from API
   responses and logs.
6. Model citation IDs outside the retrieved set are removed.
7. Timesheet output cannot change or submit time.
8. FlowHive output cannot baseline, assign, reserve or publish.
9. Help/Search identifies missing evidence instead of fabricating a conclusion.
10. Retrieval events are immutable.
11. Feedback is not a training candidate by default.
12. Answer audit remains permission and record scoped.
13. Private-model outage never sends raw document context to Claude or OpenAI.
14. Frozen authorization, citation, prompt-injection, hallucination, latency and
    regression suites pass before production routing.
