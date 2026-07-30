# Pulse AI Private Runtime Activation — Phase 011C

## Purpose

Phase 011C converts the read-only private document-processing design into a
durable, controlled runtime owned by Pulse. It adds a processing queue,
malware scanning, private extraction and OCR coordination, citation-preserving
section and chunk persistence, private embeddings, a permission-scoped hybrid
index, retries, cancellation, version evidence, and immutable processing audit.

The phase is private-first. Raw SOW, GSD, architecture, design, order, quote,
proposal, contract, customer, financial, and employee content does not enter the
Claude or OpenAI path.

## Source checkpoint

| Field | Value |
|---|---|
| Module | `011 — Pulse AI` |
| Phase | `011C — Private Runtime Activation` |
| Branch | `feature/module-011-private-runtime-activation-20260729` |
| Exact base | `main@56dd3df02a26aa0c07c0a92dd2ac9dd9f3a3d747` |
| Migration | `052_pulse_ai_private_document_runtime` |
| Provider-route change | None |
| Azure or Entra mutation | None |
| Deployment | None |

PR #221 and PR #240 are already merged on the base used for this phase. Open
Pulse AI Help usability PR #277 is independently owned; this phase does not
modify its Help-chat source files or product-knowledge catalog.

## Durable data model

Migration 052 creates the following ProjectPulse PostgreSQL structures:

- `pulse_ai_document_processing_jobs`
- `pulse_ai_document_versions`
- `pulse_ai_document_sections`
- `pulse_ai_document_chunks`
- `pulse_ai_document_processing_events`

The migration also extends `project_intake_documents` with private processing,
classification, version-authority, active-version, error, and freshness fields.

### Processing jobs

A job records:

- source document and project;
- actual and effective user;
- requested purpose and priority;
- lifecycle state and attempt count;
- queue availability and worker lease;
- cancellation state;
- malware scanner, OCR, embedding, and index evidence;
- correlation ID, diagnostics, timings, and sanitized metrics.

Only one active job can exist for a document at a time.

### Document versions

A version is keyed by document ID and source SHA-256. It records extraction,
classification, counts, OCR use, malware evidence, embedding model and
dimension, index state, effective date, authority state, and the processing job
that created it.

The version states are:

- candidate;
- approved;
- canonical;
- superseded;
- rejected;
- revoked.

Upload time alone is not contractual authority. SOW and GSD source precedence
still requires approval, revision, effective-date, supersession, or explicit
canonical-version evidence.

### Sections and chunks

Private section text and private chunk text remain inside PostgreSQL and the
approved private runtime. Every stored section and chunk carries:

- document and version identifiers;
- project/customer context;
- classification and purpose flags;
- citation anchor and page/sheet evidence;
- source and text checksums;
- token and character counts;
- embedding and index state;
- a non-authoritative authorization snapshot.

The authorization snapshot is evidence only. Current authorization is always
recomputed before retrieval and again before prompt assembly.

### Hybrid retrieval foundation

The migration creates a PostgreSQL generated `tsvector` and GIN index for
lexical retrieval. Optional private vectors are stored as bounded
`DOUBLE PRECISION[]` values with an explicit dimension and model identifier.
The next phase performs server-side cosine ranking after current authorization
filters are applied.

No unrestricted SQL or browser-side vector execution is introduced.

## Private processing lifecycle

```text
Authorized queue request
        |
        v
Current identity, permission, and project-scope revalidation
        |
        v
Private malware scan
        |
        +---- infected ------> Quarantine; no parse/embed/index
        |
        v
Native private extraction
        |
        +---- image-only ----> Approved private OCR endpoint
        |
        v
Citation-preserving sections and deterministic chunks
        |
        v
Approved private embedding endpoint
        |
        +---- unavailable ---> Retry/fail, or explicitly allowed lexical-only mode
        |
        v
Transactional version/section/chunk/index persistence
        |
        v
Document ready + immutable processing evidence
```

## Runtime adapters

### Malware scanning

The runtime supports:

1. private ClamAV-compatible TCP `INSTREAM`; or
2. an explicitly configured pre-scan attestation for environments where the
   upload gateway already performs the authoritative scan.

The scanner must return a clean result before extraction. Infected documents
are quarantined and are not parsed, OCR-processed, embedded, or indexed.
Scanner responses are converted to sanitized evidence and are not returned raw.

### OCR

OCR is called only when native PDF extraction identifies an image-only or
text-sparse document. The endpoint must pass the private endpoint policy.
Supported private endpoints are:

- loopback;
- RFC1918/private IP addresses;
- IPv6 local/private addresses; or
- an explicitly approved private DNS suffix/host.

The OCR request is sent directly from the private Pulse runtime. Module 064,
Claude, and OpenAI are not involved. The response must return page-level text
or a bounded document-level text result. The runtime converts it into standard
citation-preserving sections.

### Private embeddings

The embedding adapter uses an OpenAI-compatible JSON contract but accepts only
an endpoint that passes the private endpoint policy. It batches bounded chunk
text, validates vector counts and dimensions, and never logs embedding input.

Embedding tokens are read from runtime secret configuration and are never
returned through the API or browser.

### Lexical-only degraded mode

Lexical-only completion is disabled by default. It may be explicitly enabled
for a controlled environment when private embeddings are temporarily
unavailable. Such versions remain marked lexical-only so retrieval and answer
quality can treat them differently.

## Queue and worker behavior

The hosted worker is registered with the Pulse API process but remains disabled
unless:

```text
PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_WORKER_ENABLED=true
```

The worker:

