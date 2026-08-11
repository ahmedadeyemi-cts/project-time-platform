# Ask Celar AI — Universal Answer Reliability Roadmap

## Guiding principle

Additional software is introduced only when measured Test evidence identifies a specific reliability or performance gap. No component is added merely because it appears in a target-state diagram or because another AI stack commonly includes it.

The current validated service layer already includes:

- Caddy HTTPS;
- authenticated FastAPI gateway;
- Ollama generation;
- Ollama embeddings;
- Tesseract 5 OCR;
- ClamAV malware scanning;
- private inference, embedding, extraction, scan, and readiness routes; and
- Pulse application controls for the protected Test connection.

This roadmap does not modify that runtime.

## Workstream 1 — owning-module structured-data adapters

### Problem addressed

Many internal questions require current structured facts, not semantic document search. Ask Celar AI must have least-privilege read adapters for the owning modules.

### Priority adapters

1. timesheet status, period, and missing-submission evidence;
2. approval queue, current stage, and decision history;
3. reporting relationships and team scope;
4. engineering resource requests;
5. expense certification and billing readiness;
6. contract, rate, and block-of-hours evidence;
7. commercial opportunity and delivery pipeline;
8. cross-module audit history;
9. observability, backup, recovery, and dependency health;
10. notification, acknowledgment, and escalation state.

### Acceptance gate

Each adapter must pass:

- authorization and View-As tests;
- exact source ownership;
- null and partial-dependency behavior;
- deterministic calculation tests;
- freshness tests;
- no raw-response or secret leakage;
- no mutation capability; and
- inclusion in the 120-question suite.

## Workstream 2 — representative document extraction

### Current state

The private gateway has proven malware scanning, OCR, and endpoint behavior. Pulse also contains private document extraction and RAG contracts. Full real-document coverage still requires representative UAT.

### Apache Tika decision gate

Add Apache Tika only when Test shows that current private extraction cannot reliably handle an approved format or required metadata.

Evidence required before approval:

- failing representative files;
- current extractor output;
- required text or metadata that is missing;
- Tika output comparison;
- ARM64 CPU and memory impact;
- startup and patching model;
- parser security review;
- timeout and archive-bomb controls;
- scan-before-parse proof;
- citation-anchor preservation; and
- rollback design.

Tika would be an extraction adapter behind the existing gateway. It would not replace ClamAV, Tesseract, Caddy, authentication, authorization, or source-checksum controls.

### Tika is not justified when

- native extraction already returns correct text and anchors;
- the request concerns structured Pulse data rather than a document;
- the gap is retrieval ranking rather than extraction;
- the gap is an unauthorized or non-authoritative document; or
- the format is intentionally prohibited.

## Workstream 3 — durable hybrid retrieval

### pgvector decision gate

Persistent vector retrieval belongs in the reviewed Pulse PostgreSQL data design, not as an unreviewed add-on to the Oracle AI VM.

Consider pgvector when:

- semantic retrieval is required across durable private chunks;
- current lexical retrieval misses relevant paraphrases;
- permission, revocation, version, classification, checksum, and citation metadata can be stored and filtered safely;
- database capacity, backup, migration, rollback, and index maintenance are approved; and
- the embedding-model version and re-index strategy are defined.

### Proposed hybrid flow

```text
permission-filtered candidate scope
→ PostgreSQL full-text retrieval
+ pgvector semantic retrieval
→ reciprocal-rank fusion
→ optional reranking
→ top citation-ready evidence
→ private-model synthesis
```

### Required metadata

- chunk ID;
- document ID;
- project ID;
- document category;
- authoritative version and supersession state;
- classification;
- purpose;
- source checksum;
- extraction version;
- embedding model and dimension;
- page, slide, worksheet, heading, or section anchor;
- current authorization and revocation evidence;
- created, updated, indexed, and revoked timestamps.

### Migration boundary

A pgvector change requires a separate migration PR and separate Test authorization. This package contains no pgvector migration.

## Workstream 4 — reranking

### Decision gate

Evaluate a cross-encoder reranker only when:

- Recall@10 meets the target;
- Precision@5 or first-result quality remains below target;
- the correct passage is retrieved but ranked too low; and
- latency and memory can be supported on the approved environment.

### Benchmark

Compare:

- lexical only;
- semantic only;
- reciprocal-rank fusion;
- fusion plus reranker.

Measure:

- Recall@10;
- Precision@5;
- first relevant rank;
- citation correctness;
- answer correctness;
- p50, p95, and timeout rate;
- CPU and memory; and
- concurrent request behavior.

### Resource boundary

A reranker must have bounded candidate count, bounded input length, queue limits, timeout, and safe fallback to the un-reranked authorized result. It cannot turn an unauthorized passage into an eligible passage.

## Workstream 5 — caching

### Redis decision gate

