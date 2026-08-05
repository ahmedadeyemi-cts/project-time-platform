# Celar AI Enterprise Platform Interface

- **Module:** 011
- **Company:** US Signal
- **Platform:** Pulse
- **Intelligence system:** Celar AI
- **Created by:** Dr. Ahmed Adeyemi, Manager of Professional Services
- **Contract:** `celar-ai-enterprise-platform-v1-20260801`
- **Classification:** US Signal Internal — Confidential

## Purpose

This package turns Module 011 from a collection of lifecycle placeholders into a visible, useful enterprise AI platform surface. It combines the existing Celar AI system-intelligence, private-document, private-RAG, FlowHive, Timesheet, reporting, financial, API-discovery, troubleshooting, provider, training, evaluation, and governance foundations into one understandable interface.

The user-visible architecture uses the US Signal logo and identifies the system as created by Dr. Ahmed Adeyemi. It explains the complete private-first flow from authentication through document and tool retrieval, private reasoning, confidence assessment, optional sanitized external reasoning, private verification, and the final cited answer or reviewable draft.

## Enterprise outcomes

### Normal contextual chat

The global chat defaults to a normal working-companion size rather than covering the entire Pulse workspace. It supports compact, standard, wide, fullscreen, minimized, and manually resized desktop states.

- Enter sends.
- Shift+Enter inserts a line.
- Escape closes.
- The conversation area scrolls independently.
- A fresh visible chat does not automatically reopen the most recent conversation.
- Historical conversations remain available to the same user through an explicit History drawer.
- A historical conversation is used only when the user selects it.
- Unrelated conversations are never merged.
- New-chat context fields are cleared.

The user may explicitly add a project code, project name, person/team, and date range to the current question. This context is not copied from a prior conversation.

### People and work intelligence

Celar AI can answer questions about authorized assignments, open work, resource requests, capacity, utilization, approval state, and FlowHive planning by invoking read-only owning APIs. It distinguishes:

- assigned work;
- planned work;
- recorded or submitted work;
- pending work; and
- real-time presence, which is not inferred unless an authoritative source supplies it.

Conversation history is not treated as evidence of what a person is currently doing. The capability does not enable arbitrary SQL, arbitrary URLs, cross-user history search, activity surveillance, or unauthorized personnel comparisons.

### Platform operating guidance

The source-controlled procedure catalog explains common Pulse workflows, including project creation, project maintenance, SOW/GSD document handling, Timesheet entry and AI suggestion review, approvals, FlowHive planning, provider configuration, troubleshooting, permissions, Analytics Center usage, and defect reporting.

The target module remains the authorization and mutation authority. Celar AI explains and drafts; it does not silently perform the action.

### Enterprise solution composer

The Module 011 interface supports five review-draft modes:

1. `timesheet_description`
2. `sow_draft`
3. `project_plan`
4. `project_timeline`
5. `project_diagram`

The composer uses the existing private RAG and FlowHive contracts.

#### Timesheet

The Engineer’s factual note remains the primary evidence that work occurred. Authorized project documents and work-item context improve scope terminology. Celar AI cannot change hours, dates, project, task, save state, submission, or approval.

#### SOW

The SOW composer creates a non-binding draft containing objectives, scope, exclusions, deliverables, responsibilities, assumptions, dependencies, acceptance criteria, timeline/milestone guidance, risks, open questions, and citations. Commercial, legal, technical, security, delivery, and customer review remain required.

#### Project planning

The private FlowHive planner creates a WBS, descriptions, durations, required roles, predecessors, milestones, risks, assumptions, out-of-scope items, open questions, conflicts, and citations.

#### High-level timeline

The composer converts draft task durations and predecessor order into a business-day timeline. The result is deliberately high level. Module 066 must recalculate the authoritative schedule using approved dates, calendars, holidays, lead/lag, dependencies, and resource capacity before baseline.

#### Project diagram

The composer produces an accessible structured diagram and Mermaid source from the private project-plan draft. The browser also supports an editable SVG download. Diagram nodes retain source citation IDs and assumption status.

