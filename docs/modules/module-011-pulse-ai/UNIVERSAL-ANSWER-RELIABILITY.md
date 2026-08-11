# Ask Celar AI — Universal Answer Reliability

## Status

- Package: source-only reliability foundation
- Module: 011 — Celar AI
- Shared provider authority: Module 064
- Contract: `celar-ai-universal-answer-reliability-v1-20260810`
- Evaluation corpus: 120 frozen questions across 10 categories
- Runtime mutation in this package: none
- Database migration in this package: none
- Oracle VM change in this package: none
- Production activation in this package: none

## Purpose

Ask Celar AI must be able to receive a broad natural-language question without pretending that one model, one search index, or one database query can answer every question correctly. This package adds a universal reliability layer around the existing Celar AI production chat route. It classifies the question, defines the authoritative evidence contract, records the governed tool families that may satisfy the request, and evaluates the completed answer before it is promoted as verified.

The goal is not to promise that every possible question has an answer. The goal is that every question receives one of these trustworthy outcomes:

1. a current, permission-scoped, evidence-supported internal fact;
2. a cited private-document answer;
3. a deterministic calculation with its source and scope;
4. a source-controlled procedure;
5. a current diagnostic conclusion supported by runtime evidence;
6. a public answer supported by an approved public-information route;
7. a reviewable draft that is explicitly labeled as a draft; or
8. an evidence-limited response that explains what is unavailable, stale, conflicting, unauthorized, or ambiguous.

A fluent answer is not considered correct merely because it sounds plausible.

## Existing foundation preserved

This package extends the current architecture rather than replacing it. The following existing capabilities remain authoritative:

- `/api/celar-ai/v2/chat` remains the shared Ask Celar AI route.
- Module 064 remains the only provider-routing and protected-secret authority.
- `PulseAiSystemIntelligenceService` remains the governed system-tool and private-RAG orchestrator.
- `CelarAiInternalDataService` continues to own deterministic named-person project and task resolution.
- `CelarAiPeopleAndGuidanceService` continues to own permission-aware people and operating guidance.
- The private document pipeline continues to own file admission, malware scanning, extraction, OCR, chunking, embeddings, indexing, citations, and revocation.
- FlowHive and Project Forge retain their own planning, citation, schedule, review, and persistence boundaries.
- Each business module remains the authority for its own permissions, records, calculations, and mutations.

The new reliability service does not execute arbitrary SQL, call a provider, read secrets, bypass a module API, or widen a user’s authorization. It evaluates the evidence returned by the established governed paths.

## Universal question classes

### Structured operational

Examples:

- How many active projects does Kevin Damisch have?
- Which engineers have not submitted time this week?
- Which projects are over budget?
- What is my current utilization?
- Who changed the role policy?

Required behavior:

- resolve actual and effective identity;
- enforce the owning module’s record scope;
- use current structured data or a governed read-only tool;
- apply deterministic calculations for counts, sums, schedules, percentages, forecasts, and variances;
- preserve missing values as unknown rather than converting them to zero;
- provide source, scope, freshness, calculation basis, and limitations.

A language model may summarize the result, but it cannot manufacture the underlying number.

### Document evidence

Examples:

- What deliverables are listed in the SOW?
- What does the GSD say about customer prerequisites?
- Summarize the acceptance criteria in the attached PDF.
- Which document version is authoritative?

Required behavior:

- confirm the user is authorized for the document now;
- bind all processing to the immutable source checksum;
- require malware-scan evidence before extraction;
- use native extraction when possible and OCR only where needed;
- choose the approved or explicitly authoritative version rather than the most recent upload by default;
- return page, slide, worksheet, section, or chunk citations;
- never send raw private document content to a public provider.

If a citation-ready authorized source is unavailable, the answer must say so.

### Cross-domain

Examples:

- Are all SOW deliverables represented in the current project tasks?
- Which documented milestones are missing from the active plan?
- Does the current forecast include all work required by the SOW?
- Which current risks threaten a contracted deliverable?

Required behavior:

- retrieve at least two independent evidence families;
- combine current structured Pulse records with authoritative private document evidence;
- identify the join key, such as project, task, deliverable, WBS, customer responsibility, or milestone;
- disclose unmatched, ambiguous, duplicated, or conflicting records;
- use deterministic comparisons and calculations where applicable;
- never allow one evidence family to silently substitute for the other.

