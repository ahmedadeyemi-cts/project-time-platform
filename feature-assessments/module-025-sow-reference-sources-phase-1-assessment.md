# Feature assessment: Module 025 SOW reference sources (Phase 1)

- **Decision:** ACCEPT as Stage 1 (feature).
- **Date:** 2026-09-24
- **Stage:** `stage-instructions/stage-1-module-025-sow-reference-sources-phase-1.md`
- **Contract:** `contracts/module025-reference-source.md`
- **Security:** `docs/security-invariants.md` (Phase 1 section); ADR `docs/adr/0001`

## Request

Add an optional author-selected reference source injected as a **co-equal authoritative
citation** into Module 025 SOW generation.

## Scope (refined with the author)

- **Phase 1 = canonical templates only** — the Sell Templates (no customer info),
  **admin-uploaded and editable** in-app; injected as citation 2.
- **Deferred:** the prior-version (same-engagement, customer scope) kind; live SharePoint
  reads of Sell Templates / Client Files. These wait for the "app works with customer data"
  phase.
- **Out of scope (Phase 2):** document font/visual fidelity (exporter/template work).

## Claim / reality verification (against live source)

| Assumption | Reality |
|---|---|
| Single-chunk authoritative source mechanism exists | ✅ `CreateModule025AuthoritativeScopeSource` (`PulseAiPrivateRagService.cs:1540`) |
| Retrieval returns one chunk; prompt = "citation 1 as authority" | ✅ `Module025AuthoritativeScopeRetrieval` (:1587); question (:483) |
| Same-customer boundary enforced | ✅ ProjectCode/ProjectName guard (:444) |
| `/generate` bodyless; queues via evidence_json | ✅ `Module025SowGsdModule.cs:425`,:513 |
| Worker re-loads at execution | ✅ `ExecuteGenerationAsync` (:700), evidence (:820) |
| **No live SharePoint/Graph Files integration** | ✅ Graph used for mail/Teams only (`MicrosoftMailRuntimeConfigurationModule`); no Sites/Drives reads |
| `.docx` text extraction exists | ✅ `PulseAiPrivateDocumentExtractionService.ExtractDocx` (:403) |
| Doc font/format is exporter-driven (not LLM) | ✅ `Module025SowGsdDocumentExporter.StylesXml()` |
| No canonical store exists | ✅ new additive table required |

## Why ACCEPT (and scope decisions)

- Reuses the guarded authoritative-scope mechanism + the proven docx extractor — smallest
  safe change.
- **Templates-only** keeps Phase 1 free of customer data and free of any external
  integration, matching the author's caution.
- **Admin upload + edit** (not a one-time script) because the author needs to maintain
  templates over time; no live SharePoint because that integration doesn't exist and would
  balloon the stage.
- Neutralization kept as a **defensive** guard (templates should already be identity-free).

## Alternatives considered

- Live SharePoint read now — rejected: large net-new integration; pulls the customer folder
  in too early.
- Include prior-version now — rejected: customer scope, against stated caution.
- One-time import script — rejected: no later editing; author wants to edit templates.
- Raw canonical injection (no neutralization) — rejected: removes the defensive belt.

## Out of scope (later phases)

- Prior-version (same-engagement) references + Client Files, with same-customer guardrails
  (pre-noted in the invariants file).
- Live SharePoint browsing of Sell Templates / Client Files (its own trust-boundary review).
- Document font/style/layout fidelity (Phase 2; `Module025SowGsdDocumentExporter`).
- Front-end UI for authoring/selecting references and for admin template management (this
  stage exposes the APIs + persistence).