- uses `FOR UPDATE SKIP LOCKED` to claim one job;
- holds a bounded lease;
- increments attempts;
- revalidates access before reading the file;
- checks cancellation before major stages;
- schedules bounded retries;
- preserves terminal failure/quarantine evidence;
- never logs source or chunk text.

The worker does not activate itself, create infrastructure, or change an
endpoint. Runtime configuration and migration application remain separate
controlled operations.

## API surface

### Read-only

```text
GET /api/pulse-ai/v1/documents/runtime/readiness
GET /api/pulse-ai/v1/documents/runtime/jobs
GET /api/pulse-ai/v1/documents/{documentId}/runtime-state
```

### Explicitly confirmed mutations

```text
POST /api/pulse-ai/v1/documents/{documentId}/processing-jobs
POST /api/pulse-ai/v1/documents/runtime/jobs/{jobId}/cancel
POST /api/pulse-ai/v1/documents/runtime/jobs/{jobId}/retry
```

Required confirmations:

```text
QUEUE-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING
CANCEL-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING
RETRY-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING
```

View-As is read-only. The API rejects all mutation requests when the actual and
effective users differ.

## Permission model

Migration 052 adds:

- `VIEW_PULSE_AI_DOCUMENT_RUNTIME`
- `QUEUE_PULSE_AI_DOCUMENT_PROCESSING`
- `CANCEL_PULSE_AI_DOCUMENT_PROCESSING`
- `RETRY_PULSE_AI_DOCUMENT_PROCESSING`
- `APPROVE_PULSE_AI_DOCUMENT_VERSION`

Super Administrator and Administrator receive all five capabilities. Project
Team Coordinators receive the runtime capabilities but remain limited by the
existing document and project-scope contract. Project Management and Engineering
leads receive scoped read visibility.

A permission never expands project or document scope. Both capability and
record authorization must pass.

## Configuration reference

### Worker

```text
PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_WORKER_ENABLED
PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_POLL_SECONDS
PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_LEASE_SECONDS
PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_MAX_ATTEMPTS
PROJECTPULSE_PULSE_AI_ALLOW_LEXICAL_ONLY_COMPLETION
```

### Malware scanner

```text
PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCANNER_MODE
PROJECTPULSE_PULSE_AI_CLAMAV_HOST
PROJECTPULSE_PULSE_AI_CLAMAV_PORT
PROJECTPULSE_PULSE_AI_CLAMAV_TIMEOUT_SECONDS
PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED
PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SIGNATURE_VERSION
```

### Private OCR

```text
PROJECTPULSE_PRIVATE_OCR_ENDPOINT
PROJECTPULSE_PRIVATE_OCR_MODEL
PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN
```

### Private embeddings

```text
PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT
PROJECTPULSE_PRIVATE_EMBEDDING_MODEL
PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN
PROJECTPULSE_PULSE_AI_PRIVATE_EMBEDDING_BATCH_SIZE
```

### Private endpoint policy

```text
PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST
```

Secret values must use the approved runtime secret mechanism. They must not be
committed to Git, displayed by Module 011, written to audit JSON, or placed in
logs.

## Failure behavior

| Condition | Behavior |
|---|---|
| Authorization revoked | Fail before reading the document |
| Scanner unavailable | Retry, then fail after maximum attempts |
| Malware detected | Quarantine immediately |
| Native extraction blocked | Retry or fail without persistence |
| OCR required but missing | Set `awaiting_ocr` |
| Private OCR fails | Retry, then fail |
| Embedding fails | Retry/fail unless lexical-only mode is explicitly enabled |
| Cancellation requested | Stop at the next controlled boundary |
| Database transaction fails | Roll back version, section, chunk, and job completion writes |

## Privacy and security controls

- Raw document content is never sent to Claude or OpenAI.
- OCR and embedding endpoints must be private or explicitly allowlisted.
- Documents are reauthorized before processing.
- Retrieval authorization will be recomputed in Phase 011D.
- Scanner, extraction, OCR, embedding, and persistence evidence avoids raw text.
- Processing events are immutable.
- Old chunks are deactivated before a replacement version becomes active.
- Revoked documents can be removed from retrieval without model retraining.
- The browser receives metadata, counts, hashes, status, and diagnostics only.

## Activation boundaries

This source package does not:

- apply migration 052 to Test or Production;
- configure or create ClamAV;
- create a private OCR service;
- create or expose an embedding service;
- enable the worker;
- change Module 064;
- contact Claude or OpenAI;
- train or fine-tune a model;
- deploy an API or web revision;
- mutate Azure, Entra, Container Apps, networking, DNS, Key Vault, or storage.

Those are separate environment-specific approvals.

## Acceptance criteria

Before runtime activation:

1. Migration 052 applies, verifies, rolls back, and reapplies cleanly.
2. View-As mutation tests return HTTP 403.
3. Unauthorized documents cannot be queued, listed, cancelled, retried, or read.
4. A scanner failure never permits extraction.
5. An infected document creates quarantine evidence and zero chunks.
6. OCR traffic reaches only the approved private endpoint.
7. Embedding traffic reaches only the approved private endpoint.
8. Raw document and chunk text is absent from API responses and logs.
9. Processing transactions do not leave partial active versions.
10. Retry and cancellation preserve immutable event history.
11. Replacement versions deactivate earlier retrieval chunks.
12. Every active chunk retains citations, checksums, classification, purpose, and
    current-source references.

Phase 011D consumes this runtime to provide live private RAG for Timesheet,
Help/Search, and FlowHive.