### Product procedure

Examples:

- How do I submit a timesheet?
- Where do I upload a SOW?
- How do I use View-As?

Required behavior:

- use source-controlled product documentation and current route/navigation evidence;
- apply the effective user’s module visibility;
- identify prerequisites, safeguards, and role-specific differences;
- avoid inventing controls that are not present in the current version.

### Runtime diagnostic

Examples:

- Why is Module 082 returning an error?
- Which APIs are running for Module 011?
- Why did the Test deployment roll back?
- Is the private Celar AI runtime healthy?

Required behavior:

- use current runtime, release, endpoint, correlation, dependency, health, defect, or audit evidence;
- distinguish an observed fact from a root-cause hypothesis;
- list diagnostics already completed and diagnostics still required;
- apply strict freshness limits;
- never claim a live environment state from source code alone.

### Architecture enhancement

Examples:

- Design a new knowledge-retrieval capability.
- How should Celar AI add reranking?
- What architecture would support universal question answering?

Required behavior:

- identify current capabilities and verified gaps;
- use the API catalog, module ownership, security contract, deployment model, and existing architecture;
- label proposals, estimates, assumptions, and future-state components as drafts;
- preserve human approval for migrations, infrastructure, secrets, security changes, and Production.

### Public current

Examples:

- Who is the current President of the United States?
- What is the latest stable PostgreSQL version?
- What is today’s weather?
- What current regulation applies?

Required behavior:

- use retrieval-time public evidence;
- provide source and retrieval timestamp;
- do not answer from local model memory;
- do not attach private Pulse context to the public request;
- use only the Module 064 route approved for public general knowledge.

### Public stable

Examples:

- What is the capital of France?
- Explain zero trust.
- What is DNS?

Required behavior:

- use a governed public source or approved provider answer;
- preserve citations when requested;
- never mix private Pulse evidence into the public payload unless the request remains completely private.

### Unknown or ambiguous

Examples:

- Help.
- What about that?
- Is it ready?
- How many are there?

Required behavior:

- use current conversation context only when it is authorized and unambiguous;
- ask for the missing project, module, person, period, document, or business scope;
- never fill the missing scope with a guessed entity;
- never broaden the query to all customers, all employees, all projects, or all records.

## Authoritative source order

The following precedence applies unless the owning module defines a stricter rule:

1. deterministic runtime facts generated by the current application process;
2. current permission-scoped records from the owning module;
3. deterministic calculations produced from those records;
4. approved authoritative document versions with citations;
5. source-controlled product and operating documentation;
6. current runtime diagnostics, release evidence, health, and audit records;
7. private model synthesis over already-authorized evidence;
8. sanitized public-provider assistance for an explicitly public question;
9. governed local fallback for non-factual explanation or evidence-limited wording.

A lower-ranked source cannot overwrite a verified higher-ranked source without an explicit conflict.

## Universal evidence plan

Before answer promotion, the planner records:

- question class;
- resolved intent;
- evidence domains;
- governed tool codes;
- evidence modes;
- required source types;
- minimum number of authoritative evidence families;
- maximum evidence age;
- citation requirement;
- deterministic-calculation requirement;
- private-model eligibility;
- sanitized external-assistance eligibility;
- required privacy controls;
- clarifications that could materially change the answer; and
- the fail-closed conclusion to use when the evidence contract is not satisfied.

The read-only preview endpoint is:

```text
POST /api/celar-ai/v1/reliability/plan
```

It does not query the database, read documents, call a model, call a public provider, read a secret, or change state.

## Post-answer reliability gate

The quality gate evaluates the completed `PulseAiSystemQuestionResult` and checks:

- successful authoritative source count;
- independent evidence-family count;
- successful governed tools;
- citation IDs that actually reference successful sources;
- private-document citations;
- source freshness;
- deterministic calculation evidence;
- live verification for changing public facts;
- required document evidence;
- unresolved conflicts;
- hidden assumptions;
- ambiguity and missing scope;
- external-model use for internal claims; and
- existing safety-block status.

### Blocker findings

A blocker prevents the result from being promoted as verified. Current blocker codes include:

- `insufficient_authoritative_evidence`
- `required_citation_missing`
- `evidence_freshness_failed`
- `deterministic_calculation_evidence_missing`
- `current_public_fact_not_live_verified`
- `private_document_evidence_missing`
- `external_model_cannot_establish_internal_fact`

