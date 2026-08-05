# Module 064 Security and Operations

## Secret handling

Provider secrets may be supplied through runtime environment references or entered
by an administrator in Module 064. Web-entered secrets are encrypted with
AES-256-GCM before database storage and become active immediately. Module 064 does
not write secrets to source, browser storage, logs, responses, or audit records.

The encrypted store requires `PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY`, a
base64-encoded 32-byte key supplied by the hosting platform. If it is absent or
invalid, the web write endpoint fails closed while environment-backed providers
continue to work. The encryption key must be backed up separately; changing or
losing it makes stored provider keys unreadable.

Accepted compatibility names:

| Provider | Preferred variable | Compatibility variable |
|---|---|---|
| Claude | `PROJECTPULSE_CLAUDE_API_KEY` | `ANTHROPIC_API_KEY` |
| OpenAI | `PROJECTPULSE_OPENAI_API_KEY` | `OPENAI_API_KEY` |

The center displays only configured/not-configured state and a short SHA-256
fingerprint for operator comparison. A fingerprint is not an API key and cannot
be used to authenticate.

Only active ProjectPulse administrators may replace a key. The endpoint accepts
same-origin requests, limits the secret size, encrypts at the application boundary,
and records only provider, version, actor, action, and timestamp in audit. Key
values cannot be read back through the UI or API. Replacement is immediate;
rollback and key deletion are not exposed.

Provider model and enabled state are stored separately from the encrypted key.
Administrators can change models only through the approved dropdown; activation
requires a successful provider check and a failed check restores the prior model.
Disabling a provider stops routing and remote probes without deleting its key or
model. Configuration is reloaded from the shared database on status reads and
synchronized across API replicas.

Same-origin mutation checks compare the browser origin with the public request
host. When the host does not explicitly contain a port, the check does not infer
the API container's internal HTTP port, which keeps HTTPS requests valid behind
Azure's reverse proxy without trusting client-supplied forwarding headers.

## Core runtime variables

| Variable | Default | Constraint |
|---|---|---|
| `PROJECTPULSE_AI_MODE` | `priority_failover` | `priority_failover`, `claude_only`, `openai_only`, `local_only` |
| `PROJECTPULSE_AI_TIMEOUT_SECONDS` | `30` | 5–180 |
| `PROJECTPULSE_AI_RETRY_COUNT` | `2` | 0–5 |
| `PROJECTPULSE_AI_MAX_OUTPUT_TOKENS` | `800` | 64–8192 |
| `PROJECTPULSE_AI_HEALTH_INTERVAL_SECONDS` | `120` | 30–3600 |
| `PROJECTPULSE_AI_FAILURE_THRESHOLD` | `3` | 1–10 |
| `PROJECTPULSE_AI_CIRCUIT_BREAK_SECONDS` | `180` | 30–3600 |
| `PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION` | `false` | Explicitly authorizes eligible deidentified Claude/OpenAI requests |
| `PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED` | `false` | Enables the separate Celar enterprise generic-reasoning fallback |

Provider variables include:

- `PROJECTPULSE_AI_CLAUDE_ENABLED`
- `PROJECTPULSE_CLAUDE_MODEL`
- `PROJECTPULSE_CLAUDE_ENDPOINT`
- `PROJECTPULSE_CLAUDE_API_VERSION`
- `PROJECTPULSE_CLAUDE_APPROVED_MODELS`
- `PROJECTPULSE_AI_OPENAI_ENABLED`
- `PROJECTPULSE_OPENAI_MODEL`
- `PROJECTPULSE_OPENAI_ENDPOINT`
- `PROJECTPULSE_OPENAI_APPROVED_MODELS`
- `PROJECTPULSE_OPENAI_ORGANIZATION`
- `PROJECTPULSE_OPENAI_PROJECT`

Secret metadata may be supplied with
`PROJECTPULSE_<PROVIDER>_SECRET_SOURCE`, `_SECRET_VERSION`,
`_SECRET_ROTATED_AT`, and `_SECRET_EXPIRES_AT`.

