# Module 064 Capability Matrix

| AI-017 acceptance area | Source state | Evidence / boundary |
|---|---|---|
| Non-secret configuration UI | Implemented | Admin route shows provider, model, endpoint/API version, org/project, limits, feature routes, environment, and health |
| Claude-only / OpenAI-only / both | Implemented | Four explicit modes plus per-feature ordered routes |
| No accidental duplicate calls | Implemented | Sequential router stops after success; routes are de-duplicated |
| Claude → OpenAI → local fallback | Implemented default | Shared configuration and router |
| Availability check and skip | Implemented | Cached health, background probe, failure threshold, circuit |
| Safety refusal does not fail over | Implemented | Refusal is a terminal normalized outcome |
| Approved models | Implemented | Per-provider allowlist enforced before HTTP |
| Write-only API keys | Read-only runtime boundary implemented | Values are never serialized; write UI/API intentionally absent |
| Versioned encrypted secret storage | Blocked | Requires authorized Key Vault or approved secure-store integration |
| Rotation / expiry / version / fingerprint | Metadata read implemented | Rotation writes require secure-store authorization |
| Step-up authentication for updates | Blocked | No update endpoint exists; Entra/auth design requires authorization |
| Connectivity/model validation | Implemented for runtime references | Background and manual model-access probes |
| Explicit activation and rollback | Blocked | Requires authorized persistent configuration versions |
| Immutable sanitized audit | Blocked | Requires authorized persistence/database design |
| Last success/failure and usage | Implemented | In-memory health registry and admin API/UI |
| Provider limits | Implemented configuration and runtime view | Timeout, retry, output-token, health, circuit, and provider-reported remaining request/token limits |
| SOW/GSD routing | Reserved | `sow_gsd_planning` feature route; no live caller currently exists |
| Timesheet routing | Implemented | Existing suggestion service migrated to Module 064 |
| Help routing | Reserved | `help_assistant` feature route |
| Closeout routing | Reserved | `closeout_communication` feature route |
| Project FlowHive routing | Reserved | `project_flowhive_plan` feature route; Module 066 unchanged |

The three blocked areas are not simulated or marked complete. Their source
contracts can be added only after explicit Azure/database/Entra authorization.
