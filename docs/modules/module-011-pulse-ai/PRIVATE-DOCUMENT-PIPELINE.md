# Module 011 — Pulse AI Private Document Processing Pipeline

## Status

| Field | Value |
|---|---|
| Phase | 011B — Private Document Processing and Permission-Aware Index |
| Source branch | `feature/module-011-private-document-pipeline-20260729` |
| Exact parent | `feature/module-011-pulse-ai-deep-intelligence-20260728@0b9cc8d74fecf19c672ea07f6062acecbc87883c` |
| Runtime posture | Read-only, private processing preview |
| Database migration | None |
| Database mutation | None |
| OCR execution | None |
| Embedding execution | None |
| Vector-index write | None |
| External provider call | None |
| Deployment | None |

## Purpose

This phase builds the private document-processing layer required for Pulse AI to use SOW, GSD, architecture, design, order, quote, proposal, spreadsheet, and supporting documents without sending raw internal content to Claude, OpenAI, or another public external model.

The phase creates a secure and inspectable path from an authorized stored document to:

1. permission-aware inventory;
2. storage-path confinement and file admission;
3. private text extraction;
4. OCR requirement detection;
5. citation-preserving sections;
6. deterministic chunks;
7. permission-scoped index projections; and
8. production-readiness evidence.

The implementation deliberately stops before persistence, embeddings, index writes, OCR execution, model execution, or external transmission. This allows the extraction and security boundary to be reviewed independently from infrastructure activation.

## Relationship to earlier Pulse AI phases

PR #219 established the Module 011 lifecycle and governance foundation. PR #221 added the deep-intelligence planning, document-grounding metadata, Timesheet integration, Help/Search planning, FlowHive private-first contract, financial semantic planning, and DLP preview.

This phase consumes the same effective-user and project-scope principles while adding the missing native document-processing capability. It does not modify or deploy PR #221 and is stacked on its exact source head.

## End-to-end processing sequence

```text
Authenticated Pulse request
        |
        v
Resolve actual and effective user
        |
        v
Apply module, action, project, customer, record, and document scope
        |
        v
Load private document metadata and internal storage reference
        |
        v
Admission controls
  - upload-root confinement
  - regular-file requirement
  - no symbolic link or reparse point
  - extension allowlist
  - file-signature validation
  - size limits
  - Open XML expansion limits
  - malware-scan attestation
        |
        v
Private extraction
  - PDF
  - DOCX
  - PPTX
  - XLSX
  - HTML / XML
  - CSV / Markdown / JSON / text
        |
        v
OCR requirement detection
        |
        v
Citation-preserving sections
        |
        v
Deterministic chunks, overlap, token estimates, checksums
        |
        v
Permission-scoped index projection
        |
        v
Evaluation and production blockers
```

## Authorization model

Document processing occurs only after the backend resolves the effective user and verifies that the document belongs to an authorized project.

Current source recognizes the same conservative scope used by the preceding Pulse AI grounding phase:

- Super Administrator, Administrator, Project Team Coordinator, and Executive organization-level document scope;
- Project Manager ownership;
- direct project assignment;
- fulfilled or assigned engineering resource request; and
- engineering resource-request assignment.

Only active, engineering-visible documents are returned by the pipeline inventory. A document that is visible to an administrator but not an Engineer is never made available to that Engineer merely because Pulse AI can technically parse it.

The model, extractor, embedding service, and index are not authorization authorities. Pulse remains the authority.

## Storage boundary

A document is eligible for private parsing only when its normalized storage path remains underneath the configured `PROJECTPULSE_UPLOAD_ROOT`.

The pipeline rejects:

- relative traversal outside the upload root;
- paths resolving outside the upload root;
- missing files;
- directories;
- symbolic links and reparse points;
- zero-length files;
- files larger than the configured limit; and
- metadata/file-size mismatches as an unresolved warning.

Storage paths are never returned to the browser.

## Supported formats

| Format | Extension | Extraction method | Citation unit |
|---|---|---|---|
| PDF | `.pdf` | PdfPig content-order text extraction | Page |
| Word Open XML | `.docx` | Open XML paragraph and heading extraction | Heading/section |
| PowerPoint Open XML | `.pptx` | Slide XML text extraction | Slide |
| Excel Open XML | `.xlsx` | ClosedXML formatted-cell extraction | Worksheet |
| Plain text | `.txt` | Private UTF text reader | Section |
| Markdown | `.md` | Private UTF text reader | Section |
| CSV | `.csv` | Private UTF text reader | Section |
| JSON | `.json` | Private UTF text reader | Section |
| XML | `.xml` | XML text-node extraction | Section |
| HTML | `.html`, `.htm` | Script/style removal, tag removal, HTML decode | Section |