When a blocker exists, the service:

- changes a non-blocked status to `partial`;
- replaces unsupported factual conclusions with the class-specific fail-closed conclusion;
- removes invalid citation IDs;
- caps confidence at 0.40;
- adds the missing or stale evidence to limitations and known unknowns; and
- gives the user a specific corrective action.

### Review findings

A review finding prevents a result from being treated as fully verified even when the available sources succeeded. Current review codes include:

- `conflicting_evidence_requires_review`
- `assumptions_hidden_by_preference`
- `clarification_recommended`

Review-required answers cap confidence at 0.74 and preserve the conflict or clarification rather than choosing a convenient answer.

### Safety refusal preservation

An existing `blocked` result remains blocked. The reliability layer cannot route around a provider refusal, application safety policy, permission boundary, or document-admission failure.

## Response evidence returned to the browser

The public response adds a sanitized `reliability` section containing:

- contract version;
- question class;
- intent;
- evidence domains;
- required governed tools;
- evidence modes;
- required source types;
- minimum source count;
- freshness limit;
- citation and calculation requirements;
- external-assistance eligibility;
- clarification prompts;
- assessment level and score;
- counts of successful sources, tools, citations, stale sources, and private citations;
- reliability findings; and
- privacy attestations.

The response does not return raw tool bodies, raw document chunks, embedding vectors, secrets, unrestricted SQL, hidden reasoning, storage paths, or unauthorized rows.

## Governed tool catalog

`CelarAiUniversalToolCatalog` is a source-controlled inventory of the evidence capabilities Ask Celar AI may request. Each tool identifies:

- stable tool code;
- display name;
- business domain;
- owning modules;
- authoritative source;
- current availability state;
- access policy;
- freshness class;
- deterministic and citation requirements;
- private-only boundary;
- mutation prohibition;
- query signals;
- required source types; and
- approved routes or logical adapters.

The catalog is not an access-control list and does not make an adapter operational. It exposes the difference between:

- an existing governed adapter;
- a protected-Test Oracle runtime capability;
- a provider route that is available only when Module 064 is ready; and
- an owning-module execution adapter that still must be implemented.

See `UNIVERSAL-ANSWER-TOOL-MATRIX.md`.

## Readiness and operator workspace

The read-only readiness endpoint is:

```text
GET /api/celar-ai/v1/reliability/readiness
```

The evaluation catalog endpoint is:

```text
GET /api/celar-ai/v1/reliability/evaluation-catalog
```

Module 011 includes an **Answer Reliability** workspace that displays:

- reliability contract status;
- governed tool and domain counts;
- current adapter coverage and explicit gaps;
- frozen evaluation count;
- question-class operating rules;
- read-only evidence-plan preview;
- the authoritative tool matrix; and
- activation gates.

The workspace performs no lifecycle write, database migration, provider mutation, model download, deployment, or environment change.

## Permission and privacy model

### Actual and effective identity

Every live retrieval continues to resolve:

- actual signed-in user;
- effective View-As user;
- active account state;
- role and permission codes;
- module visibility;
- project, team, reporting, assignment, document, and record scope; and
- View-As read-only restrictions.

The reliability catalog never overrides these decisions.

### Private information

The following information is prohibited from a public provider payload:

- raw SOW, GSD, IQS, design, order, email, attachment, or document text;
- customer, employee, project, assignment, financial, billing, risk, audit, credential, infrastructure, or security records;
- raw tool responses;
- source paths;
- secret values or secret references that expose a value;
- embedding vectors;
- internal prompts containing private facts; and
- unresolved private user questions.

### Generated SQL

Ask Celar AI may call pre-reviewed parameterized tools. It may not generate unrestricted SQL, choose arbitrary tables, remove permission predicates, execute mutation statements, or use database-superuser credentials.

### Citations

A citation is valid only when it identifies a successful source returned for the current request. Unknown or failed source IDs are removed. Private citations must preserve document, version, project, classification, checksum, and source anchor without returning raw hidden chunks.

## Deterministic calculation standard

Counts, totals, durations, dates, critical paths, utilization, capacity, costs, margins, variances, forecasts, balances, percentages, and status rollups require:

