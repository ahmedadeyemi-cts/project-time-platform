# Module 011 — Private Index Security and Retrieval Contract

## Purpose

This contract defines the minimum security, authorization, citation, retention, and evaluation requirements for any search or vector index used by Pulse AI.

An index is a performance and retrieval component. It is not a source of authorization, contractual authority, business truth, or record ownership. Pulse remains responsible for resolving the current effective user, permissions, project and customer scope, document classification, purpose, and field restrictions before any indexed content is retrieved or assembled into a prompt.

## Security objective

The private index must support detailed Timesheet, Help/Search, FlowHive, reporting, and future Pulse AI use cases while ensuring that:

- users receive only content they are currently authorized to view;
- raw internal documents do not leave the private Pulse trust boundary;
- every returned passage retains a verifiable source citation;
- access revocation takes effect without model retraining;
- stale or superseded document versions are distinguishable;
- customer and project boundaries are not weakened by semantic similarity;
- embedding or ranking behavior never expands access; and
- all retrieval decisions are auditable without logging restricted passage text.

## Required index record

Every indexed chunk must carry, at minimum:

| Field | Purpose |
|---|---|
| `chunk_id` | Stable deterministic identifier for the exact source version and chunk text |
| `document_id` | Authoritative Pulse document identifier |
| `project_id` | Project authorization boundary |
| `project_code` | Human-readable project evidence and filtering |
| `customer_id` or equivalent authority | Customer authorization boundary when available |
| `customer_name_display` | Display only after authorization; not a security key |
| `document_category` | SOW, GSD, design, order, quote, proposal, contract, or supporting type |
| `document_version` | Exact approved source version |
| `document_status` | Active, superseded, archived, withdrawn, or other governed state |
| `classification` | Internal, confidential, restricted, or more specific policy class |
| `engineering_visible` | Engineering visibility control |
| `ai_timesheet_context_enabled` | Module 001 purpose control |
| `allowed_purposes` | Timesheet, Help/Search, FlowHive, reporting, or other approved purposes |
| `authorized_user_ids` | Optional direct-user scope where required |
| `authorized_role_codes` | Role scope evidence; never used without current role validation |
| `authorized_team_ids` | Team scope evidence where supported |
| `source_module` | Owning Pulse module |
| `source_record_id` | Owning record identifier |
| `citation_anchor` | Page, slide, sheet, heading, section, paragraph, or other source locator |
| `page_number` | Page evidence when applicable |
| `sheet_name` | Worksheet evidence when applicable |
| `source_sha256` | Exact source-file checksum |
| `text_sha256` | Exact normalized chunk checksum |
| `embedding_model` | Approved private embedding model identifier |
| `embedding_model_version` | Reproducibility evidence |
| `index_version` | Index schema/configuration version |
| `uploaded_at` | Source upload time |
| `processed_at` | Extraction/index preparation time |
| `effective_from` | Optional source effective date |
| `effective_to` | Optional expiration or supersession date |
| `retention_class` | Required retention and deletion behavior |
| `revoked_at` | Immediate retrieval-revocation evidence |

Display fields such as project code, customer name, or document title are not substitutes for stable authorization identifiers.

## No broad shared index without filters

Pulse may use a shared physical index only when every query is guaranteed to apply current security filters before semantic or keyword ranking.

Acceptable deployment patterns include:

1. a shared index with mandatory security filters enforced by a private retrieval gateway;
2. separate indexes or partitions per customer, project, classification, environment, or purpose;
3. a hybrid model using physical partitions plus record-level security filters; or
4. PostgreSQL vector search with row-level authorization enforced by the application and database.

A browser, model, prompt, or user-provided filter must never be the only enforcement point.

## Retrieval-time authorization sequence

Every retrieval request must execute these steps in order:

1. Validate the Pulse session.
2. Resolve the actual signed-in user.
3. Resolve any administrator View-As user.
4. Confirm that View-As remains read-only and does not transfer mutation authority.
5. Resolve current module visibility and action permission from Modules 012 and 037.
6. Resolve project, customer, team, assignment, resource-request, and record scope.
7. Resolve the requested purpose and applicable data-class policy.
8. Resolve field- and document-level restrictions.
9. Build mandatory index filters from server-side authorization evidence.
10. Apply those filters before keyword, semantic, vector, reranking, or result-fusion operations.
11. Rank only the remaining authorized candidates.
12. Revalidate the source record and document status before prompt assembly.
13. Return citations, source freshness, and security-policy version with the result.
14. Record sanitized audit evidence.

