# Module 064 provider authority and Gemini model discovery

This change addresses a mismatch between saved AI priorities and SOW execution,
Gemini's missing structured SOW adapter, a fixed Gemini model list, and unclear
quota failures. Module 064 remains the administrator control surface. No live
provider settings, paid approvals, credentials, or deployment policy are changed
by this PR.

## Operator workflow

1. Open Module 064. Gemini automatically loads compatible text-generation models
   from Google's paginated model inventory using its saved server-side key.
   Refresh models updates the inventory without changing the active model.
2. Compare the linked official Google pricing. Inventory metadata contains token
   limits and supported methods, not prices or remaining generation quota.
3. Select a model and choose **Save and test**. Pulse tests an isolated candidate;
   it persists and activates the choice only after inference succeeds. A failed
   test preserves the active model. Saved discovered models survive API restarts.
   Model selection requires a key saved in Module 064 so verification and the
   database update can be tied to the same credential version across API replicas.
   Concurrent key or settings changes return a conflict and require a fresh test.
4. In SOW/GSD planning, review the saved provider order and effective execution
   policy. Explicitly approve sanitized external phase generation if desired and
   save the route. Approval is persisted, revision-checked and audited. Deployment
   privacy restrictions remain visible and enforced.
5. Generate a representative supported SOW scope and verify each phase, hours,
   descriptions and SOW/GSD exports before accepting provider quality or latency.

## Routing and privacy

An approved SOW route follows Module 064's saved order when the server can build
and validate a closed technical capsule. Raw customer scope, private documents,
identities and commercial values do not leave Pulse. Unsupported scopes retain
private routing; a local template is never reported as a successful AI phase.
Gemini now requests structured phase output with the same server validation as
other SOW providers and uses a bounded phase budget and low thinking effort where
supported. No request is silently retried by its provider adapter.

The existing deployment privacy flags still govern external requests:
`PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION` and
`PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED`. Module 064 displays
these blockers. Ordinary saved SOW routes use their audited approval rather than
the hidden legacy paid-fallback flag. Release-managed qualification retains its
existing frozen configuration and paid-provider boundary.

FlowHive's detailed, source-cited WBS still uses its private inference path;
external providers currently supply generic guidance. Module 064 explicitly
shows this limitation and effective order. This PR does not claim Gemini now
generates the authoritative FlowHive WBS.

## Gemini degraded status

The supplied screenshot reports zero successful generations and nine failed
readiness probes, with `gemini_http_429`. Google returned a rate/quota response;
that alone does not distinguish minute limits, daily allowance, or billing-tier
quota. Check the API project's limits in Google AI Studio. The existence of a
saved key or a discoverable model does not prove inference quota is available.

Pulse exposes safe error categories and retry times, honors Retry-After and
Google RetryInfo, and prevents forced/background probes from retrying inside the
cooldown. Model discovery does not reset inference health. An older successful
request cannot clear a newer quota cooldown. Provider response bodies and API
keys are never returned in diagnostics.

## Installation and verification

Apply `123_module064_external_generation_approval.sql` before deploying the API.
The new approval defaults to false; existing routes are not implicitly approved.
The corresponding rollback removes the migration ledger entry while retaining
approval and audit columns for history. The protected-Test migration job installs
and verifies migration 123 before the API update. This change does not grant new
deployment authority.

Focused tests cover routing/privacy boundaries, approval persistence, Gemini
structured output and refusals, quota backoff, paginated model discovery, cache
invalidation, safe model activation, endpoint authorization and the browser UI.
Mocked provider tests cannot establish actual account quota or production latency.

Primary API references:

- https://ai.google.dev/api/models
- https://ai.google.dev/gemini-api/docs/openai
- https://ai.google.dev/gemini-api/docs/troubleshooting
- https://ai.google.dev/gemini-api/docs/pricing
