# Contract: module025-reference-source

- **Status:** frozen v1
- **Owner:** Module 025 SOW/GSD generation (`PulseAiPrivateRagService` + `Module025SowGsdModule`) and canonical-reference admin module

## Scope note

Phase 1 supports the **canonical template** reference kind only. `PriorVersion`
(same-engagement, customer scope) and live SharePoint reads are **deferred** to a later
phase and are NOT part of v1.

## Exposes

- **HTTP** `POST /api/module025/sow-gsd/{engagementId}/generate` — optional JSON body
  (backward compatible; empty/absent body = today's behavior):
  ```json
  { "canonicalReferenceId": "uuid|null" }
  ```
  Body carries an **identifier only** — never reference text.
- **HTTP** canonical-reference management (admin-only except list/detail):
  - `POST /api/module025/canonical-references` (multipart: template `.docx` + `label`, optional `projectName`) — admin
  - `PUT /api/module025/canonical-references/{id}` (edit `label`/`projectName`/`source_text`/`active`) — admin
  - `POST /api/module025/canonical-references/{id}/replace` (re-upload `.docx`) — admin
  - `GET /api/module025/canonical-references[?active=true]` — readable with generation authority
  - `GET /api/module025/canonical-references/{id}` — detail/preview
- **Internal type** `Module025ReferenceSource(Kind=Canonical, ReferenceId, Label,
  ReferenceText, SavedAt)` (server-owned; `internal`) attached to
  `CelarAiAuthoritativeScopeEvidence.Reference` → `CreateModule025ReferenceScopeSource` →
  citation-2 `PulseAiPrivateRetrievedChunk`.
- **Provenance** on the generated answer/audit: citation 2 with
  `Classification = "author_selected_reference_scope"`,
  `SourceType = "module025_reference_scope"`, `RankOrder = 2`.

## Consumes

- `module025_canonical_references` (new additive table; text-only, no customer columns).
- `PulseAiPrivateDocumentExtractionService` (`.docx` → text on upload/replace).
- `PulseAiEscalationSanitizer` / `Module025ExternalSowAdapter` neutralization (defensive).
- Existing `CreateModule025AuthoritativeScopeSource` / `Module025AuthoritativeScopeRetrieval`.

## Schema / wire

- `Module025ReferenceKind { Canonical }` (`PriorVersion` reserved, not implemented in v1).
- `Module025ReferenceSource(Kind, ReferenceId, Label, ReferenceText, SavedAt)` — `internal`,
  constructed server-side only.
- Failure sentinel: `module025_reference_scope_invalid` (residual canonical identity/collision,
  or inactive/unknown id) → generation `Blocked`, draft unchanged.
- Kill-switch: reference path + canonical endpoints gated by a dark-launch flag (default OFF).

## Versioning

Frozen at **v1**. Changes are **additive only** — a breaking change is a NEW contract, not
an edit (framework-spec §4.3). The client→server boundary accepts identifiers only; reference
text is always server-loaded — a security invariant, not a soft convention. Adding the
`PriorVersion` kind later is an additive extension of `Module025ReferenceKind` + a new
`referenceVersionId` field.