## Feature routes

Current capability routes are stored in `ai_capability_routes` and managed by
Module 064. Every route contains four unique targets and keeps the governed local
template last:

`celar_ai,claude,openai,local_template`

Allowed codes are `celar_ai`, `claude`, `openai`, and `local_template`. The three
Timesheet capabilities—project task, service request, and non-project—have
independent persisted routes. Environment route variables remain compatibility
inputs for the legacy router; they are not the authority for consumers compiled
against `CelarAiCapabilityRouter`.

Celar AI is the only target permitted to receive authorized private SOW/GSD
context. If Claude or OpenAI is placed first, that provider receives only the
purpose-built, server-authored activity/domain categories derived from the
Engineer note plus a generic work classification. The raw note, customer name,
project/task names and codes, people names, dates, locations, and retrieved
documents are not copied into that capsule. Lowercase or unlabeled names cannot
leak through regex matching because no captured free-text token or substring is
sent. Provider order cannot override the private-document boundary.

Every public-provider call requires both a declared purpose-built capsule and at
least one safe derived fact. A missing/invalid customer-identity inventory,
uncertain residual identifier, credential, commercial marker, raw document,
people-record dataset, or financial value fails closed to the governed local
template with a per-target decision code. Claude/OpenAI output is revalidated
against the same identity inventory before it may be returned; failing output is
discarded without being shown or passed to the next workflow stage.

## Private Celar target and document runtime

The private target is a separately managed OpenAI-compatible endpoint. It must
be enabled and contain an approved private/allowlisted endpoint, exact
model/deployment name, stable authentication secret or approved workload
identity, and private-host allowlist. Saving a route that starts with `celar_ai`
does not configure this target.

SOW-grounded Timesheet responses additionally require:

- migrations 052, 053, and 061;
- `PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED=true`;
- a shared, private, writable `PROJECTPULSE_UPLOAD_ROOT` mount rather than
  revision-local `/tmp`;
- document-specific malware scanning, extraction worker, OCR where required,
  and private embeddings or an explicitly approved lexical-only completion path;
- an engineering-visible, Timesheet-context-eligible SOW processed to ready;
  and
- a successful private-target test returning `private_model_available`.

The AI configuration stores accept the API's `PTP_DB_*` secret references as
well as approved direct connection-string variables. The encryption key must be
stable across revisions.

## Failure and refusal policy

- Network errors, timeouts, rate limits, and service errors are availability
  failures and may route to the next available provider.
- A provider is skipped while disabled, unconfigured, or circuit-open.
- Repeated failures open the circuit; a cached background probe later closes it
  after a successful provider check.
- Safety or content-policy refusal is a successful provider interaction with a
  refused outcome. It does not increase the circuit failure count and never
  triggers another provider.
- Provider exceptions are logged server-side with the provider code. Exception
  messages and raw response bodies are not returned to users.

## Operational validation

Source validation must include:

1. Module 064 validator;
2. Module 059 global-shell validator;
3. Module 062 identity validator;
4. Module 056E contract-management guard;
5. .NET 10 Release build;
6. production frontend build;
7. review of all changed and untracked files before any explicit staging;
8. confirmation that no secret, database migration, Azure/Entra, or deployment
   artifact was introduced.

Live provider connectivity is an environment smoke test, not a source-build
requirement. It must be run only in an authorized environment with injected
secrets and must not print secret values.

For a Test smoke check, verify:

1. `GET /api/ai-configuration` reports both sanitized-execution policy flags.
2. `GET /api/ai-configuration/private-model` reports a persisted, enabled,
   private-endpoint-approved profile.
3. `POST /api/ai-configuration/private-model/test` returns
   `private_model_available`.
4. Claude and OpenAI show configured, enabled, and probe `available`.
5. All three Timesheet routes contain the intended four-target order.
6. A normal generation increments exactly one provider's generation counter and
   returns per-target decision codes; probe counts alone do not prove routing.
7. A SOW-backed project task uses only private Celar for document content, while
   a non-project task never attempts document grounding.
