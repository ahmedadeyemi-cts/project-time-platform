# Celar AI free self-hosted private RAG activation

This runbook maps the current Celar AI runtime contracts to software that has no license fee. It does not make the pull request a deployment: no service, secret, database migration, or environment setting changes until an approved infrastructure change is released separately.

“Free” refers to the software licenses. Private compute, storage, networking, TLS certificates, backups, monitoring, model capacity, patching, and operational ownership still have a cost.

## Recommended stack

| Capability | Free option | Why it fits the current code |
|---|---|---|
| Malware protection | ClamAV using the official `clamav/clamav` image | The backend already implements clamd TCP `INSTREAM` and fail-closed scan results. |
| OCR | Tesseract 5 behind a small private HTTPS adapter | The backend already posts a multipart `file`, `model`, `documentId`, and `documentCategory` request and accepts bounded `pages[]` or `text` JSON. |
| Private embeddings and inference | Ollama behind a private HTTPS reverse proxy that enforces bearer authentication | Ollama exposes OpenAI-compatible chat-completion and embedding APIs, matching the existing private clients. |
| Permission-scoped retrieval | Existing PostgreSQL `tsvector`/GIN index plus stored private vectors | Migrations 052 and 053 already provide lexical search, bounded vector storage, authorization-before-ranking, and server-side cosine scoring. A separate vector database is not required. |

Official references:

- ClamAV: <https://docs.clamav.net/manual/Installing.html>
- Tesseract: <https://tesseract-ocr.github.io/tessdoc/Installation.html>
- Ollama OpenAI compatibility: <https://docs.ollama.com/api/openai-compatibility>
- PostgreSQL full-text search: <https://www.postgresql.org/docs/current/textsearch.html>

## 1. Malware protection

Run the official ClamAV image on a private network. Allow TCP 3310 only from the Celar AI API runtime, persist or reliably refresh signatures with `freshclam`, and monitor signature freshness and scan failures. Do not expose clamd publicly.

After the scanner has passed a live clean-file and EICAR test in the target environment, configure:

```text
PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCANNER_MODE=clamav_tcp
PROJECTPULSE_PULSE_AI_CLAMAV_HOST=<private DNS name>
PROJECTPULSE_PULSE_AI_CLAMAV_PORT=3310
PROJECTPULSE_PULSE_AI_CLAMAV_TIMEOUT_SECONDS=45
PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SIGNATURE_VERSION=<observed signature version>
PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED=true
```

The last value records reviewed environment evidence; it must not be set merely to turn the tile green. Direct Celar AI conversation uploads still require a live ClamAV result for every file.

## 2. OCR

Install Tesseract 5 and the approved language packs. Put it behind an internal adapter with this contract:

```text
POST multipart/form-data
  file=<document>
  model=<configured model name>
  documentId=<UUID>
  documentCategory=<category>

200 application/json
  { "pages": [{ "pageNumber": 1, "text": "..." }] }
```

The adapter must enforce bounded request sizes, timeouts, page limits, sandboxed conversion of PDFs to images, no shell interpolation, temporary-file deletion, and no document text in logs. Publish it through private HTTPS with bearer authentication and a DNS name that resolves only to private addresses.

```text
PROJECTPULSE_PRIVATE_OCR_ENDPOINT=https://ocr.internal.example/v1/extract
PROJECTPULSE_PRIVATE_OCR_MODEL=tesseract-5-eng
PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN=<runtime secret>
PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN_SECRET_REFERENCE=<pinned secret reference>
PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST=ocr.internal.example,ai.internal.example
```

OCR is invoked only for image-only or text-sparse documents. Native PDF, Office, HTML, XML, CSV, Markdown, JSON, and text extraction remains local in the API.

## 3. Private retrieval and inference

Run Ollama on approved private compute. Use separate models for generation and embeddings when appropriate, then front Ollama with internal TLS and a reverse proxy that validates the bearer token. Ollama may ignore an API key itself, but Celar AI production readiness requires authenticated private endpoints.

Configure full endpoint URLs, not only the service root:

