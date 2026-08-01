# PR #418 Extension — Celar AI and Module 064 Unified Routing

Repository: `ahmedadeyemi-cts/project-time-platform`

Target pull request: **#418 — Module 011: Add the Celar AI enterprise platform interface**

Target branch: `feature/celar-ai-enterprise-platform-interface-20260801`

## Architecture decision

Module 064 is the single AI control plane for Pulse. Celar AI appears in Module 064 as the **private orchestration target**, not as a public vendor provider or another public API-key form.

The managed execution targets are:

1. **Celar AI** — private orchestration, governed tools, private RAG, private inference, evidence verification, and private reassembly.
2. **Claude** — optional sanitized external reasoning.
3. **OpenAI** — optional sanitized external reasoning.
4. **Governed local template** — deterministic final fallback.

The default route for each AI capability is:

```text
Celar AI → Claude → OpenAI → Governed local template
```

Administrators can change the order of Celar AI, Claude, and OpenAI for a capability. The governed local template remains the required final fallback. Route order never weakens classification, authorization, DLP, refusal, or human-review controls.

## Capability catalog

| Capability code | Display name | Owning modules | External-context rule |
|---|---|---|---|
| `timesheet_non_project_description` | Timesheet — Non-project time | 001 | Only the user note, category, date, and non-project row metadata can be considered for sanitized external assistance. |
| `timesheet_project_task_description` | Timesheet — Project tasks | 001, 019 | Project records and document evidence stay private; external stages receive a generic sanitized problem only. |
| `timesheet_service_request_description` | Timesheet — Requests / Service Requests | 001, 019 | Request records, attachments, IQS, SOW, GSD, and governed email evidence stay private. |
| `sow_gsd_planning` | SOW / GSD planning | 011, 025 | Customer, commercial, design, contract, pricing, and document evidence stays private. |
| `project_flowhive_plan` | Project FlowHive plan, schedule, and diagram | 011, 066 | Project evidence stays private; external stages provide generic planning patterns only. |
| `closeout_communication` | Closeout communication | 011, 040, 055C | Completion and acceptance evidence stays private; external stages may assist only with generic structure or tone. |
| `help_assistant` | Celar AI Help, Search, and troubleshooting | 011, 999 | Source-controlled operating knowledge and authorized system tools run first. |

The historical `timesheet_description` code remains a compatibility alias. The server resolves it to the non-project, project-task, or service-request capability from the selected row context.

## Route editor

Each capability card exposes:

- Primary target
- Secondary target
- Tertiary target
- Final fallback
- Save and reset controls
- Revision and persistence state
- Owning-module badges
- Context-classification and external-policy labels

Validation rules:

- Exactly four unique targets are required.
- Governed local template remains final.
- Disabled, unconfigured, unhealthy, or circuit-open targets are skipped.
- A provider safety refusal terminates the route; a later target is not attempted.
- Restricted context is never sent directly to Claude or OpenAI.
- A route change cannot save or submit time, publish a SOW, baseline a plan, send closeout communication, alter financial data, change permissions, or deploy software.

## Private Celar AI model profile

Module 064 manages a write-only private inference profile containing:

- Enabled state
- OpenAI-compatible private endpoint
- Private model or deployment name
- Optional bearer token
- Private-host allowlist
- “Require private model for document answers” policy
- Revision, updater, and timestamp

The endpoint and bearer token are encrypted with the existing Module 064 AES-GCM secret boundary. GET responses return only configured flags, model name, fingerprints, allowlist count, revision, and timestamps. Endpoint, token, ciphertext, nonce, tag, provider API keys, prompts, and source documents are never returned.

The private endpoint must resolve to loopback, a private IP, or an approved private DNS host or suffix. A public or unapproved host is rejected.

## API surface

```text
GET  /api/ai-configuration/routes
PUT  /api/ai-configuration/routes/{featureCode}
POST /api/ai-configuration/routes/{featureCode}/reset
GET  /api/ai-configuration/consumers

GET  /api/ai-configuration/private-model
PUT  /api/ai-configuration/private-model/settings
PUT  /api/ai-configuration/private-model/secret
POST /api/ai-configuration/private-model/test

POST /api/project-flowhive/ai/generate
POST /api/sow-gsd-planning/ai/generate
POST /api/project-closeout/ai/communication
```

