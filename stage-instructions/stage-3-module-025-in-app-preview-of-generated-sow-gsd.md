# Stage 3: Module 025 in-app preview of generated SOW/GSD

- **Type:** feature
- **Depends on:** 2
- **Contract:** consumes `contracts/module025-output-standards.md`

## Objectives

Let a user **see** the generated SOW/GSD in the app before downloading — today the
authoring workspace and register only offer download links, with no viewer. Read-only,
additive. Answers the "understand what the generated documents look like" ask by putting
the real deliverable on screen. The preview must render the **same artifact** the download
produces (no separate/second-source rendering that could disagree with the file).

## What to build

1. **Preview data source (backend).** Reuse the existing generation/export path; do NOT add a
   second rendering engine. Add read-only preview endpoints that return a
   **structured, safe representation** of the already-generated artifact (mirror the existing
   template-candidate preview approach in `Module025TemplateCatalog` /
   `GET .../{versionId}/preview`, which returns worksheet cells/formulas for xlsx and
   paragraphs/tables for docx):
   - `GET /api/module025/sow-gsd/{engagementId}/preview/sow` and `.../preview/gsd`
     (draft + confirmed, honoring current state gating), returning JSON the client renders.
   - Auth/scope identical to the existing download handlers (`LoadReadableStateAsync` →
     `AuthorizeViewAsync` / `ResolveAccessAsync` / `access.CanViewOwned`); drafts set
     `Cache-Control: no-store`; confirmed requires `status == "confirmed"`.
   - The preview JSON is derived from the same exporter output the download serves, so it
     reflects the `module025-output-standards` guarantees (letterhead/footer noted as present,
     phase sections in order, GSD sheet set, DRAFT marker surfaced).

2. **Preview UI (frontend, `src/frontend/project-time-web/src/module025/`).** Add a preview
   panel/modal in `SowGsdAuthoringWorkspace.jsx` next to the existing Download draft/confirmed
   buttons (and optionally in `SowRegister.jsx` for retained versions). Render the structured
   preview (SOW: sections/paragraphs/tables; GSD: sheet tabs → cell grid). Clearly badge
   DRAFT vs confirmed. Use the existing `requestJson`/`protected-download.js` session-header
   pattern; no new API-client framework.

3. **Kill-switch / dark-launch flag (default OFF)** gating both the preview endpoints and the
   UI entry points, following the module's established flag pattern
   (`Module025ReferenceSourcePolicy` env-var style). OFF ⇒ preview endpoints 404 and the UI
   shows no preview control; download behavior is byte-for-byte unchanged.

## Interface contracts

- **Exposes:** read-only `GET .../preview/sow|gsd` returning structured preview JSON; preview UI.
- **Consumes:** existing SOW/GSD generation/export state and download handlers in
  `Module025SowGsdModule`; the `module025-output-standards` guarantees (Stage 2); the existing
  auth/scope helpers and frontend session pattern.
- **Must not break:** existing download endpoints (`sow.docx`/`gsd.xlsx`/`draft-*`) and their
  state-gating/auth; the `module025-output-standards` contract (preview must not imply a format
  the file doesn't have).

## Testing requirements

- Preview endpoints enforce the same auth/scope/state gating as the matching download endpoints
  (owner-scope forbidden case; confirmation-required for confirmed preview; drafts `no-store`).
- Preview content agrees with the exported artifact for a fixture engagement: same phase order,
  same GSD sheet set, DRAFT badge present iff the artifact is a draft (cross-check against the
  Stage 2 golden samples).
- Kill-switch OFF ⇒ endpoints 404 and no UI control; download path unchanged.
- **UI-smoke asset** for the Operator: open an engagement, click Preview SOW and Preview GSD,
  confirm the rendered content and DRAFT badge; confirm download still works.

## Acceptance conditions

- [ ] Kill-switch / dark-launch flag (default OFF) gates preview endpoints + UI; OFF = today's behavior
- [ ] Preview is derived from the same exporter output as download (no divergent second renderer)
- [ ] Preview endpoints match download auth/scope/state-gating exactly
- [ ] SOW preview shows phases in order with sections; GSD preview shows the standard sheet set
- [ ] DRAFT vs confirmed clearly indicated; drafts are `no-store`
- [ ] UI-smoke asset authored and passes post-deploy
- [ ] Existing suite stays green; CI all-green

## Pipeline test: NO
