# Feature assessment: Module 025 — SOW/GSD templates (load & edit) + generated-document fidelity

- **Decision:** SPLIT. Plan a **thin first slice** (Stages 2–4); defer the rest to incremental intake.
- **Date:** 2026-09-24
- **Mode:** B (single request into the existing Module 025 codebase).
- **Stages:** `stage-2-*` (output-standards baseline), `stage-3-*` (in-app preview),
  `stage-4-*` (SOW canonical-reference admin UI).
- **Contracts:** new `contracts/module025-output-standards.md` (v1); reuses frozen
  `contracts/module025-reference-source.md`.
- **Relates to:** ADR `docs/adr/0001` (Phase 1 trust boundary; font/visual fidelity = "Phase 2").

## Request

Continue Ahmed's SOW/GSD work: (1) load & edit **SOW** templates in the app, (2) **along with
GSD** templates, and (3) understand what the generated SOW/GSD documents look like to ensure
they match US Signal standards.

## Author clarification (this session)

- "Templates" = **both** the AI-content/citation templates *and* the output-format templates.
- "Understand what generated docs look like" = **both** an in-app preview *and* a written
  standards baseline with test enforcement.

Because "both/both" is a large eventual backlog, per ADR-0025 (thin initial backlog, not a
giant upfront plan) only the first buildable slice is planned now; the rest is re-planned via
Mode B intake once this slice merges against the now-real code.

## Claim / reality verification (against live source)

| Assumption in the request | Reality (verified) |
|---|---|
| SOW templates can be loaded/edited today | Partly. `module025_canonical_references` (migration 125, PR #1159) = text-only SOW *content* templates with full backend CRUD (upload/list/edit/replace/delete) — but **no frontend UI** and **dark-launched OFF** (`PROJECTPULSE_MODULE025_REFERENCE_SOURCES_ENABLED`). |
| GSD templates can be loaded/edited today | No editable GSD template drives output. `module025_template_candidates` (migration 117) can hold **binary SOW+GSD originals** with an upload/preview/download UI, but rows are **immutable** (DB trigger) and **"never consumed by live exporters"** (`awaiting_mapping`). |
| Templates shape the finished document's look | Not yet. Output format is code/asset-driven: SOW `.docx` = hand-built OpenXML (Arial 11pt body, US Signal letterhead + footer + logo); GSD `.xlsx` = ClosedXML filling the bundled 12-sheet `Module025StandardGsd.xlsx` (standard) or generated from scratch (HAEA). |
| No way to see generated docs | Downloads exist (draft + confirmed, state-gated, auth'd) in the authoring workspace and register; there is **no in-app preview/viewer** of the generated deliverable. |
| "Our standards" are captured somewhere checkable | No. Only one typography assertion exists (Arial + `000000` in `styles.xml`); there is no written output-standards spec or golden sample. |
| Font/visual fidelity was intended for later | ✅ ADR-0001 explicitly defers font/visual fidelity to "Phase 2 (exporter/template concern)." |

No false premises found; the request is coherent but spans two distinct existing subsystems
plus the exporter, which is why it must be split.

## Why SPLIT (and this ordering)

- **The output-format track is the risky one** — making uploaded templates actually drive the
  exporters reopens the trust boundary and can silently break "what a US Signal SOW looks like."
  You must be able to **see and lock** current output *before* reshaping it. So the first slice is
  inspection + a frozen standards baseline (Stages 2–3), then the low-risk SOW *content*-template
  UI over the already-built, already-contracted backend (Stage 4).
- **Stage 2 (standards baseline)** is interpretation-independent, pure-additive (docs + golden
  samples + tests, zero runtime change), and creates the `module025-output-standards` guardrail
  every later stage depends on. Lowest risk, highest leverage → first.
- **Stage 3 (in-app preview)** directly answers "see what they look like," reads existing exporter
  output (no second renderer), and is dark-launched.
- **Stage 4 (SOW canonical-reference UI)** delivers real "load & edit SOW templates in the app"
  by fronting existing backend CRUD — no exporter risk, reuses the frozen reference-source contract.

## Explicitly deferred to later Mode B intake (the rest of "both/both")

- **GSD content-reference equivalent** of the canonical-reference library (load & edit GSD content
  templates) — mirror the Stage-4 pattern with a GSD-scoped store/UI.
- **Output-format templates actually driving the exporters** — wire the currently-inert
  `module025_template_candidates` binaries into `Module025*Exporter` with validated input mappings,
  editing/versioning/activation, and document QA. This is architecture-affecting (trust boundary +
  fidelity): it will need its own ADR and likely a `module025-output-standards` **v2** contract
  when the letterhead/sheet-set/fonts intentionally change.
- **Editing (vs. re-upload) of binary template originals** — the candidates table is immutable by
  design; an editing model is a product decision to make when that track is planned.

## Contract & safety

- New additive contract `contracts/module025-output-standards.md` (v1) freezes the *observed*
  output format so later changes are deliberate (v2 + ADR + regenerated golden sample), never silent.
- Stages 3–4 are additive and dark-launched; no frozen contract is broken. Stage 4 preserves the
  `module025-reference-source` v1 boundary (client sends identifiers only; reference text stays
  server-side; writes stay admin-only).
- No stage writes `.github/workflows/` (containment-protected); Stage 2 extends the existing
  app-level `tests/Module025ExportTests` harness only.
