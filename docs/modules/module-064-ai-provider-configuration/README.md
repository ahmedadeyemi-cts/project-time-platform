# Module 064 — AI Provider Configuration Center

## Purpose

Module 064 is the single ProjectPulse boundary for AI provider configuration,
availability, routing, usage visibility, and safe fallback. Existing and future
AI consumers must call the shared router instead of constructing provider clients,
reading API keys, or selecting models independently.

The default governed route is:

1. Claude, when enabled, configured, approved, and available;
2. OpenAI, when Claude is unavailable and OpenAI is enabled, configured,
   approved, and available;
3. the deterministic local template when no remote provider is available.

A provider safety or content-policy refusal stops routing. It is not treated as
an availability failure and never causes a request to be sent to another provider.

## Source checkpoint

| Field | Value |
|---|---|
| Module | `064` |
| Tracker requirement | `AI-017` |
| Workspace branch | `feature/modules-064-074-release-train-on-main-20260719` |
| Implementation base | `main@2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4` |
| Current-main compatibility target | `main@2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4` |
| Source phase | Full shared runtime and read-only administration center |
| Commit/push/deployment | Not performed; separate authorization required |
| Azure/database/Entra changes | None |

## Implemented source behavior

- One environment-backed, sanitized configuration model for Claude, OpenAI,
  local fallback, timeouts, retries, output limits, model allowlists, provider
  priority, and feature routes.
- Claude Messages API and OpenAI Responses API adapters behind one provider
  interface and one shared HTTP-client factory.
- Cached provider health state, periodic health probes, bounded retries,
  consecutive-failure tracking, and a circuit that skips a provider while it is
  known unavailable.
- Sequential, de-duplicated routing. A single logical request is never sent to
  both providers after one succeeds.
- Safety-refusal handling that does not fail over.
- Sanitized error handling. Provider response bodies, API keys, and exception
  messages are not returned to the browser.
- Administrator-only read APIs and an administration route showing non-secret
  provider configuration, model/endpoint metadata, health, last success/failure,
  circuit state, usage totals, provider-reported remaining request/token limits,
  feature routes, and secret version metadata.
- Migration of the existing timesheet-description generator from a direct Claude
  call to the Module 064 router.
- Shared API Status Dashboard integration.

The current repository contained only one live remote-provider caller before
Module 064: the timesheet description suggestion service. Module 025 and Module
028 contain workflow/readiness surfaces but no separate live provider client.
Future SOW/GSD, help, closeout, and Project FlowHive AI behavior must use the
feature codes already reserved by this module.

## Configuration modes

`PROJECTPULSE_AI_MODE` supports:

| Mode | Governed route |
|---|---|
| `priority_failover` | Claude → OpenAI → local |
| `claude_only` | Claude → local |
| `openai_only` | OpenAI → local |
| `local_only` | Local only |

Each feature may override the order with a comma-separated
`PROJECTPULSE_AI_ROUTE_<FEATURE>` value. Invalid and duplicate provider codes are
discarded, and the local fallback is appended once when absent.

Reserved feature codes:

- `timesheet_description`
- `sow_gsd_planning`
- `help_assistant`
- `closeout_communication`
- `project_flowhive_plan`

## Availability rules

A remote provider is contacted only when all of the following are true:

- it is enabled;
- an API key reference is configured;
- the selected model is in that provider's approved-model list;
- its circuit is not open;
- it is included in the selected feature route.

The background monitor performs cached model-access health checks. Transient
timeouts, rate limits, and service failures use bounded retries. Repeated failures
open the circuit for the configured interval so normal requests do not repeatedly
contact a provider that is known to be unavailable. An administrator may request
an explicit health refresh from the Module 064 center.

## Secure-secret and persistence boundary

The administration center is read-only. It never returns an API key and has no
endpoint to create, update, rotate, activate, roll back, or delete provider
configuration.

Tracker acceptance for write-only Key Vault secret entry, versioned encrypted
storage, step-up authentication, explicit activation, rollback, and immutable
sanitized audit cannot be truthfully activated under the current authorization:

- Azure changes: not authorized;
- database changes: not authorized;
- Entra changes: not authorized.

Those controls are visibly locked in the UI and API response. A later authorized
phase must add the secure-store adapter and persistence/audit design without
changing the router contract or exposing secret values.

## Files owned by Module 064

- `src/backend/ProjectTime.Api/Ai/ProjectPulseAiContracts.cs`
- `src/backend/ProjectTime.Api/Ai/ProjectPulseAiConfiguration.cs`
- `src/backend/ProjectTime.Api/Ai/ProjectPulseAiHealthRegistry.cs`
- `src/backend/ProjectTime.Api/Ai/ProjectPulseAiRemoteProviders.cs`
- `src/backend/ProjectTime.Api/Ai/ProjectPulseAiRouter.cs`
- `src/backend/ProjectTime.Api/Ai/ProjectPulseAiHealthMonitor.cs`
- `src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs`
- `src/backend/ProjectTime.Api/Modules/AiProviderConfigurationModule.cs`
- `src/frontend/project-time-web/src/AiProviderConfigurationCenter.jsx`
- `src/frontend/project-time-web/src/ai-provider-configuration-center.css`
- `src/frontend/project-time-web/scripts/validate-module-064-shared-ai.mjs`
- this documentation directory.

Shared-file edits are limited to DI/endpoint registration and consumer migration
in `Program.cs`, route/registry/mount/provider-label additions in `App.jsx`, the
Module 064 build guard in `package.json`, the build-stage-only validator context
in `deployment/containers/web/Dockerfile`, and central governance records. The
runtime web image still receives compiled frontend assets only.

## Protected behavior

- Module 001 timesheet save, submit, locking, and edit behavior is unchanged.
- Module 002 Approval Center files and workflows are untouched.
- Module 056E remains validated.
- Module 059 remains mounted once after authenticated route content.
- Module 062 remains the identity/profile authority.
- Module 066A remains preserved; Module 064 does not register or mutate Module
  066 behavior.
- No database migration, Azure/Entra artifact, deployment file, or PDF/Excel
  artifact is introduced.

## Local validation evidence

- Module 064 validator: 43/43 passed.
- .NET 10 Release build: passed with 0 errors and 0 Module 064 warnings.
- Production frontend build: passed.
- Module 059, Module 062, Module 066A (34/34), and Module 056E preservation
  validators: passed.
- Production-mode startup: passed without provider secrets.
- Route protection smoke test: health 200; unauthenticated Module 064 and
  timesheet AI endpoints 401.
- Router runtime harness: Claude stop, availability failover to OpenAI, terminal
  safety refusal, and local fallback all passed.
- Live provider connectivity was not attempted and is not asserted.
- GitHub comparison confirmed that `main` advanced from the implementation base
  to `2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4` through the consolidated
  release-train integration
  container Dockerfile. The final integration patch includes the additional
  Module 064 validator context and must be replayed on that exact current-main
  commit before any commit is authorized.