The diagram is a review artifact. It cannot commit a customer date, publish architecture externally, baseline a plan, or assign an Engineer.

## Private-first architecture

```text
Pulse users
    |
Authentication / roles / permissions / record scope
    |
    +-- Private document retrieval: SOW, GSD, design, architecture, project evidence
    |
    +-- Governed live-data tools: projects, time, capacity, finance, APIs, diagnostics
    |
Private Celar AI intelligence layer
    |
Confidence and evidence assessment
    |
    +-- Sufficient private evidence -> private verification
    |
    +-- Generic reasoning may help
           |
        Sanitization / DLP
           |
        Module 064
           |
        Claude or OpenAI
           |
        private verification
    |
Detailed cited answer or reviewable Timesheet, SOW, plan, timeline, or diagram
```

## Optional sanitized external reasoning

External reasoning is disabled by default and requires both:

```text
PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED=true
PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION=true
```

Even when enabled, the route accepts only a closed backend-owned purpose category.
The backend maps that category to a fixed generic capsule; there is no request
field capable of carrying caller-authored problem text, customer names, project
names, person names, sensitive-term inventories, or substrings into the provider
payload. Unknown, empty, and mode-mismatched categories fail closed. The route
also blocks:

- private document text;
- customer and project identities;
- people and workload records;
- financial and commercial values;
- credentials and secrets;
- URLs, IP addresses, record IDs, and long internal identifiers;
- arbitrary tool responses; and
- any classification other than public or internal-generic.

Module 064 remains the only provider boundary. A safety refusal ends the route and is never bypassed by another provider. External output is untrusted generic assistance until Celar AI applies it inside the private boundary and the result is reviewed against authoritative evidence.

## New API surface

```text
GET  /api/celar-ai/v1/platform/readiness
GET  /api/celar-ai/v1/architecture
POST /api/celar-ai/v1/compose

GET  /api/celar-ai/v1/context-policy
GET  /api/celar-ai/v1/guidance/catalog
GET  /api/celar-ai/v1/people-activity/readiness
POST /api/celar-ai/v1/chat
```

Existing `/api/pulse-ai/*`, `pulse_ai_*`, `PULSE_AI*`, and `PROJECTPULSE_PULSE_AI*` contracts remain compatibility identifiers.

## Security and governance

- Authentication and authorization occur before retrieval or tool execution.
- Owning modules reauthorize their data.
- View-As remains read-only.
- Historical conversations are effective-user scoped.
- New chats do not automatically ingest the previous conversation.
- Raw document chunks and vectors are never returned to the browser.
- No arbitrary SQL or URL is accepted.
- No public model receives raw internal documents or restricted live data.
- Feedback does not automatically become training data.
- Consequential actions remain human controlled.

## Locked behavior

This package does not:

- apply a migration;
- deploy or roll back Pulse;
- configure Azure, Entra, networking, DNS, storage, or Container Apps;
- configure a private inference endpoint;
- add or reveal a provider secret;
- enable external fallback by default;
- train or promote a model;
- publish a SOW;
- save or submit time;
- baseline a project plan;
- assign resources;
- commit a customer date;
- change a financial record; or
- modify a role or permission.

## Acceptance criteria

- Module 011 visibly displays the US Signal Celar AI architecture and creator attribution.
- Every main Celar AI capability has an understandable current-state card.
- The solution composer produces source-grounded review artifacts or explicit missing-evidence results.
- Project diagrams are accessible, reviewable, and downloadable as SVG.
- The default chat does not prevent users from working in the underlying Pulse module.
- A fresh chat does not auto-load the previous conversation.
- User history remains available only through explicit selection.
- People/work answers remain permission scoped and distinguish assignment from real-time presence.
- External fallback remains disabled by default and fail closed when restricted data is detected.
- API, frontend, container, existing Celar AI, Module 001, Module 066, provider, permission, and regression validations pass on the exact PR head.
