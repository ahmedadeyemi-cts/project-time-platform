# Module 011 — Pulse AI

## Purpose

Pulse AI is the governed ProjectPulse control plane for creating and operating
custom AI capabilities. It will manage model projects, permission-aware
knowledge sources, immutable dataset versions, external training jobs,
evaluations, model registrations, approvals, and controlled promotion.

Module 011 does **not** replace Module 064. Module 064 remains the single shared
runtime boundary for provider configuration, encrypted secrets, provider
health, feature routing, usage visibility, circuit breaking, and safe fallback.
Pulse AI prepares and governs model lifecycle work; approved inference endpoints
must still be registered and routed through Module 064.

The current internal-first question-routing and deterministic live-data
resolver contract is documented in
`CELAR-AI-INTERNAL-DATA-INTELLIGENCE.md`.

## Source checkpoint

| Field | Value |
|---|---|
| Module | `011` |
| Current name | `Pulse AI` |
| Source branch | `feature/module-011-pulse-ai-foundation-20260728` |
| Exact implementation base | `main@ad9fa2c76f6aba8df9bbdd4ab6970dcb0748fbb2` |
| Route | `work-task-builder` compatibility route |
| Source phase | Read-only and browser-session-only foundation |
| Database migration | None |
| Training execution | None |
| Provider mutation | None |
| Azure or deployment change | None |

## Why the compatibility route remains

`App.jsx` historically mounts Module 011 through the `work-task-builder` route
and the `WorkTaskBuilderPanel.jsx` component. The retired Work Task Builder
workflow already transferred project creation and project/task management to
Modules 055D and 055C.

This first Pulse AI phase keeps the historical route as a compatibility mount
and replaces only the mounted component. That avoids an unnecessary edit to the
large shared `App.jsx` while another open source effort owns Module 064 runtime
files. The application and navigation display the current business name,
**Pulse AI**.

A later route-normalization phase may introduce a dedicated `pulse-ai` route
after shared-route ownership and backward-link behavior are separately reviewed.

## Implemented foundation

The source foundation provides:

- a branded Module 011 Pulse AI workspace;
- Overview, Knowledge & RAG, Datasets, Training, Evaluations, Model Registry,
  Deployments, and Governance workspaces;
- read-only, non-secret visibility into the existing Module 064 configuration
  endpoint when the signed-in user is authorized;
- graceful restricted-state handling when Module 064 details are unavailable;
- browser-memory-only model project drafting for workflow review;
- documented ProjectPulse feature targets already governed by Module 064;
- a future external-compute lifecycle for LoRA, QLoRA, supervised fine-tuning,
  and evaluated distillation;
- explicit evaluation and permission-isolation gates;
- separation-of-duties and capability concepts for future Modules 012/037
  integration;
- disabled training, registration, and deployment actions that make the locked
  source phase visible to operators.

## Deliberately locked behavior

This phase cannot:

- persist model projects, knowledge sources, datasets, jobs, model versions, or
  deployment records;
- upload or import training files;
- submit a training or fine-tuning job;
- contact an external GPU environment;
- create embeddings or a vector index;
- register a model artifact;
- add, replace, read, or delete a provider secret;
- change a Module 064 provider, model, enabled state, or feature route;
- create or modify Azure, Entra, Container App, database, or deployment
  resources;
- promote a model to development, test, or production.

## Target architecture

```text
Authorized ProjectPulse records and approved documents
                         |
                         v
Module 011 — Pulse AI lifecycle control plane
  - knowledge-source governance
  - immutable datasets and approvals
  - external training-job orchestration
  - evaluation and permission tests
  - model registry and audit evidence
  - controlled promotion requests
                         |
                         v
Approved external training or inference compute
                         |
                         v
Module 064 — provider health, secrets, routing, usage, and fallback
                         |
                         v
Authorized ProjectPulse feature consumers
```

The ProjectPulse backend remains the authorization authority. A model never
decides which records a user may access. Retrieval must occur after role,
module, project, and effective-user checks, and only authorized content may be
placed in a prompt.

## RAG and fine-tuning boundary

Changing business facts remain in live APIs or permission-aware retrieval:

- current role and permission assignments;
- project, customer, user, task, and time-entry records;
- open defects and operational status;
- deployments and release state;
- current policies and published module documentation.

Fine-tuning is reserved for stable behavior such as:

- ProjectPulse terminology;
- approved answer structure;
- tool-selection behavior;
- consistent explanations;
- required structured output;
- handling uncertainty and missing evidence.

## Planned lifecycle

1. Define a narrow model project and owning module.
2. Register authorized knowledge sources.
3. Build and review a purpose-specific dataset.
4. Create an immutable dataset version and checksum.
5. Obtain a separate dataset approval.
6. Submit a future external training job.
7. Register the resulting adapter or model artifact reference.
8. Compare the candidate with the base and active models.
9. Block promotion on correctness, hallucination, permission, structured-output,
   or safety failures.
10. Obtain model and environment approvals.
11. Register the approved endpoint with Module 064.
12. Activate only approved feature routes with a tested rollback target.

## Future persistence boundary

A separately authorized migration may eventually introduce metadata tables for:

- model projects;
- knowledge sources and index versions;
- immutable dataset versions and approved examples;
- external training jobs;
- model versions and artifact checksums;
- evaluation runs and individual results;
- deployment registrations and feature assignments;
- immutable lifecycle audit events.

Large model or adapter files must not be stored in ProjectPulse PostgreSQL. The
database should store approved artifact locations, checksums, classification,
retention, ownership, and lifecycle evidence.

## Future permissions

The intended Modules 012/037 capability model includes:

- View Pulse AI;
- Manage Knowledge Sources;
- Create Training Datasets;
- Approve Training Datasets;
- Start or Cancel Training Jobs;
- Run Evaluations;
- Approve Model Versions;
- Deploy to Test;
- Promote to Production;
- View AI Audit.

Super Administrator retains Full Control. Other roles receive only explicitly
assigned capabilities. No Access hides Module 011 and denies direct API access.
Dataset creation, dataset approval, training operation, and production approval
should remain separable duties.

## Module 064 isolation

The foundation reads only the sanitized `GET /api/ai-configuration` response.
It does not call Module 064 mutation endpoints and does not directly contact
Claude, OpenAI, or another model provider. Provider secrets remain write-only
inside Module 064.

Open PR #215 owns the current automatic provider-health correction. Module 011
must not edit or recreate that PR's Module 064 files. Pulse AI will consume the
shared runtime only after those changes are independently reviewed and merged.

## Historical Module 011 recovery

The prior Work Task Builder implementation remains recoverable from the exact
pre-reuse checkpoint and blob recorded in
`LEGACY-WORK-TASK-BUILDER-RECOVERY.md`. Its business ownership remains in
Modules 055D and 055C; Pulse AI does not reclaim project creation, task
management, assignment, or time-entry responsibilities.

## Owned source files

- `src/frontend/project-time-web/src/PulseAiCenter.jsx`
- `src/frontend/project-time-web/src/pulse-ai-center.css`
- `src/frontend/project-time-web/scripts/validate-module-011-pulse-ai.mjs`
- this documentation directory.

Compatibility edits are limited to:

- `src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx`;
- `src/frontend/project-time-web/src/module-availability-registry.js`;
- `src/frontend/project-time-web/src/permission-aware-more-menu.css`;
- `src/frontend/project-time-web/scripts/validate-group-1-navigation-work-consolidation.mjs`;
- `src/frontend/project-time-web/package.json`;
- `docs/MODULE-CATALOG.md`.

No deployment, migration, Azure, Entra, provider-secret, or live-model change is
authorized by this source foundation.