```text
PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT=https://ai.internal.example/v1/chat/completions
PROJECTPULSE_PRIVATE_INFERENCE_MODEL=<approved generation model>
PROJECTPULSE_PRIVATE_INFERENCE_AUTH_MODE=bearer
PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN=<runtime secret>
PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN_SECRET_REFERENCE=<pinned secret reference>

PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT=https://ai.internal.example/v1/embeddings
PROJECTPULSE_PRIVATE_EMBEDDING_MODEL=embeddinggemma
PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN=<runtime secret>
PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN_SECRET_REFERENCE=<pinned secret reference>
PROJECTPULSE_PRIVATE_VECTOR_INDEX=projectpulse_postgresql_hybrid
```

The current PostgreSQL implementation stores vectors as bounded `DOUBLE PRECISION[]` values and calculates cosine similarity only after current user, project, document, and attachment authorization filters pass. `PROJECTPULSE_PRIVATE_VECTOR_INDEX` is therefore an explicit activation marker for the existing permission-scoped hybrid index, not a connection string or credential.

An explicitly approved lexical-only route can be used temporarily, but it is a degraded mode rather than the recommended 100% hybrid-retrieval state:

```text
PROJECTPULSE_PULSE_AI_ALLOW_LEXICAL_ONLY_COMPLETION=true
PROJECTPULSE_PULSE_AI_LEXICAL_ONLY_APPROVAL_REFERENCE=<change or risk approval>
```

## 4. Gates for `Private RAG Ready`

All of these must pass:

1. Apply and verify migrations 052, 053, 061, 071, 072, 079, and 081 through the normal migration workflow. Migration 081 creates only the least-privilege document-admission identity and repairs a Work Register filename extension only when the durable stored path proves the supported type.
2. Set `PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED=true`.
3. Configure a private HTTPS inference endpoint and exact model.
4. Store a write-only bearer secret and its pinned secret reference.
5. Keep `PROJECTPULSE_PULSE_AI_RAG_REQUIRE_PRIVATE_MODEL=true` for document-grounded answers.
6. Configure private embeddings plus `PROJECTPULSE_PRIVATE_VECTOR_INDEX=projectpulse_postgresql_hybrid`, or record an explicit temporary lexical-only approval.
7. Configure shared durable upload storage with `PROJECTPULSE_UPLOAD_ROOT` and, only after verification across replicas, `PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT=true`.
8. Enable the private worker and automatic admission only after the dedicated service principal is active and has `QUEUE_PULSE_AI_DOCUMENT_PROCESSING`.
9. Upload an active, engineering-visible, AI-context-enabled SOW or GSD; process it; approve the version; and verify at least one ready document and active chunk.
10. Run private DNS, TLS, authentication, malware, OCR, embedding, inference, authorization-isolation, revocation, retry, and restore tests before production activation.

Relevant worker settings:

```text
PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_WORKER_ENABLED=true
PROJECTPULSE_PULSE_AI_AUTO_QUEUE_ELIGIBLE_DOCUMENTS=true
PROJECTPULSE_PULSE_AI_DOCUMENT_SERVICE_PRINCIPAL_USER_ID=<dedicated application user UUID>
PROJECTPULSE_PULSE_AI_DOCUMENT_EXTRACTION_PREVIEW_ENABLED=true
```

For Test, `.github/workflows/projectpulse-deploy-celar-ai-private-runtime-test.yml`
binds the protected private-service endpoints and bearer-secret references,
verifies the upload root is backed by an AzureFile mount, applies migrations 080
and 081 inside the private network, probes private inference, verifies private
DNS for ClamAV/OCR/embeddings, processes and approves the exact SOW version via
the audited API, and requires a citation-grounded FlowHive response. The
workflow restores the previous images and touched Container App settings when
application activation or UAT fails.

## Acceptance evidence

Do not call the path 100% ready until the live readiness response reports:

- `private_rag_ready`;
- private inference and embedding endpoints accepted by private DNS policy;
- private inference bearer authentication configured;
- hybrid retrieval ready, or a time-bounded lexical-only approval;
- private document runtime ready;
- zero current blockers;
- at least one approved ready SOW/GSD and active indexed chunks; and
- no raw document, chunk, vector, person, customer, project, or financial context sent to Claude or OpenAI.
