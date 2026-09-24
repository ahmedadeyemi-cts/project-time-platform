# Security Invariants

> Defined by the **Security Auditor**; ENFORCED by the **Reviewer** on every PR
> (framework-spec §6). Verify each against the ACTUAL diff/source — not the PR
> description. Edit this list as the system's threat surface grows.

- [ ] Least-privilege tool/permission registries — a mode is never offered tools it doesn't need
- [ ] Caller-scoped reads are NON-WIDENABLE (a user can only ever see their own data)
- [ ] Path / symlink confinement on any file access
- [ ] External or consequential actions are draft-then-confirm, never autonomous
- [ ] No secrets in the repo, image, or source snapshot (names/locations only)
- [ ] Migrations are additive (no destructive schema change)
- [ ] AuthN/AuthZ enforced on every new endpoint
- [ ] Inputs validated; outputs escaped (no injection / XSS)

## Feature: Module 025 SOW reference sources (Phase 1)

> Surface: author-selected **canonical template** reference injected as a co-equal
> authoritative citation (citation 2) into Module 025 generation. Templates are
> admin-uploaded and imported into the app (no live SharePoint). Branch
> `feature/module025-sow-reference-sources-20260923`. The Reviewer MUST verify each
> of these against the actual diff, not the PR description.
>
> Scope: Phase 1 is **canonical templates only**. The `PriorVersion` (same-engagement,
> customer scope) kind and live SharePoint reads are DEFERRED to a later phase.

- [ ] **No customer data in Phase 1** — the canonical store (`module025_canonical_references`)
  has no customer columns/FK; no customer files or engagement scope are read. Templates are
  identity-free by construction.
- [ ] **Server-owned evidence boundary intact** — the client submits only a canonical
  *identifier*. Reference *text* is ALWAYS loaded server-side from the canonical store;
  `Module025ReferenceSource` and `CelarAiAuthoritativeScopeEvidence` stay `internal`; the
  public Celar compose endpoint never accepts caller-asserted reference content.
- [ ] **Canonical management is admin-only** — create/edit/replace/delete of canonical
  references require an administrator; author selection is limited to `active` references and
  requires `HasGenerationAuthority`. No view-as / impersonation may create, edit, or inject.
- [ ] **Uploads are validated** — accept `.docx` only (magic-byte detection, not extension
  alone), enforce size caps and the existing extraction-safety limits
  (`PulseAiPrivateDocumentExtractionService`); stored `source_text` is length-capped
  (≤ ServiceOverview budget).
- [ ] **Canonical text neutralized (defensive) before injection** — even though templates
  should be identity-free, injected text passes identity neutralization (customer names,
  named people, identifying facts) via the existing
  `Module025ExternalSowAdapter`/`PulseAiEscalationSanitizer` patterns. Residual identity or
  collision → fail closed (`module025_reference_scope_invalid`); raw text is NEVER injected.
- [ ] **Selection is durable and re-validated at execution** — the queued selection is bound
  to `generationId` + engagement revision; the background worker RE-LOADS and RE-VALIDATES the
  reference (still exists, still `active`, neutralization) at execution time and never trusts
  the queued value blindly.
- [ ] **No reference-derived identity leaks into output** — existing external-output identity
  validation still runs so nothing from a reference can surface as an identity in the draft.
- [ ] **Citation provenance is distinct** — the reference chunk carries its own classification /
  source type / rank (citation 2) so audit and citation records never conflate author-saved
  primary scope with an author-selected canonical reference.
- [ ] **Migration additive** — `module025_canonical_references` is additive only; no destructive
  schema change.

> **Deferred-kind guardrails (enforce when the later phase adds them, not now):**
> prior-version references must be same-engagement + same-customer (name + engagement number),
> fail-closed on mismatch; live SharePoint reads need their own trust-boundary review.
