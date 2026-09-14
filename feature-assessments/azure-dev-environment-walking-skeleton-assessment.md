# Feature assessment: Azure DEV environment — walking skeleton (Mode A intake)

- **Date:** 2026-09-14
- **Source:** ADRs 0001-0003 (`docs/adr/`), Ryan's design decisions from 2026-09-09
- **Decision:** ACCEPT, split into 4 dependency-ordered stages (stages 1-4)

## Request

Stand up a scale-to-zero, single-region DEV environment for Pulse in Azure, per the
architecture locked in ADRs 0001-0003, and produce a bootable "walking skeleton"
before any feature-level DEV work begins.

## Claim/reality verification (mandatory anti-hallucination check)

Two research passes against the live `project-time-platform` repo (not docs/memory)
confirmed or corrected every load-bearing assumption before stages were written:

| Claim | Verified? | Detail |
|---|---|---|
| API container listens on 5080 | Confirmed | `deployment/containers/api/Dockerfile:41,45` |
| Web container listens on 8080 | Confirmed | `deployment/containers/web/Dockerfile:215`, `default.conf.template:7` |
| `GET /health` exists, no DB dependency | Confirmed | `Program.cs:281-286` |
| DB config is discrete env vars, not a connection string | Confirmed | `PTP_DB_HOST/PORT/NAME/USER/PASSWORD`, `Program.cs:2055-2072` |
| `az00c-*` is enforced budget guardrail tooling | **Corrected** | Read-only cost report only; real enforcement is `az00d-create-test-subscription-budget.sh`, not previously referenced — ADR 0002 updated |
| Existing Azure IaC can be parameterized down to DEV shape | **Corrected** | No IaC exists (Bicep/Terraform) — `az01`-`az12a*` are ~80 imperative scripts hardcoded to two-region HA prod; DEV needs new scripts, not a config toggle |
| A unified migration runner exists to apply all schema changes | **Corrected** | No such runner exists; migrations are applied one-by-one via bespoke per-migration scripts. Stage 3 must build this from scratch |
| `IProjectPulseAiProvider` covers the private/Ollama AI path | **Corrected** | That interface only covers Claude/OpenAI *external* providers; the private target (`PulseAiPrivateModelClient`) is a separate, already-pluggable-via-env-vars seam — captured in `contracts/ai-provider-interface.md` |

The two corrected IaC/migration-runner findings directly shaped stage scope (stages
1 and 3 build new tooling, not thin wrappers) — this is exactly the kind of false
premise this verification step exists to catch before planning commits to it.

## Why 4 stages, in this order

- **Stage 1** (infra) has no dependencies and unblocks everything else — provisions
  the compute/DB/registry shell with nothing running on it yet.
- **Stages 2 and 3** (API-on-ACA-with-health-probe, and the migration runner) both
  depend only on Stage 1 and are independent of each other — they can build/review
  in parallel.
- **Stage 4** depends on both 2 and 3: it needs a running API app *and* a migrated
  database before the walking skeleton can be considered complete, and it's the
  stage that actually proves the end-to-end slice (API + web + DB, real page render,
  real DB-backed API call) rather than just infrastructure existing in isolation.

## What's explicitly deferred (not planned this run — Mode A bounds the batch)

- **The AI runtime** (Foundry or Ollama) — ADR 0003 defers this past the walking
  skeleton by design; no stage here touches `contracts/ai-provider-interface.md`
  beyond referencing it.
- **OCR / malware-scanning provider choice** — flagged as open in ADR 0003, not
  planned.
- **CI automation of these deploys** — ADR 0002 chose interactive `az login`; no
  stage writes `.github/workflows/` (containment-protected path per ADR-0011).
  Automating this is a future decision, not blocked by anything here.
- **Any further Module 025 / feature-level work on this environment** — out of
  scope until the walking skeleton (stages 1-4) is green; re-plan via Mode B
  intake once it lands.