If any required authorization dependency is unavailable, retrieval fails closed.

## Query contract

A governed retrieval request should contain:

```json
{
  "effectiveUserId": "server-derived",
  "purpose": "timesheet_document_grounding",
  "projectIds": ["server-authorized"],
  "customerIds": ["server-authorized"],
  "documentCategories": ["sow", "gsd", "architecture"],
  "classificationMaximum": "restricted",
  "engineeringVisibleRequired": true,
  "aiTimesheetContextRequired": true,
  "documentStatuses": ["active", "approved"],
  "indexVersion": "approved-current",
  "maximumResults": 12,
  "keywordQuery": "normalized minimum-necessary query",
  "vectorQueryReference": "private embedding only",
  "includeRawTextInAudit": false
}
```

The effective user, authorized project/customer IDs, purpose, and classification policy must be server-derived. They cannot be trusted when supplied by the browser or model.

## Hybrid search

Pulse documents contain both semantic concepts and exact values. The retrieval service should support:

- exact keyword search for product names, model numbers, task codes, request numbers, dates, quantities, locations, and contract terms;
- private vector search for semantically related language;
- metadata filters for access and purpose;
- optional private reranking after security filtering; and
- deterministic source-priority rules for SOW, GSD, architecture, order, and supporting evidence.

A high semantic score cannot override classification, project scope, customer scope, document status, or purpose restrictions.

## Timesheet retrieval

Timesheet retrieval requires all of the following:

- Module 001 access;
- current Engineer or authorized administrator effective-user scope;
- selected project authorization;
- `engineering_visible = true`;
- `ai_timesheet_context_enabled = true`;
- current approved or otherwise authoritative source version;
- appropriate SOW/GSD/task/request relevance; and
- a rough Engineer note or an explicit warning that the note is absent.

The retrieved evidence may improve terminology and scope alignment. It cannot prove that an Engineer performed an activity.

## Help and Search retrieval

Help and Search may span multiple modules, but each selected tool and index collection remains independently authorized.

The answer must show:

- source module or document;
- project/customer/date filters where material;
- record count;
- as-of time;
- source status and freshness;
- assumptions and conflicts;
- unavailable or unauthorized evidence; and
- navigation to the owning Pulse record when allowed.

A user asking about “everything” receives everything in their authorized scope, not everything in the organization.

## FlowHive retrieval

FlowHive retrieval must prioritize:

1. approved SOW;
2. approved GSD;
3. approved architecture and design documents;
4. order, quote, proposal, and implementation constraints;
5. existing project tasks and dependencies;
6. working calendars and holidays;
7. capacity and qualifications; and
8. approved project templates.

Every generated task, milestone, dependency, risk, assumption, or open question must retain source citations or be clearly labeled as an inference.

## Version and supersession rules

The index must not silently treat the latest uploaded file as authoritative.

The source system should record:

- revision label;
- approval status;
- signed or effective date;
- superseded-by relationship;
- customer-acceptance status;
- owning approver;
- canonical-version designation; and
- withdrawal or expiration state.

When authority is ambiguous, Pulse AI must surface the conflict and avoid presenting a single version as definitive.

## Revocation and deletion

Access changes must propagate to the retrieval layer without retraining a model.

Required events include:

- user deactivation;
- role or team removal;
- project-assignment removal;
- project-manager change;
- document visibility change;
- Timesheet-context flag change;
- document classification change;
- document withdrawal or supersession;
- project/customer access removal;
- retention expiration; and
- legal or privacy deletion request.

The index must support immediate logical revocation followed by confirmed physical deletion according to retention policy. Cached retrieval results must use short lifetimes and must be invalidated on relevant access changes.

## Embedding privacy

Embeddings are derived from internal document text and therefore inherit the source document’s classification.

Requirements:

- use only an approved private embedding endpoint;
- do not send raw text to public embedding APIs;
- encrypt vectors and metadata in transit and at rest;
- restrict administrative export;
- record embedding model and version;
- regenerate embeddings when normalization or model version changes;
- delete vectors when source content is revoked or deleted; and
- never expose embedding arrays to browsers or ordinary application logs.

