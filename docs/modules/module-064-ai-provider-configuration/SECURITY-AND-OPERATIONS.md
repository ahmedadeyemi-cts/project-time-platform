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

Feature route variables are comma-separated provider codes, for example:

`PROJECTPULSE_AI_ROUTE_TIMESHEET_DESCRIPTION=claude,openai,local_template`

Allowed codes are `claude`, `openai`, and `local_template`. Values are
case-normalized and de-duplicated. Local fallback is appended once when absent.

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
