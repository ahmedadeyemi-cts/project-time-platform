# 0001. Module 025 reference-source trust boundary

- **Status:** Proposed
- **Date:** 2026-09-24

## Context

Phase 1 of the Module 025 SOW reference-source feature lets an author attach one
reference SOW that is injected as a **co-equal authoritative citation (citation 2)**
into detailed scope generation.

The organization keeps SOW/GSD material in two SharePoint locations: a **Client Files**
folder (customer-identified) and a **Sell Templates** folder (project-name templates
with **no customer information**). The app is **not ready to work with customer data**
yet, and — verified against source — has **no live SharePoint/Graph Files integration**
(Graph is wired for mail/Teams only). `.docx` text extraction, however, already exists
(`PulseAiPrivateDocumentExtractionService`).

The existing design deliberately keeps `CelarAiAuthoritativeScopeEvidence` `internal`
and server-owned so a caller cannot assert authoritative evidence, and
`GenerateFlowHivePlanInternalAsync` enforces a same-customer boundary. Adding a second
author-selected authoritative source reopens those trust and scope-bleed questions.

## Decision

**Phase 1 scope = canonical templates only.**

- The **canonical** reference kind sources from the Sell Templates (no customer info),
  **imported into the app** via an **admin upload/edit** path — NOT read live from
  SharePoint. Store is a new additive `module025_canonical_references` table (text-only,
  no customer columns). Templates are extracted to text with the existing extractor and
  are **editable later** (label/text/active/replace).
- The **prior-version (same-engagement, customer scope)** kind and **live SharePoint
  reads** are **deferred** to a later phase (aligned with when the app is ready for
  Client Files / customer data).
- The client submits only a reference **identifier**; reference **text** is always loaded
  server-side. `Module025ReferenceSource` stays `internal`.
- Canonical create/edit/replace/delete are **admin-only**; author selection is limited to
  `active` references and requires the `HasGenerationAuthority` gate.
- Uploads are validated (docx magic bytes, size, extraction-safety); stored text is
  length-capped.
- Canonical text is **identity-neutralized (defensive)** before injection, reusing the
  `Module025ExternalSowAdapter` / `PulseAiEscalationSanitizer` patterns; residual identity
  or collision fails closed (`module025_reference_scope_invalid`).
- The selection is persisted durably against `generationId` + revision and **re-validated
  at execution time** by the worker.

Full enforcement list: `docs/security-invariants.md` → "Module 025 SOW reference sources
(Phase 1)". The Reviewer enforces these per-PR.

## Alternatives considered

- **Live SharePoint read in Phase 1.** Most faithful to "the folder is the source of
  truth," but a large net-new Graph Files integration and trust boundary, and it invites
  the customer-file folder before the app is ready. Rejected for Phase 1; revisit with the
  Client Files phase.
- **Include prior-version (same-engagement) now.** It is already in-app data, but it IS
  customer scope, against the stated "not ready to pull customer info." Deferred.
- **One-time import script for templates.** Simplest, but no later editing without an admin
  UI; the author asked to be able to edit templates. Rejected in favor of an admin
  upload/edit endpoint.
- **Raw canonical injection (no neutralization).** Simpler, but removes the defensive belt
  against a stray identity in a template. Rejected.

## Consequences

- Phase 1 ships with **zero customer data** and no external integration — small, safe blast
  radius; reuses the proven extractor and citation mechanism.
- An admin-managed canonical library (upload/edit) is a new admin surface that needs its own
  least-privilege controls (covered by the invariants).
- Font/visual fidelity remains **Phase 2** (exporter/template concern; not the citation path).
- When the Client Files phase arrives, the prior-version kind and live SharePoint reads slot
  in additively (extend `Module025ReferenceKind`; add `referenceVersionId`); their guardrails
  are pre-noted in the invariants file.