Macro-enabled Office formats, executable files, scripts, binary installers, and general-purpose archives are explicitly blocked.

Legacy binary Office formats such as `.doc`, `.xls`, and `.ppt` are not silently parsed. They require a separately approved conversion service or user re-upload in a supported format.

## Malware and hostile-file controls

A supported extension does not prove that a document is safe.

Before parsing, the source pipeline validates:

- extension allowlist;
- signature and extension agreement;
- file size;
- path confinement;
- regular-file state;
- symbolic-link/reparse-point absence;
- macro-format exclusion;
- Open XML entry count;
- individual expanded-entry size;
- total expanded size; and
- compression ratio.

A verifiable malware-scan attestation is required before content parsing. The preview remains blocked when `PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED` is not enabled by an approved runtime process.

The attestation setting is an integration gate, not a malware scanner. Production activation still requires a real scanner that produces document-specific evidence, not a manually asserted global flag.

## PDF processing

The private PDF extractor preserves one section per page and uses content-order text extraction. It records:

- page count;
- processed page count;
- character count;
- estimated token count;
- page anchors;
- page text checksums;
- source document checksum; and
- OCR requirement evidence.

PDF is a presentation format, so extracted text order may not always match visual reading order. Retrieval and evaluation must therefore include representative SOW and GSD PDFs with columns, tables, headers, footers, and diagrams.

Image-only or text-sparse PDFs are marked `ocr_required`. This phase does not call an OCR service.

## DOCX processing

DOCX files are opened as constrained Open XML packages. The extractor reads `word/document.xml`, preserves paragraph order, recognizes heading/title styles, and groups body text beneath the applicable heading.

It does not execute macros, external relationships, embedded scripts, or document instructions.

## PPTX processing

PPTX files are opened as constrained Open XML packages. Each slide becomes a source section with a stable slide anchor. Text is gathered from DrawingML text runs in slide order.

Speaker notes, embedded objects, media, and external relationships are not automatically trusted or executed. A later phase may add separately evaluated notes extraction.

## XLSX processing

XLSX files are opened through the existing private ClosedXML dependency. Each worksheet becomes a source section. Formatted cell values are emitted in row and column order with tab separation.

The extractor does not evaluate untrusted formulas by running Excel, invoke macros, follow external links, or retrieve linked data.

## HTML, XML, and text processing

HTML extraction removes script and style blocks, removes markup, decodes entities, and normalizes text. XML extraction reads text nodes without executing external entities. Text-family formats use BOM-aware UTF reading with a configured maximum character count.

All output is normalized to stable line endings, repeated spaces are reduced, null characters are removed, and excessive blank lines are collapsed.

## Section and citation model

Every extracted section contains private text and non-secret evidence:

- section index;
- source anchor;
- title;
- page number, slide number, or worksheet name when applicable;
- character count; and
- SHA-256 text checksum.

The public processing-preview API returns evidence only. It does not return section text.

Citation examples:

```text
page:14
slide:7
sheet:resource-plan
DOCX section 5 — Implementation Responsibilities
```

## Chunking model

Chunks are created deterministically from normalized section text.

Default behavior:

- target: 2,400 characters;
- overlap: 280 characters;
- natural break preference: newline, sentence punctuation, semicolon, colon, or space;
- maximum chunks: 1,500 per preview;
- token estimate: approximately four characters per token;
- stable source anchor retained on every chunk; and
- SHA-256 checksum for every chunk.

A chunk ID is derived from:

```text
document ID
source document checksum
citation anchor
chunk sequence
chunk text checksum
```

This makes duplicate detection, reprocessing comparison, and citation validation possible without exposing the private text.

## Document version authority

Upload time alone is not contractual authority.

When multiple active SOW or GSD documents exist, Pulse AI must require an explicit authority rule based on one or more of:

- approved status;
- signed or effective date;
- revision identifier;
- superseded-by relationship;
- owning business approver;
- customer acceptance; or
- explicit canonical-document designation.

The processing preview surfaces version-authority questions instead of choosing the newest file silently.

## Permission-scoped index projection

This phase prepares, but does not persist, one index projection per chunk.

Required metadata includes:

- chunk ID;
- document ID;
- project ID and project code;
- project and customer context;
- document category and version;
- classification;
- engineering visibility;
- Timesheet-context eligibility;
- effective access-scope contract;
- citation anchor;
- page or worksheet evidence;
- source checksum;
- chunk checksum;
- character and token estimates;
- embedding state; and
- index state.

No vector is returned by the API.

