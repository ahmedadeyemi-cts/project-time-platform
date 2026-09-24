# Stage 1: Module 025 SOW reference sources (Phase 1)

- **Type:** feature
- **Depends on:** none
- **Branch:** `feature/module025-sow-reference-sources-20260923`
- **Security:** `docs/security-invariants.md` → "Module 025 SOW reference sources (Phase 1)"; ADR `docs/adr/0001`

## Objectives

Let a SOW author attach **one** optional **canonical template** reference to Module 025
detailed-scope generation, injected as a **co-equal authoritative citation (citation 2)**
alongside the saved Service Overview (citation 1).

**Phase 1 source = admin-managed canonical templates only.** Templates come from the
"Sell Templates" SharePoint folder but are **imported into the app** (no live SharePoint
integration exists — Graph is mail/Teams only). They contain **no customer information**.

**Deferred to a later phase (explicitly OUT of scope here):**
- The "prior version of the same engagement" reference kind (real customer scope — waits
  until the app is ready to work with the Client Files / customer data).
- Live SharePoint browsing of the Sell Templates / Client Files folders.
- Document font/visual fidelity (Phase 2; an exporter/template concern, not this path).

## What to build

1. **Migration — `database/migrations/` (next number, additive)**
   `module025_canonical_references`:
   - `id uuid pk`, `label text not null`, `project_name text`, `source_text text not null`,
     `original_filename text`, `source_sha256 varchar(64)`, `active boolean not null default true`,
     `created_at timestamptz not null default now()`, `updated_at timestamptz not null default now()`,
     `created_by uuid references app_users(user_id)`, `updated_by uuid references app_users(user_id)`.
   - **No customer FK / no customer columns.** Additive only. Length cap on `source_text`
     consistent with the ServiceOverview budget (≤ 30_000).

2. **Admin canonical-reference management (admin-only) — new module e.g. `Module025CanonicalReferenceModule.cs`**
   - `POST /api/module025/canonical-references` — multipart upload of a template SOW `.docx`
     (+ `label`, optional `projectName`). Validate type (magic bytes) + size; extract text via
     `PulseAiPrivateDocumentExtractionService`; store `source_text` + `source_sha256`. Admin-only.
   - `PUT /api/module025/canonical-references/{id}` — **edit later**: update `label`,
     `projectName`, `source_text`, `active`. Admin-only.
   - `POST /api/module025/canonical-references/{id}/replace` — re-upload a `.docx` to refresh
     `source_text` (re-extract). Admin-only.
   - `GET /api/module025/canonical-references` — list (management view = all; author selection
     view = `active=true`). Readable by users with generation authority.
   - `GET /api/module025/canonical-references/{id}` — detail / extracted-text preview.
   - Deactivate via `active=false` (soft); hard delete optional/admin-only.

3. **Contract — `src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformContracts.cs`**
   - Add internal enum `Module025ReferenceKind { Canonical }` (PriorVersion reserved for later).
   - Add internal record `Module025ReferenceSource(Module025ReferenceKind Kind, Guid ReferenceId,
     string Label, string ReferenceText, DateTimeOffset SavedAt)`. Keep `internal`.
   - Add optional `Module025ReferenceSource? Reference` to `CelarAiAuthoritativeScopeEvidence`.

4. **RAG service — `src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs`**
   - `CreateModule025ReferenceScopeSource(Module025ReferenceSource)` → `PulseAiPrivateRetrievedChunk`
     at `RankOrder: 2`, `Classification: "author_selected_reference_scope"`,
     `SourceType: "module025_reference_scope"`, anchor "Reference Template". Canonical text is
     passed through **defensive** identity neutralization
     (`Module025ExternalSowAdapter`/`PulseAiEscalationSanitizer` patterns) before hashing;
     residual identity/collision → fail closed.
   - `GenerateFlowHivePlanInternalAsync`: build a chunk **list** `[primary, reference?]`. Keep the
     same-customer guard on the primary. On any reference failure →
     `Blocked(..., "module025_reference_scope_invalid", ...)`.
   - `Module025AuthoritativeScopeRetrieval` takes the chunk list.
   - Update the co-equal prompt + `FlowHive{System,User}Instruction` so citation 1 (saved Service
     Overview) and citation 2 (canonical template) are both authoritative scope inputs. Make clear
     the template is a structural/scope precedent, not customer-specific truth.

5. **Generation wiring — `src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs`**
   - `/generate` accepts optional body `{ Guid? CanonicalReferenceId }`. Validate the id is an
     `active` canonical reference. Enforce `HasGenerationAuthority`. Persist selection in the
     `ai_generation_queued` `evidence_json`.
   - `ExecuteGenerationAsync`: **re-load and re-validate** the selection by `generationId`; load the
     canonical `source_text`, build `Module025ReferenceSource`, attach to `evidence`.

6. **Kill-switch** — a dark-launch flag (default OFF) gates the whole reference path AND the
   canonical-reference endpoints. OFF ⇒ `/generate` ignores any reference selection; behavior is
   identical to today.

## Interface contracts

- **Exposes:** `Module025ReferenceSource` (internal); canonical-reference admin/list API;
  `/generate` optional `canonicalReferenceId`; citation-2 provenance (`module025_reference_scope`).
- **Consumes:** `PulseAiPrivateDocumentExtractionService` (docx→text), existing
  `CreateModule025AuthoritativeScopeSource` / `Module025AuthoritativeScopeRetrieval`,
  neutralization patterns.
- **Must not break:** the internal server-owned `CelarAiAuthoritativeScopeEvidence` boundary; the
  existing single-source generation path (reference optional/absent).

## Testing requirements

- Co-equal citation 2 present when a canonical reference is supplied; unchanged single-citation
  behavior when absent (regression).
- Upload: valid `.docx` extracted + stored; non-docx / oversize rejected; extraction-safety honored.
- Edit: `PUT` updates label/text/active; `replace` re-extracts; deactivated references are not
  selectable by authors.
- AuthZ: create/edit/replace/delete require admin; list(active) readable with generation authority;
  non-admin create/edit rejected.
- Neutralization still runs defensively; residual identity → fails closed.
- `/generate` persists selection and the worker re-validates it (durability); inactive/unknown id rejected.

## Acceptance conditions

- [ ] Kill-switch / dark-launch flag (default OFF) gates the reference path + canonical endpoints; OFF = today's behavior
- [ ] Phase 1 stores **no customer data**; canonical table has no customer columns/FK
- [ ] Reference *text* is server-side only; client submits identifiers only; `Module025ReferenceSource` + `CelarAiAuthoritativeScopeEvidence` stay `internal`
- [ ] Canonical create/edit/replace/delete are **admin-only**; author selection is limited to `active` references
- [ ] Uploads validated (docx magic bytes + size + extraction safety); `source_text` length-capped
- [ ] Canonical references identity-neutralized (defensive) before injection; residual identity → fail closed; raw text never injected
- [ ] Selecting a reference requires `HasGenerationAuthority`; selection persisted durably and **re-validated at execution**
- [ ] Distinct citation-2 provenance (classification/source type/rank) in audit + citation records
- [ ] Existing external-output identity validation still runs
- [ ] Additive migration only (`module025_canonical_references`); no destructive schema change
- [ ] Existing suite stays green; CI all-green
- [ ] All Phase 1 invariants in `docs/security-invariants.md` verified by the Reviewer against the diff

## Pipeline test: NO