Redis is a performance component, not an answer-correctness component. Add it only when repeated extraction, embedding, retrieval, or readiness operations create measured latency or load.

### Cache rules

Any cache must include keys for:

- source checksum;
- extraction version;
- embedding model version;
- authorization or scope version where applicable;
- document authority state;
- classification and purpose;
- revocation state; and
- request policy version.

### Prohibitions

- no indefinite raw private text cache;
- no shared cache entry across unauthorized scopes;
- no stale answer cache that bypasses current structured data;
- no secret cache;
- no cache that prevents revocation;
- no cache represented as authoritative source evidence.

## Workstream 6 — secondary local model

### Purpose

A secondary local model may improve complex tool-plan adherence and multi-source synthesis. It does not replace source quality, permissions, calculations, citations, or the quality gate.

### Candidate evaluation

A model such as an approved 7B–8B tool-capable model may be tested against the current baseline. No model is pulled to the Oracle VM through this package.

### Required benchmark

- 120-question suite;
- structured response validity;
- tool-plan adherence;
- citation use;
- current-public restraint;
- unsupported internal claim rate;
- refusal correctness;
- latency;
- memory;
- swap;
- model load and eviction;
- concurrent queue behavior; and
- recovery after timeout.

### Promotion rule

A candidate must improve reliability without exceeding accepted operational limits. Better prose alone is not sufficient.

## Workstream 7 — current public information

### Gap

A local model cannot reliably establish changing public facts from memory.

### Required capability

Module 064 should provide a governed public-information route that:

- accepts only a clearly public question;
- contains no private Pulse context;
- retrieves current sources;
- returns source, publication date, and retrieval time;
- uses standard TLS validation;
- has timeout, circuit breaker, and usage evidence;
- rejects ambiguous internal-person or internal-organization questions; and
- falls back to an explicit not-live-verified response.

### Production boundary

Public search or provider configuration remains a Module 064 change and requires separate authorization.

## Workstream 8 — answer-quality telemetry

### Required metrics

- question-class distribution;
- selected tool families;
- adapter-gap count;
- source success and failure;
- source freshness failures;
- citation validity;
- private citation count;
- deterministic evidence presence;
- blocker and review findings;
- confidence before and after enforcement;
- partial and blocked response rate;
- fallback target decisions;
- latency and timeout;
- user feedback; and
- corrected-answer outcome.

### Privacy

Telemetry should store codes, counts, timings, checksums, source identifiers, and sanitized evidence—not raw private documents, raw tool bodies, secrets, vectors, or unnecessary personal content.

## Workstream 9 — human correction and learning

### Principle

Celar AI does not automatically train on every conversation or accept every user correction as truth.

### Controlled correction lifecycle

```text
user feedback
→ owning-source verification
→ privacy and classification review
→ labeled evaluation example
→ immutable dataset version
→ evaluation
→ human promotion decision
```

Corrections to a business record belong in the owning module. Corrections to a document belong in the authoritative document lifecycle. Corrections to procedure belong in source-controlled documentation. Model fine-tuning is optional and separate.

## Workstream 10 — Production operations

Production consideration requires:

- Test acceptance;
- security review;
- data-governance review;
- documented service owner;
- monitoring and alerting;
- backup and recovery;
- capacity plan;
- certificate-renewal plan;
- ClamAV definition monitoring;
- model and embedding health;
- queue and timeout controls;
- secret rotation;
- incident response;
- exact release manifest;
- canary and rollback;
- audit evidence; and
- protected human approval.

## Recommended sequence

1. Complete protected Test deployment and Oracle activation verification.
2. Run the 120-question source-level and live Test suite.
3. Implement Priority 1 structured-data adapters.
4. Run representative document-format UAT.
5. Improve extraction only where the test matrix fails.
6. Measure lexical/private retrieval.
7. Design pgvector only when durable semantic retrieval is justified.
8. Add reranking only when ranking—not recall, extraction, permissions, or source authority—is the demonstrated gap.
9. Add Redis only when performance measurements justify caching.
10. Benchmark a secondary local model after tools and evidence are stable.
11. Complete operational hardening.
12. Request separate Production authorization.

## Status labels for competition and architecture material

### Deployed and validated

Use only for components with runtime evidence.

### Integrated in protected Test

Use only after Pulse-to-runtime UAT has passed.

### Source ready

Use for this universal reliability package after its CI passes.

### Adapter planned

Use for cataloged owning-module readers not yet implemented.

### Evaluation candidate

Use for Tika, pgvector, reranking, Redis, and a secondary model until decision-gate evidence exists.

### Production ready

Use only after separately authorized Production operations and release evidence exist.

## Current decision

This PR builds the reliability control plane, tool catalog, operator visibility, tests, and documentation. It deliberately does not install or activate Tika, pgvector, a reranker, Redis, NGINX, Docker Compose, or another model.
