# Stage 4: Module 025 admin UI for SOW canonical-reference (content) templates

- **Type:** feature
- **Depends on:** 2
- **Contract:** consumes `contracts/module025-reference-source.md` (Phase 1, already frozen v1)

## Objectives

Deliver the **SOW half** of "load SOW templates and edit them in the app" with minimal risk,
by putting a frontend on the Phase 1 canonical-reference backend that **already exists** but
has no UI and is dark-launched. These are the *content/citation* templates (they steer what
the generated SOW says as "citation 2") — NOT the output-format templates (those are the
larger, riskier track deferred to a later intake). No exporter changes here.

> Scope guard: this stage is UI + safe enablement over an existing, contracted backend. It does
> NOT touch the exporters, the `module025_template_candidates` binary catalog, or GSD content
> references (a GSD equivalent is a separate deferred stage).

## What to build

1. **Canonical-reference admin UI (frontend, `src/frontend/project-time-web/src/module025/`).**
   Add a management view (new component, e.g. `CanonicalReferenceLibrary.jsx`, surfaced from the
   existing Templates nav in `SowGsdAuthoringWorkspace.jsx` — kept distinct from the immutable
   `TemplateCatalog` candidates view). Wire to the existing endpoints (no backend route changes):
   - `GET /api/module025/canonical-references[?active=true]` — list (management view = all;
     author-facing selection = active only).
   - `GET /api/module025/canonical-references/{id}` — detail / extracted-text preview.
   - `POST /api/module025/canonical-references` — upload `.docx` (+ `label`, optional `projectName`).
   - `PUT /api/module025/canonical-references/{id}` — **edit** `label`/`projectName`/`sourceText`/`active`.
   - `POST /api/module025/canonical-references/{id}/replace` — re-upload `.docx` (re-extract).
   - `DELETE /api/module025/canonical-references/{id}`.
   Use the existing `requestJson` / `protected-download.js` session pattern.

2. **Role gating (frontend, server-driven).** Create/edit/replace/delete controls visible only
   when the server reports admin (`access.isAdministrator && !access.isViewAs`); list/detail
   available with generation authority; View-As is read-only. Do not compute roles client-side —
   read server-provided flags, matching the rest of the workspace.

3. **Author-side selection (optional, additive).** In the Service Scope / generate step, let an
   author pick an **active** canonical reference to attach (sends `canonicalReferenceId` only —
   the existing generate contract). Identifier only; never send reference text from the client.

4. **Dark-launch enablement.** The whole path is gated by the existing
   `PROJECTPULSE_MODULE025_REFERENCE_SOURCES_ENABLED` flag (default OFF). This stage adds the UI
   behind that same flag; enabling it in an environment is a deploy/config decision for the
   Operator, not a code default change. When OFF, the UI entry point is hidden and endpoints 404
   (today's behavior).

## Interface contracts

- **Exposes:** canonical-reference management UI; author selection control (uses existing
  `canonicalReferenceId` body on `/generate`).
- **Consumes:** the frozen `module025-reference-source` API (no route/shape changes); existing
  auth flags; the existing kill-switch flag.
- **Must not break:** `module025-reference-source` v1 (client sends identifiers only; reference
  text stays server-side; endpoints stay admin-gated for writes); the immutable
  `TemplateCatalog` candidates view; the `module025-output-standards` contract (no exporter change).

## Testing requirements

- List/detail render; upload/edit/replace/delete call the correct endpoints and refresh state.
- AuthZ (server-driven): non-admin sees read-only (no create/edit/replace/delete); View-As is
  read-only; list(active) visible with generation authority.
- Author selection sends `canonicalReferenceId` (identifier only) and only allows `active` refs.
- Kill-switch OFF ⇒ UI entry hidden, endpoints 404, generate ignores any selection (unchanged).
- **UI-smoke asset** for the Operator (run with the flag ON in the target env): create a template,
  edit its label/text, deactivate it, confirm a deactivated ref is not author-selectable.

## Acceptance conditions

- [ ] Kill-switch (`PROJECTPULSE_MODULE025_REFERENCE_SOURCES_ENABLED`, default OFF) gates the UI; OFF = today's behavior
- [ ] No backend route/contract changes; `module025-reference-source` v1 preserved (identifier-only client boundary)
- [ ] Create/edit/replace/delete are admin-only in the UI (server-driven flags); View-As read-only
- [ ] Author selection limited to `active` references; sends `canonicalReferenceId` only
- [ ] Canonical library UI is distinct from the immutable `TemplateCatalog` candidates view
- [ ] No exporter or output-format change (does not touch `module025-output-standards`)
- [ ] UI-smoke asset authored and passes with the flag enabled
- [ ] Existing suite stays green; CI all-green

## Pipeline test: NO