## Consumer wiring

### Timesheet — non-project

Celar AI uses the work date, category, row label, user note, and authorized role context. No project document is assumed. The result remains review-only and cannot save or submit time.

### Timesheet — project task

Celar AI resolves the authorized project and task, retrieves eligible SOW, GSD, IQS, architecture, design, order, quote, supporting, and approved email-derived evidence, and preserves citations and document versions. Public providers receive no raw project evidence.

### Timesheet — request / service request

Celar AI resolves request metadata, attachments, IQS, related project documents, and governed email artifacts through request- and project-authorized adapters. It distinguishes request number, function, and status from project task details.

### Project FlowHive

Celar AI generates a review-only WBS, dependencies, roles, assumptions, risks, milestones, high-level timeline, and diagram. The draft must pass through the deterministic Module 066 schedule engine before any baseline proposal. No resource assignment, customer-date commitment, persistence, or baseline occurs automatically.

### SOW / GSD planning

Celar AI prepares a private, non-binding draft with citations, scope, exclusions, deliverables, responsibilities, assumptions, dependencies, acceptance criteria, risks, and open questions. Commercial, legal, security, technical, and customer approval remain mandatory.

### Closeout communication

Celar AI prepares unsent internal-review and customer-ready drafts from authorized completion status, acceptance evidence, deliverables, outstanding items, risk, and handoff information. The owning closeout and mail modules retain final review and send authority.

## External assistance

```text
Authorized private evidence and governed Pulse tools
                         ↓
                 Private Celar AI
                         ↓
             Evidence/confidence gate
                         ↓
       Sufficient evidence? ────────────────┐
             │                              │
             │ no                           │ yes
             ↓                              │
      DLP sanitization                      │
             ↓                              │
       Claude or OpenAI                     │
             ↓                              │
       Private reassembly                    │
             └──────────────→ Private verification
                                      ↓
                       Detailed cited answer or draft
```

Public providers never receive raw SOW, GSD, IQS, email, customer, project, employee, contract, rate, financial, credential, endpoint, IP-address, or unrestricted tool content. Generic external output is untrusted until Celar AI privately reapplies and verifies it.

## Consumer assurance

Module 064 lists every registered AI consumer with:

- Owning module and entry point
- Configured route
- Central-router connection state
- Private-context compliance
- Direct-provider-free state
- Last exercised, successful, and failed timestamps
- Last target, outcome, and correlation ID

Build validation fails when a registered consumer creates a direct Claude or OpenAI client, reads provider keys, bypasses the central capability router, returns endpoint or token values, omits local fallback, duplicates targets, or enables raw restricted context for a public provider.

## Persistence

Migration 061 introduces:

```text
ai_capability_routes
ai_capability_route_audit
ai_private_model_profiles
ai_private_model_profile_audit
```

The migration is idempotent, seeds all seven default Celar-first capability routes, has a matching rollback, and is validated through apply, repeat, rollback, and reapply in CI. The migration source is included in PR #418 but is not applied to Test or Production by the source package.

## Acceptance criteria

1. Every new environment defaults to Celar AI → Claude → OpenAI → Governed local template.
2. An administrator can change one capability without changing another.
3. View-As cannot change routes or private-model settings.
4. Duplicate targets and non-local final fallback are rejected.
5. A private allowlisted endpoint is accepted; a public host is rejected.
6. Endpoint, token, API key, and encrypted values never appear in GET responses or logs.
7. Project-task and service-request Timesheet suggestions preserve authorization and private citations.
8. Non-project Timesheet suggestions do not require project documents.
9. FlowHive returns a review-only plan, timeline, and diagram for deterministic scheduling.
10. SOW output remains non-binding and unpublished.
11. Closeout output remains unsent.
12. Provider refusal stops routing.
13. Provider outage skips to the next healthy, policy-eligible target.
14. Module 064 shows every named consumer as centrally routed and direct-provider-free.

## Non-goals

PR #418 does not train or deploy a private model, apply migration 061 to an environment, expose private endpoint or token values, send raw internal documents externally, save or submit Timesheets, publish a SOW/GSD, baseline a FlowHive plan, send closeout communication, or change Azure, Entra, Test, Production, or provider credentials during source validation.