## Retrieval-time authorization

Index-time security metadata is necessary but not sufficient. Every retrieval request must also apply current authorization before returning a match.

Required retrieval sequence:

1. resolve current actual and effective identities;
2. resolve current module and action permission;
3. resolve current project/customer/team/record scope;
4. apply document classification and purpose restrictions;
5. filter candidates before ranking;
6. rank only the remaining authorized candidates;
7. return source citations and freshness; and
8. re-check authorization before prompt assembly.

A user losing access to a project must lose search and RAG access without retraining the model.

## New API surface

```text
GET /api/pulse-ai/v1/documents/pipeline/readiness
GET /api/pulse-ai/v1/documents/inventory
GET /api/pulse-ai/v1/documents/{documentId}/processing-preview
```

All endpoints are authenticated, read-only, and effective-user scoped.

## Runtime configuration contract

| Variable | Purpose | Default behavior |
|---|---|---|
| `PROJECTPULSE_UPLOAD_ROOT` | Private document-storage root | `/opt/project-time-platform/app/uploads` |
| `PROJECTPULSE_PULSE_AI_DOCUMENT_EXTRACTION_PREVIEW_ENABLED` | Permit private in-memory extraction preview | `false` |
| `PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED` | Require approved scan evidence before parsing | `false` |
| `PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCANNER_MODE` | Sanitized scanner-mode label | `not_configured` |
| `PROJECTPULSE_PRIVATE_OCR_ENDPOINT` | Private OCR readiness | absent |
| `PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT` | Private embedding readiness | absent |
| `PROJECTPULSE_PRIVATE_EMBEDDING_MODEL` | Approved embedding-model identifier | absent |
| `PROJECTPULSE_PRIVATE_VECTOR_INDEX` | Permission-scoped index identifier | absent |
| `PROJECTPULSE_PULSE_AI_DOCUMENT_MAX_BYTES` | Maximum document size | 25 MiB |
| `PROJECTPULSE_PULSE_AI_DOCUMENT_MAX_PAGES` | Maximum pages/slides/sheets | 500 |
| `PROJECTPULSE_PULSE_AI_DOCUMENT_MAX_CHARACTERS` | Maximum extracted characters | 2,000,000 |
| `PROJECTPULSE_PULSE_AI_DOCUMENT_MAX_SECTIONS` | Maximum sections | 1,000 |
| `PROJECTPULSE_PULSE_AI_DOCUMENT_MAX_CHUNKS` | Maximum chunks | 1,500 |
| `PROJECTPULSE_PULSE_AI_DOCUMENT_CHUNK_CHARACTERS` | Target chunk size | 2,400 |
| `PROJECTPULSE_PULSE_AI_DOCUMENT_CHUNK_OVERLAP` | Chunk overlap | 280 |

No private endpoint value or credential is returned by the readiness API.

## Production persistence still required

A future separately authorized database and infrastructure phase must define:

- document scan results;
- source checksums and immutable document versions;
- extraction jobs and attempts;
- page/section manifests;
- private extracted-text storage references;
- chunk manifests;
- embedding model and version;
- index version and partition;
- authorization metadata version;
- revocation and deletion state;
- processing audit events;
- retention periods; and
- rollback and reprocessing evidence.

Large extracted text, embeddings, and model artifacts should not be stored indiscriminately in the transactional Pulse database.

## Locked behavior in this phase

This phase cannot:

- update `extraction_status`;
- populate or replace `ai_context_summary`;
- persist extracted text or chunks;
- call OCR;
- generate embeddings;
- write a vector or hybrid index;
- call Claude, OpenAI, or another external provider;
- train or fine-tune a model;
- alter Module 064;
- change Azure or Entra;
- run a migration; or
- deploy itself.

## Acceptance criteria for the next activation phase

1. Every document is scanned before parsing.
2. Path traversal, symlink, signature mismatch, blocked extension, oversized file, and archive-bomb tests fail closed.
3. PDF, DOCX, PPTX, XLSX, and text fixtures extract reproducibly.
4. Image-only PDFs are routed to private OCR and never to public OCR.
5. Page, slide, worksheet, and heading citations survive chunking and retrieval.
6. Unauthorized users receive no metadata, chunk, citation, or search result.
7. Multiple SOW/GSD versions produce an authority conflict instead of silent selection.
8. Revoked access removes retrieval eligibility promptly.
9. Embeddings are generated only by an approved private endpoint.
10. Index writes include all required security metadata.
11. Raw source text, chunks, and embeddings never enter browser telemetry or external provider payloads.
12. Retrieval quality, citation coverage, and authorization isolation pass frozen evaluation suites.
