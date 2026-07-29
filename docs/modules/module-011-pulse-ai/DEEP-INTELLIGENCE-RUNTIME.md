# Module 011 — Pulse AI Deep Intelligence Runtime

## Purpose

This package begins the operational implementation of Pulse AI as the private,
permission-aware intelligence layer for ProjectPulse. It extends the Module 011
foundation without changing the protected Module 011 foundation PR, applying a
database migration, deploying an application, modifying Azure, changing a
provider secret, or enabling unrestricted external AI access.

The package is intentionally implemented on the dependent branch:

`feature/module-011-pulse-ai-deep-intelligence-20260728`

It was created from the current Module 011 foundation head and is intended to be
reviewed as a separate pull request whose base is the Module 011 foundation
branch. PR #218 remains separate provider-neutral resilience work. PR #220
remains the independent Group 3 project-financial truth source and is consumed
only after its own review and integration.

## Product objective

Pulse AI must provide detailed, comprehensive, source-grounded assistance for:

1. Module 001 document-grounded timesheet descriptions;
2. global ProjectPulse Help and Search;
3. Module 066 FlowHive planning from SOW, GSD, architecture, order, and related
   project documents;
4. reports, financials, utilization, capacity, commercial, operational,
   security, and cross-system insight; and
5. future ProjectPulse consumers registered through the same governed
   intelligence and tool boundary.

A detailed response is not permission to expose more data. The assistant must
be broad in reasoning but limited to the current effective user's authorized
modules, projects, customers, records, documents, environments, and actions.

## Implemented source capabilities

### 1. Private document-grounding service

`PulseAiDocumentGroundingService` now provides a server-side read-only grounding
contract. It:

- resolves the effective ProjectPulse user, including authorized Administrator
  View-As context;
- loads the user's active roles;
- resolves the selected project by exact project code or exact project name;
- verifies broad, Project Manager, assignment, and engineering-resource-request
  project scope;
- resolves the selected canonical project task where possible;
- resolves an engineering resource request where the row represents a Request /
  Service Request;
- inspects the live project-document schema before querying optional columns;
- retrieves only active, engineering-visible project documents;
- requires `ai_timesheet_context_enabled` for Module 001 grounding;
- prioritizes SOW, GSD, architecture/design, order, quote/proposal, and other
  supporting documents;
- consumes the existing approved `ai_context_summary` only when extraction is in
  a ready state;
- calculates source coverage, missing inputs, document-version conflicts, source
  categories, and private-context readiness;
- derives high-level scope themes from approved extracted summaries without
  returning the summaries; and
- returns sanitized metadata and evidence only.

The API response never contains original document bytes, raw extracted text, or
`ai_context_summary` values.

### 2. Module 001 timesheet integration

The existing Module 001 endpoint remains authoritative:

`POST /api/timesheets/ai-description-suggestions`

No duplicate endpoint or second user button was created. The existing
`ProjectPulseAiTimeEntrySuggestionService` now attempts private document
grounding for the current effective user and selected project/task/request.

When approved private document context is ready:

- the service uses a deterministic private-grounded suggestion path;
- the response identifies document metadata, source coverage, conflicts, and
  missing inputs in its warning/evidence text;
- the raw document and extracted summary remain inside the service boundary;
- Claude and OpenAI are not called with the private document context; and
- the Engineer must review and explicitly apply the description.

When approved document context is absent or not ready:

- the existing non-document AI route can still use the Engineer's rough note and
  selected row context;
- the remote prompt explicitly states that it contains no SOW, GSD,
  architecture, contract, rate, financial, or customer-document content;
- the result identifies the grounding limitation; and
- no restricted document is retrieved or transmitted.

The Engineer's rough note remains the primary statement of work performed. An
SOW or GSD can improve wording and scope alignment, but it cannot prove that a
specific activity occurred.

Pulse AI cannot change:

- hours;
- work date;
- Normal or Afterhours type;
- project, task, request, or category;
- allocation;
- timesheet status;
- save state;
- submission state; or
- approval state.

### 3. Detailed Help and Search planning

`PulseAiQuestionPlanner` classifies a question across all relevant domains rather
than stopping after the first keyword match. Its registered domains include:

- product Help and documentation;
- projects, delivery, and documents;
- time, work, approval, utilization, and capacity;
- FlowHive planning;
- finance and commercial information;
- identity, permissions, security, privacy, and audit; and
- platform operations, defects, release, observability, backup, recovery,
  replication, and diagnostics.

For each question it prepares:

- relevant domains;
- owning modules;
- required read-only tools;
- required evidence;
- filters that must be resolved;
- deterministic calculations;
- required answer sections;
- a detailed execution sequence;
- privacy controls;
- missing inputs; and
- a governed semantic query plan.