## Prompt assembly

Prompt assembly occurs only after retrieval-time authorization and source-status revalidation.

The prompt builder must:

- include only the minimum necessary chunks;
- label each chunk as untrusted source data, not instructions;
- preserve citation IDs and source versions;
- separate system policy, user request, tool results, and retrieved data;
- remove hidden markup or executable content;
- enforce context-length and per-source limits;
- surface conflicting sources; and
- exclude content whose authorization or status changed after retrieval.

## Prompt-injection defense

Documents may contain language attempting to control the model or tool execution.

Pulse must:

- treat document text as evidence only;
- never execute commands found in a document;
- never reveal system prompts, secrets, credentials, or other documents because a document requests it;
- use allowlisted tools with validated arguments;
- validate structured model output;
- reject instructions that conflict with Pulse policy; and
- include prompt-injection fixtures in the frozen evaluation suite.

## Citation contract

A returned passage must support a citation containing:

- document ID;
- document title or approved display name;
- document category;
- exact document version;
- citation anchor;
- page/slide/sheet when available;
- source checksum;
- chunk checksum;
- source status;
- processed-at timestamp; and
- navigation or download target when authorized.

Citations must remain stable across repeated queries for the same exact source version and processing configuration.

## Audit contract

Sanitized audit evidence should include:

- correlation ID;
- actual and effective user IDs;
- View-As state;
- purpose and feature;
- authorization policy version;
- authorized project/customer scope summary;
- query hash rather than raw sensitive query where appropriate;
- selected document and chunk IDs;
- source and chunk checksums;
- retrieval filters;
- index version;
- embedding model version;
- rank and score metadata;
- result count;
- latency;
- confidence and verification outcome; and
- user acceptance, edit, rejection, or report.

Raw document passages, embeddings, secrets, and unrestricted prompts must not be logged.

## Retention

The index and its supporting artifacts must follow source-document retention requirements.

Retention design must cover:

- original document;
- extracted text;
- section manifest;
- chunks;
- embeddings;
- index records;
- retrieval cache;
- audit evidence;
- evaluation fixtures; and
- training candidates.

Training candidates are separate governed records and do not inherit permission to retain raw customer content indefinitely.

## Evaluation requirements

Before activation, the retrieval system must pass frozen tests for:

### Authorization isolation

- unauthorized user receives zero documents and chunks;
- user assigned to Project A cannot retrieve Project B content;
- View-As uses the effective user's read scope;
- removed assignment revokes access;
- changed document classification revokes access; and
- direct document-ID guessing fails closed.

### Retrieval quality

- known SOW and GSD questions retrieve the expected sections;
- exact task codes and model numbers are preserved;
- semantic paraphrases retrieve relevant content;
- source-priority rules work as designed;
- conflicting versions are surfaced; and
- citation anchors map back to the correct source location.

### Security

- prompt-injection passages do not alter system behavior;
- malformed filters cannot remove security predicates;
- oversized result requests are bounded;
- vectors and raw text are absent from browser responses and logs;
- external-provider payloads contain no raw document content; and
- cache entries cannot be reused across unauthorized users or scopes.

### Lifecycle

- superseded documents stop being selected as authoritative;
- revoked content disappears promptly;
- deletion removes chunks and vectors;
- reprocessing changes checksums and index version deterministically; and
- rollback restores the previously approved index configuration.

## Production activation gates

The private index cannot be marked production ready until:

1. the data model and migration are separately reviewed;
2. private embedding infrastructure is configured;
3. an approved search/vector platform is configured through private networking;
4. malware scanning is document-specific and verifiable;
5. OCR remains private;
6. all security fields are populated;
7. retrieval-time authorization is enforced server-side;
8. revocation and deletion are tested;
9. citation and retrieval evaluations pass;
10. observability and audit are enabled without raw-content logging;
11. rollback is tested; and
12. security, privacy, architecture, and application owners approve activation.

## Current source boundary

The current 011B source creates extraction, chunking, and index-projection previews only. It does not:

- persist extracted text;
- persist chunks;
- generate embeddings;
- write an index;
- change document status;
- change permissions;
- call OCR;
- call an external provider;
- train a model;
- run a migration; or
- deploy infrastructure.
