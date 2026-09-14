# 0003. Pluggable AI provider: Azure AI Foundry default for DEV, private Ollama runtime deferred

- **Status:** Accepted (AI runtime stand-up itself deferred — see Decision)
- **Date:** 2026-09-14

## Context

Pulse's FlowHive/SOW modules use an AI runtime (Ollama for LLM inference, Tesseract
for OCR, ClamAV for malware scanning), documented in
`deployment/podman/README.md` under a **fail-closed evidence gate**: "Raw SOW
content never goes to Claude or OpenAI." That's a real, load-bearing contract with
the business, not incidental config. Standing up self-hosted Ollama/Tesseract/ClamAV
in DEV adds meaningful compute cost and operational complexity that doesn't serve
the DEV environment's actual purpose (fast, cheap, disposable iteration).

Azure AI Foundry (gpt-4o-mini: $0.15/1M input tokens, $0.66/1M output;
text-embedding-3-small: $0.02/1M tokens — Azure Retail Prices API, westus3, PAYG,
pulled 2026-09-09) would cost under $5/mo at DEV volume and is inherently
scale-to-zero, fitting ADR 0001's cost target far better than running Ollama
continuously. But Foundry runs OpenAI models — even in-tenant, private-endpoint-able,
no-retention-configurable, it still reverses the documented "never goes to
Claude or OpenAI" contract, which makes this an architecture decision requiring
Ahmed's sign-off before it can flow anywhere near production, not a free swap.

## Decision

- **Defer standing up any AI runtime in the initial DEV walking skeleton.** The
  walking skeleton (see hand-off to `/verity:plan`) proves the API + web + DB spine
  end-to-end without FlowHive/SOW's AI path.
- **Design the AI provider seam as pluggable from the start** (frozen contract,
  see `contracts/ai-provider-interface.md`), so swapping providers is a
  configuration change behind the contract, not a rewrite.
- **When AI is needed in DEV, default to Azure AI Foundry** (gpt-4o-mini +
  text-embedding-3-small) for cost and scale-to-zero fit.
- **The private Ollama runtime remains the production/privacy-locked option** and
  is what any Foundry-based DEV work must be validated against before proposing
  the same change upstream.
- **Any change that would apply Foundry to the *production* or privacy-committed
  runtime (not just DEV) requires Ahmed's explicit buy-in first** — this ADR
  authorizes Foundry for the DEV environment only.
- **Out of scope, needs its own decision later:** OCR (Tesseract → candidate:
  Azure AI Document Intelligence) and malware scanning (ClamAV → candidate:
  Microsoft Defender for Storage) aren't replaced by Foundry and don't yet have a
  DEV-environment home. Deferred, not decided.

## Alternatives considered

- **Run the real Ollama/Tesseract/ClamAV stack in DEV** — rejected for the initial
  stand-up: highest fidelity to production, but adds always-on compute cost
  (working against ADR 0001's scale-to-zero goal) and operational burden for an
  environment whose job is fast iteration, not fidelity. Not ruled out permanently —
  can be added later as an explicit feature stage if DEV needs to validate the real
  runtime.
- **Skip AI entirely, no seam at all** — rejected: FlowHive/SOW is a real part of
  the app; punting the interface question to whenever AI work actually starts
  would risk it getting wired directly to Foundry with no seam, making the eventual
  Ollama path a rewrite instead of a swap.
- **Default to Foundry for production too, right now** — rejected: crosses the
  documented no-OpenAI evidence-gate contract without the accountable owner's
  (Ahmed's) sign-off. Explicitly out of scope for this ADR.

## Consequences

- **Easier:** DEV can exercise AI-dependent features cheaply (<$5/mo) and
  scale-to-zero, without waiting on a decision that isn't Ryan's to make alone.
  The frozen provider-interface contract means the eventual Ollama-vs-Foundry
  decision for production doesn't force a code rewrite, just a configuration and
  sign-off change.
- **Harder:** DEV testing against Foundry will not catch behavior differences
  specific to the self-hosted Ollama models (latency, output formatting, model
  version drift) — anything AI-output-sensitive still needs a pass against the real
  runtime before shipping to production. OCR and malware-scanning provider choices
  remain open questions that block full AI-path parity in DEV until decided.
