# Contract: ai-provider-interface

- **Status:** frozen v1 (documents an existing production seam — not new code)
- **Owner:** `PulseAiPrivateModelClient` (`src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs`),
  invoked as the `celar_ai` / "private target" slot inside `CelarAiCapabilityRouting`
  (`CelarAiCapabilityRouting.cs`)

## Context

This is **not a new abstraction** — it's the existing "private target" seam that
`CelarAiCapabilityRouting` already calls for FlowHive/SOW (Module 025) work, called
out separately from `IProjectPulseAiProvider` (which only covers the Claude/OpenAI
*external* providers in `ProjectPulseAiRemoteProviders.cs`). Freezing it here means:
for the DEV environment (ADR 0003), swapping the private target from self-hosted
Ollama to Azure AI Foundry is a **configuration change against this same wire
contract**, not new application code — as long as whatever sits behind the endpoint
speaks this shape.

## Exposes

An HTTP endpoint (whatever `PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT` points at)
that accepts OpenAI-chat-completions-style POST requests and returns a JSON
completion. `PulseAiPrivateModelClient` calls it via `IHttpClientFactory` client
name `"PulseAiPrivateInference"`.

## Consumes

Configuration, via environment variables (no `appsettings.json` keys) — see
`PulseAiPrivateRagContracts.cs:118-134`:
- `PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT` — base URL of the inference target
- `PROJECTPULSE_PRIVATE_INFERENCE_MODEL` — model name/deployment to request
- `PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN` — bearer auth token
- `PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED`, `PROJECTPULSE_PULSE_AI_RAG_REQUIRE_PRIVATE_MODEL` —
  RAG-path toggles that also govern whether this target is required before any
  external provider may be tried

Default capability routing order is `[celar_ai, claude, openai, local_template]`
(`CelarAiCapabilityRouting.cs:14-22`), overridable per-capability via the Module 064
admin store.

## Schema / wire

Request (`PulseAiPrivateModelClient.cs:73-83`):
```json
{
  "model": "<PROJECTPULSE_PRIVATE_INFERENCE_MODEL>",
  "messages": [{"role": "system|user|assistant", "content": "..."}],
  "temperature": 0.0,
  "max_tokens": 0,
  "response_format": {"type": "json_object"}
}
```
Auth: `Authorization: Bearer <PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN>`.
Response: standard OpenAI chat-completions JSON envelope (choices[].message.content).

## Governance constraint (not code-enforced — read before pointing this at a new target)

The fail-closed gate in `CelarAiCapabilityRouting.PrepareExternalRequest` (~line
2894) blocks the **external** Claude/OpenAI provider path when
`ContainsPrivateDocuments` is true (true unconditionally for SOW-GSD —
`CelarAiEnterprisePlatformService.cs:91`). It does **not** inspect what is actually
running behind *this* private-target endpoint. Pointing
`PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT` at Azure AI Foundry (an OpenAI-family
model) passes this code gate while still sending raw SOW content to an
OpenAI-family model — satisfying the letter of the routing code but not the
documented promise in `deployment/podman/README.md` ("raw SOW content never goes to
Claude or OpenAI"). This is exactly why ADR 0003 scopes Foundry to the **DEV**
environment only and requires Ahmed's explicit sign-off before this endpoint is
ever pointed at Foundry for production or any environment holding real SOW content.

## Versioning

Frozen at **v1**, matching the wire shape already in production. Changes are
**additive only** — a breaking change (e.g. a different request/response envelope)
is a NEW contract, not an edit (framework-spec §4.3). Every consumer (the
`celar_ai` routing target) depends on this shape.