The planner includes detailed direct product guidance for high-value questions,
including:

- document-grounded timesheet suggestions;
- No Access, View, HTTP 403, and View-As behavior;
- project creation and existing-project management;
- SOW/GSD upload, classification, extraction, and AI-context readiness;
- FlowHive project planning;
- reports and financial analysis;
- Module 064 provider configuration; and
- defect reporting.

The global Help assistant now calls the planner. When a detailed direct product
answer exists, the assistant displays the full procedure, important controls,
source modules, and ProjectPulse navigation targets. For questions requiring
live multi-tool data, it displays the complete evidence and calculation plan and
clearly states that it has not invented live values.

A static local fallback remains available when the new endpoint is unavailable.
The fallback has been corrected to reflect that Modules 055D and 055C—not the
retired Work Task Builder—own project and task workflows.

### 4. Governed tool registry

The source registers read-tool contracts for:

- ProjectPulse product knowledge;
- roles and permissions;
- Project Workspace and project documents;
- private document grounding;
- timesheet, work, approval, and compliance;
- FlowHive planning;
- capacity and utilization;
- Group 3 project financial truth;
- customers, opportunities, contracts, rates, and pipeline;
- release, defect, deployment, observability, and diagnostics; and
- audit, security, privacy, and governance.

A tool registration describes its owner, routes, availability, access policy,
data classification, calculation policy, mutation policy, evidence policy, and
supported question types.

Tool registration does not grant access. Each owning module and record-level
policy remains authoritative.

### 5. FlowHive private-first planning contract

The former FlowHive AI preview accepted raw GSD and SOW excerpts in a prompt
prepared for the shared provider router. This package corrects that boundary.

The current preview:

- constructs the detailed prompt only as a private payload;
- returns only a hash and length evidence for that private payload;
- identifies the required private model target;
- marks the former Claude/OpenAI/local route as rejected for raw-document use;
- provides only a generic abstract planning capsule for any future separately
  approved external reasoning path;
- excludes document excerpts, project and customer identity, infrastructure and
  record identifiers, commercial values and terms, and sensitive authentication
  material from that capsule; and
- performs no provider call.

The planned FlowHive output contract contains:

- project objectives and constraints;
- document source coverage;
- scope and exclusions;
- deliverables and acceptance evidence;
- customer and internal responsibilities;
- prerequisites and dependencies;
- WBS hierarchy;
- tasks, descriptions, durations, and required roles;
- milestones;
- risks and mitigations;
- assumptions;
- unresolved questions;
- source citations; and
- a structured input for FlowHive's deterministic schedule engine.

The language model does not calculate or establish the authoritative schedule.
FlowHive calculates working dates, dependency types, lead and lag, critical path,
total float, free float, milestones, and calendar effects through its
deterministic engine.

### 6. Reporting and financial insight planning

The planner recognizes detailed questions about:

- planned and actual cost;
- forecasted final cost;
- current variance;
- budget status;
- revenue and margin when authoritative values exist;
- expenses;
- rates;
- contracts and block-of-hours balances;
- billing and invoice readiness;
- time and utilization;
- capacity and assignments;
- customers, projects, and Project Managers; and
- current and prior reporting periods.

It produces a governed semantic read plan instead of arbitrary SQL.

The plan requires exact values to come from deterministic source contracts. The
model explains the result, identifies drivers and exceptions, and recommends
follow-up; it does not invent formulas or hidden values.

PR #220 owns the independent Group 3 project-financial truth contract and these
routes:

- `GET /api/project-financials/portfolio`
- `GET /api/project-financials/reporting-summary`
- `GET /api/project-financials/projects/{projectId}`
- `GET /api/project-financials/sources`

This dependent Pulse AI package does not copy or recreate that implementation.
Its runtime consumption remains explicitly unregistered until PR #220 is
independently reviewed and integrated.

### 7. Sanitized external-reasoning preview

`PulseAiEscalationSanitizer` creates a local preview of a minimal reasoning
capsule. It detects and replaces categories including:

- secret-like assignments;
- email addresses;
- URLs;
- IPv4 addresses;
- record identifiers;
- financial values;
- phone numbers;
- long project, host, and infrastructure identifiers;
- labeled customer and person values; and
- caller-supplied explicit sensitive terms.

The result includes redaction counts and categories. It always returns:

`externalExecutionAuthorized = false`

The preview never calls Claude, OpenAI, a private model, or another external
platform. Restricted, confidential, financial, contract, SOW, GSD, and
credential-like inputs remain blocked from execution even after redaction unless
a separate policy and human review are approved in a future phase.

## Registered API surface

All APIs require a valid ProjectPulse session and honor the current effective
user:

