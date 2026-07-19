# Module 064 API Contract

All Module 064 endpoints require a valid ProjectPulse session. The configuration
and health endpoints also verify the actual signed-in user has an active
`SUPER_ADMINISTRATOR`, `SYSTEM_ADMINISTRATOR`, or `ADMINISTRATOR` role. View-As
does not transfer administrator authority to the effective user.

## `GET /api/ai-configuration`

Returns the complete sanitized configuration view:

- environment and routing mode;
- timeout, retry, token, health, and circuit limits;
- enabled/configured flags;
- provider model, endpoint, API version, allowlist, organization, and project;
- secret source/version/rotation/expiry/fingerprint metadata;
- de-duplicated route per AI feature;
- cached provider health and usage;
- locked lifecycle/governance state.

API keys are never serialized. The `secret.valueReturned` and
`secretLifecycle.apiKeysReturned` fields are always `false`.

## `GET /api/ai-configuration/health`

Returns cached provider state without initiating a remote request. Each provider
includes:

- `enabled`, `configured`, and `status`;
- last check, success, and failure timestamps;
- sanitized last-failure code;
- circuit-open deadline;
- success, failure, and refusal counts;
- cumulative input/output token counts;
- provider-reported remaining request/token limits and reset values when present;
- last provider request identifier when supplied by the provider.

Overall status is `healthy`, `degraded`, or `local_fallback_only`.

## `POST /api/ai-configuration/health/refresh`

Runs a model-access health check against each enabled and configured remote
provider. Disabled and unconfigured providers are never contacted. This action
does not change provider configuration, Azure, the database, or Entra.

## Existing consumer contract

`POST /api/timesheets/ai-description-suggestions` remains the existing
timesheet-facing contract. Its request shape is unchanged. The response retains:

- `suggestion`
- `provider`
- `warning`
- `message`

Provider is now one of `claude`, `openai`, or `local_template`. A safety refusal
returns an empty suggestion, the refusing provider, and a warning that no fallback
provider was attempted.

## Provider adapter contracts

Claude uses the Messages endpoint and model-access endpoint. OpenAI uses the
Responses endpoint and model-access endpoint. Both adapters:

- enforce the approved-model list before an HTTP request;
- use the shared timeout/retry policy;
- classify safety refusals separately from availability failures;
- return normalized content, usage, request identifier, outcome, and sanitized
  failure code to the router;
- never expose raw error bodies or API keys to consumers.

## Error responses

| HTTP | Status | Meaning |
|---|---|---|
| 401 | `session_required` | No valid ProjectPulse session |
| 403 | `access_denied` | Actual user is not an administrator |
| 503 | `configuration_unavailable` | Database connection is unavailable for role verification |
| 503 | `authorization_unavailable` | Administrator authority could not be safely verified |

There is intentionally no configuration mutation, secret-write, activation,
rollback, or delete endpoint in this authorization phase.
