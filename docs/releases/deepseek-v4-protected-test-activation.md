# DeepSeek v4 — Protected Test activation

This change adds the user-authorized DGX model `deepseek-v4-flash-0731` at
`https://dgx-spark-lab01.taile0ffc4.ts.net/v1`. No key is included in source.

After deployment, an actual Module 064 administrator saves the key in the DeepSeek v4
provider card. The existing same-origin, encrypted, write-only credential endpoint
stores it. Verify the card reports available before testing generation.

Webpage key entry requires the existing database-managed runtime mode. Candidate
and active immutable AI release phases keep their HTTP 423 mutation lock; they
instead explicitly enable `PROJECTPULSE_AI_DEEPSEEK_ENABLED=true` and require
`PROJECTPULSE_DEEPSEEK_API_KEY` injected from the version-pinned
`PROJECTPULSE_DEEPSEEK_API_KEY_SECRET_REFERENCE`. DeepSeek defaults to disabled in
those immutable phases until that configuration is supplied. The safe configuration digest binds
the enabled flag and secret reference. Verify the target runtime's configuration
authority before claiming webpage activation is available; do not unset an immutable
release phase to bypass its controls.

The requested default is DeepSeek v4 → Celar AI → Claude → OpenAI → governed local
template. Safety refusals remain terminal; public vendors retain their existing
sanitization requirements, and evidence-dependent consumers retain their adoption
checks. Reasoning is never used as final answer text. Readiness requests 500 final-answer tokens; DeepSeek adds 2,048 tokens of reasoning
headroom to consumer budgets, with a 16,384-token total cap.

The DGX request slot is coordinated across API replicas with a PostgreSQL transaction
advisory lock. Waiting is bounded; missing credentials, queue contention, timeouts,
and invalid responses remain explicit provider failures.

## Unavailable Oracle dependencies

The user reports that the Celar VM was deleted. DGX inference does not restore
ClamAV malware scanning, Tesseract OCR, or Ollama embeddings. New documents requiring
those services must remain blocked or degraded, not marked safe/ready. No scanner,
OCR, embedding, evidence, or citation gate is disabled by this release.

## Validation after key entry

- Module 064: save key; confirm available; verify reload never returns the key.
- Ask AI: check provider attribution and an authorized internal project question.
- Module 025: generate from authorized structured scope and verify persisted phases.
- FlowHive/Forge: use already-admitted evidence and verify citations.
- Uploads: confirm missing scanning/OCR services remain visible and fail closed.

Authenticated DGX generation and full acceptance cannot be certified before key entry.
Production is outside this release.