| Method | Route | Purpose | Mutation |
|---|---|---|---|
| GET | `/api/pulse-ai/v1/overview` | Deep-intelligence runtime and policy overview | None |
| GET | `/api/pulse-ai/v1/private-runtime/readiness` | Private document/model/index readiness | None |
| GET | `/api/pulse-ai/v1/tools` | Governed read-tool registry | None |
| GET | `/api/pulse-ai/v1/timesheet/context-preview` | Module 001 private grounding evidence | None |
| GET | `/api/pulse-ai/v1/help-search/plan` | Detailed Help/Search answer plan | None |
| GET | `/api/pulse-ai/v1/flowhive/context-preview` | Module 066 source and planning contract | None |
| GET | `/api/pulse-ai/v1/insights/plan` | Reporting and financial semantic plan | None |
| POST | `/api/pulse-ai/v1/external-escalation/sanitize-preview` | Local deterministic redaction preview | None |

The POST endpoint transforms caller-supplied text in memory and returns a
preview. It performs no durable write or external call.

## Private runtime readiness

The readiness API detects whether the following optional private services are
configured without returning their values:

- `PROJECTPULSE_PRIVATE_AI_ENDPOINT`
- `PROJECTPULSE_PRIVATE_AI_MODEL`
- `PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT`
- `PROJECTPULSE_PRIVATE_EMBEDDING_MODEL`
- `PROJECTPULSE_PRIVATE_VECTOR_INDEX`
- `PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION`

The source does not create or modify these resources.

The readiness response also inspects whether ProjectPulse has:

- a project document table;
- engineering visibility metadata;
- the timesheet AI-context flag;
- extraction status;
- approved AI context summaries;
- context processing timestamps; and
- authorized ready-document counts for the effective user.

## User interface

Module 011 now mounts an interactive Deep Intelligence Workbench with the
following workspaces:

1. Private Runtime
2. Timesheet Grounding
3. Help & Search
4. FlowHive Planning
5. Reports & Financials
6. Privacy Capsule
7. Tool Registry

The workbench displays structured evidence and complete JSON for review. It does
not upload documents, run extraction, create embeddings, submit training, call a
model, change providers, create a project plan, modify a financial record, or
deploy anything.

The global Help launcher is renamed **Ask Pulse AI** and uses the detailed
Help/Search planner while preserving Module 999 and Module 076 shortcuts.

## Data and privacy boundary

The following data remains inside the approved private ProjectPulse boundary by
default:

- SOW and GSD document content;
- architecture, design, order, and implementation documents;
- contracts, pricing, and rates;
- customer and project identities;
- project records and assignments;
- employee and user information;
- reports and financial records;
- credentials and connection information;
- extracted context summaries; and
- private vector embeddings.

External providers receive no document bytes and no unrestricted retrieved
context.

## No migration or environment mutation

This package includes:

- no database migration;
- no rollback SQL;
- no database mutation;
- no Azure or Entra change;
- no Container App change;
- no provider secret change;
- no Module 064 route activation;
- no external model execution;
- no training or fine-tuning job;
- no vector index creation;
- no deployment or rollback workflow; and
- no production change.

## Remaining implementation phases

### Private extraction worker

A future approved worker must retrieve original document bytes through the
existing authorized document service, scan for malware, extract native text,
use OCR only when necessary, classify content, create section and page
citations, identify document versions, and produce a human-reviewable private
context package.

### Private embedding and vector retrieval

A future private embedding service and index must apply security metadata to
every chunk, including user, role, project, customer, document, classification,
use case, version, and retention scope. Security filtering must occur before
retrieved context is provided to a model.

### Private model endpoint

A future private model endpoint must support detailed instructions, structured
outputs, tool selection, source citations, refusal handling, evaluation, health,
canary, rollback, and Module 064 registration. Raw internal documents cannot be
routed to public provider endpoints.

### Live multi-tool Help/Search execution

Each owning module must expose or approve a sanitized read adapter. Pulse AI will
orchestrate those tools after authorization, combine results, calculate exact
values deterministically, identify source health and conflicts, and produce a
comprehensive answer.

### FlowHive generation and approval

A future private model adapter will generate a structured planning draft. The
Project Manager reviews and presents it to Engineering. Engineering modifies and
validates it. Persistence, baseline, assignment, capacity reservation, customer
publication, and date commitment remain separately authorized.

### Financial execution

After the Group 3 source is independently integrated, Pulse AI can consume the
financial truth contract through its read-only API. It must preserve contract
version, formulas, currency, filters, source health, unknown values, and
freshness.

### Governed learning

Accepted and corrected results may become evaluation candidates. They do not
become training records until sanitized, reviewed, versioned, approved, and
separated into training, validation, and held-out evaluation datasets. No model
may retrain or promote itself.