- explicit input rows or authoritative source summary;
- formula or schedule-engine identity;
- date range and time zone;
- project, team, person, or portfolio scope;
- handling of missing, stale, duplicated, and conflicting values;
- currency and units when applicable;
- calculation timestamp; and
- citation to the underlying source evidence.

The model summarizes the calculation; it does not replace it.

## Freshness policy

Default maximum ages in this package are:

| Question class | Maximum age |
|---|---:|
| Structured operational | 3,600 seconds |
| Cross-domain | 3,600 seconds |
| Runtime diagnostic | 1,800 seconds |
| Public current | 3,600 seconds |
| Document evidence | 86,400 seconds, while authority and access are rechecked at request time |
| Architecture enhancement | 604,800 seconds for source-controlled current-state evidence |
| Product procedure | 2,592,000 seconds, bounded by source version |
| Public stable | 2,592,000 seconds |

Owning modules may impose stricter limits. A source older than the applicable limit cannot establish a current fact.

## Evaluation contract

The frozen corpus contains 120 cases in 10 categories:

1. identity and permissions;
2. projects and assignments;
3. time, approval, and capacity;
4. financial and commercial;
5. documents and retrieval;
6. cross-domain delivery;
7. FlowHive and Project Forge planning;
8. operations, security, and audit;
9. public current and stable knowledge; and
10. ambiguity, privacy, and failure behavior.

Each case defines:

- question;
- planner context;
- expected question class;
- expected governed tool families;
- required evidence;
- citation rule;
- deterministic calculation rule;
- maximum evidence age;
- expected fail-closed behavior; and
- forbidden behavior.

See `UNIVERSAL-ANSWER-EVALUATION.md` and `tests/celar-ai-universal-answer-evaluation-cases.json`.

## Activation sequence

### Phase 1 — source integration

This PR provides:

- universal tool catalog;
- question and evidence planner;
- post-answer quality gate;
- Module 011 reliability endpoints;
- integration with `/api/celar-ai/v2/chat`;
- operator reliability workspace;
- 120-case regression corpus;
- executable behavioral tests;
- CI and source-isolation checks; and
- documentation.

### Phase 2 — protected Test verification

After the normal Pulse and Oracle Test deployment path is healthy:

- run all 120 cases against protected Test;
- validate representative real documents;
- validate View-As, permissions, revocation, stale data, conflict, and partial-dependency behavior;
- measure answer correctness, source correctness, citation correctness, retrieval quality, leakage, refusal correctness, latency, CPU, memory, and concurrency;
- implement missing owning-module read-only adapters in isolated packages.

### Phase 3 — retrieval enhancement decision

Only measured gaps justify:

- Apache Tika for broader extraction;
- pgvector and reviewed hybrid retrieval;
- a cross-encoder reranker;
- Redis caching; or
- a larger secondary local reasoning model.

See `UNIVERSAL-ANSWER-ROADMAP.md`.

### Phase 4 — Production consideration

Production requires a separate authorization and release. It must include:

- complete Test evidence;
- security and privacy review;
- data-governance approval;
- migration review if a database change is proposed;
- load and recovery testing;
- monitoring and operational ownership;
- exact release and rollback evidence;
- human approval; and
- no unresolved blocker-class evaluation failures.

## Non-goals of this package

This package does not:

- install Tika, Redis, pgvector, a reranker, or another local model;
- modify the Oracle runtime;
- create or rotate a token;
- expose a private port;
- edit Module 064 provider settings;
- deploy to Test or Production;
- apply a migration;
- execute a training job;
- fine-tune a model;
- autonomously learn from conversations;
- grant permissions;
- perform a business mutation; or
- claim that every question has a factual answer.

## Acceptance criteria

The source package is complete when:

- the API builds;
- the full frontend production bundle builds;
- all 120 planner cases pass;
- all behavioral fail-closed tests pass;
- the existing private-RAG, internal-data, Module 011, FlowHive, Project Forge, provider-routing, security, and release validators remain intact;
- the `/api/celar-ai/v2/chat` response includes sanitized reliability evidence;
- unsupported internal facts are not promoted as verified;
- changing public facts without live evidence are not answered from memory;
- invalid citations are removed;
- stale evidence is blocked;
- deterministic questions require deterministic evidence;
- document and cross-domain questions require the correct evidence families;
- safety refusals remain terminal;
- the operator UI identifies active adapters and honest gaps; and
- the change contains no migration, secret, deployment, infrastructure, Oracle, or Production mutation.
