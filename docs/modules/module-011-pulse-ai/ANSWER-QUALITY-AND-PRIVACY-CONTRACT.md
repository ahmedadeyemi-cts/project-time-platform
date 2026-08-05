# Module 011 — Answer Quality and Privacy Contract

## Standard

Pulse AI responses must be detailed enough for an authorized ProjectPulse user
to understand:

- the direct answer;
- the underlying records and documents;
- the calculation or decision method;
- the filters and permission scope;
- the data's freshness and completeness;
- material assumptions, contradictions, and uncertainty;
- operational and business implications; and
- the next action the user can take in ProjectPulse.

A short answer can be appropriate only when the question is narrow and the user
requests brevity. The default is a comprehensive, source-grounded response, not
a surface summary.

## Required response structure

A complete live-data answer should contain the following sections whenever they
are material.

### 1. Direct conclusion

Answer the user's question immediately and in plain language. Do not make the
reader infer the result from a list of records.

Examples:

- “Three projects are approaching budget this quarter; the largest driver is
  labor consumption on Project A.”
- “The current Engineer can use two approved project documents for this
  timesheet suggestion, but the GSD extraction is not ready.”
- “The current effective user has View permission for Module 019 but does not
  have project assignment or Project Manager scope for the requested project.”

### 2. Scope and filters

State the effective scope used, including relevant values such as:

- actual user;
- effective user and View-As state;
- module and action;
- project, customer, team, resource, or portfolio;
- reporting period;
- environment;
- status filters;
- document classification and use-case eligibility;
- currency; and
- result limits.

Do not hide a material default. If a period, currency, project, customer, or
workspace was inferred, label it as an assumption and invite correction.

### 3. Source evidence

Identify the ProjectPulse source modules, API contracts, records, document
versions, and generated-at timestamps that support the answer.

Document evidence should identify:

- document ID;
- project ID;
- category;
- file name;
- uploaded time;
- private-processing time;
- extraction status;
- context readiness; and
- source version marker.

The response must not disclose document metadata the user is not authorized to
see.

### 4. Detailed analysis

Explain the relationships and drivers, not merely the totals. A detailed answer
may cover:

- what changed;
- why it changed;
- which records contribute most;
- where the result differs from plan or policy;
- dependencies;
- trends;
- exceptions;
- cross-module effects;
- source conflicts;
- quality or completeness problems; and
- downstream consequences.

### 5. Deterministic calculations

Exact values must come from approved source contracts and deterministic
calculations. The response should state the calculation definition.

Examples:

- utilization numerator and denominator;
- assigned, used, and remaining hours;
- planned cost;
- actual cost;
- forecasted final cost;
- current variance;
- margin when authoritative revenue exists;
- capacity variance;
- working-day duration;
- dependency lead or lag;
- total and free float; and
- age or elapsed time.

The language model must not invent a formula or calculate an authoritative
financial or scheduling result from prose when a deterministic ProjectPulse
contract exists.

### 6. Unknown, stale, and unavailable values

Distinguish among:

- known zero;
- known blank;
- unknown;
- not applicable;
- source unavailable;
- optional source unavailable;
- stale;
- outside scope; and
- not authorized.

Missing data must not be silently converted to zero. A model estimate must not
be presented as an actual value.

### 7. Assumptions, conflicts, and limitations

List material assumptions and unresolved conflicts. Examples include:

- multiple eligible SOW versions;
- multiple eligible GSD versions;
- missing extraction summaries;
- a project name that differs from the resolved project code;
- incomplete financial sources;
- missing currency;
- unavailable rate or contract evidence;
- a calendar that has not incorporated Module 057 holidays;
- missing assignment or capacity information; or
- a question that lacks a reporting period.

### 8. Risks and implications

When applicable, explain:

- delivery risk;
- budget risk;
- billing risk;
- capacity risk;
- compliance risk;
- privacy risk;
- security risk;
- operational risk;
- customer impact; and
- consequences of taking or delaying action.

Do not claim customer impact without evidence.

### 9. Recommended action

Provide actionable next steps and link to the applicable ProjectPulse module or
record. Separate recommendations from confirmed facts.

Pulse AI must not execute the recommendation merely because it suggested it.

### 10. Freshness and audit evidence

Display:

- data-as-of or generated-at timestamp;
- contract version;
- document version or processing time;
- source health;
- record count; and
- relevant filters.

Generation, retrieval, sanitization, feedback, evaluation, model, and provider
evidence must be auditable without storing secrets or unnecessary raw private
content.

## Timesheet response contract

A Module 001 suggestion must:

1. treat the Engineer's rough note as the primary statement of work performed;
2. resolve the selected project, task, or Request / Service Request;
3. verify the effective user's project scope;
4. retrieve only engineering-visible documents enabled for timesheet context;
5. prioritize approved SOW and GSD evidence;
6. identify context readiness, source coverage, conflicts, and missing inputs;
7. use private document context only inside the private ProjectPulse boundary;
8. avoid claiming tools, actions, completion, customer impact, meetings,
   approvals, deliverables, or outcomes that the Engineer did not report;
9. return a customer-facing description suitable for review and audit; and
10. require the Engineer to review, edit, and apply the result.

It cannot change hours, date, time type, project, task, category, allocation,
save state, submission, or approval.

## Help and Search response contract

A product Help answer should contain:

- a direct explanation;
- a detailed procedure;
- required permissions;
- important safeguards;
- module and route ownership;
- navigation targets; and
- current limitations.

A live Search answer should additionally contain:

- the effective permission and record scope;
- source modules and tools;
- query filters;
- record counts;
- detailed findings;
- contradictions and missing records;
- freshness; and
- links to the relevant ProjectPulse records.

When live tool execution is unavailable, Pulse AI must return the required
execution plan and state that it has not supplied live values.

## FlowHive response contract

A FlowHive planning draft should include:

1. project objectives and constraints;
2. source-document inventory and coverage;
3. scope and exclusions;
4. deliverables and acceptance criteria;
5. customer and internal responsibilities;
6. prerequisites;
7. WBS hierarchy;
8. task descriptions and durations;
9. dependency types and rationale;
10. milestones;
11. required roles and skills;
12. planned effort;
13. capacity conflicts;
14. risks and mitigations;
15. assumptions;
16. unresolved questions;
17. source citations; and
18. deterministic schedule results.

Every inferred task, duration, dependency, role, or date must be labeled as an
assumption until validated.

The Project Manager reviews the draft and presents it to Engineering. Engineering
modifies and validates the technical plan. Pulse AI cannot baseline, assign,
reserve capacity, publish, or commit a customer date.

## Reporting and financial response contract

A reporting or financial answer must include:

- authorized workspace;
- reporting period;
- currency;
- source contract and version;
- source health;
- metrics and formula definitions;
- filters and grouping dimensions;
- known, unknown, stale, and unavailable values;
- detailed drivers and exceptions;
- trend or comparison basis;
- risk and operational implications;
- recommended follow-up; and
- generated-at timestamp.

Pulse AI cannot use arbitrary generated SQL. It cannot change a rate, contract,
expense, billing state, invoice, reconciliation, opportunity, accounting period,
or financial source record.

## Privacy classification

### Internal

Ordinary ProjectPulse operating guidance and non-sensitive system metadata.
Access still follows authentication and module visibility.

### Confidential

Project, customer, workforce, delivery, operational, or commercial information
whose disclosure would cause business harm. Private processing is required by
default.

### Restricted

SOW, GSD, architecture, contract, rate, detailed financial, credential,
security, incident, regulated, or highly sensitive personal information.
Restricted content remains inside approved private services unless an explicit,
reviewed policy permits a narrower use.

## Private document rules

- Authorization and security filtering occur before retrieval.
- Original bytes remain in approved storage.
- Extraction and embeddings remain private.
- Every chunk carries project, customer, document, version, classification,
  audience, use-case, retention, and access metadata.
- Permission removal revokes retrieval without retraining the model.
- A document is not training data merely because it was uploaded or retrieved.
- Raw document content and extracted private summaries are not written to browser
  logs, provider logs, audit summaries, or external prompts.
- Browser responses contain only approved evidence metadata unless a separate
  authorized excerpt contract is implemented.

## External provider rules

Claude or OpenAI may receive an external reasoning request only when:

1. the use case explicitly allows external assistance;
2. a separate policy and human review authorize it;
3. the consumer selects a reviewed purpose from the closed backend-owned
   category catalog;
4. the backend constructs the entire generic capsule from a fixed string for
   that category; arbitrary caller text and sensitive-term lists cannot be
   represented in the external request;
5. Module 064 selects the provider and records sanitized operational evidence;
6. the provider payload contains no raw document or unrestricted retrieved
   context;
7. a provider refusal ends routing; and
8. the returned generic reasoning is privately verified against authoritative
   ProjectPulse evidence.

The current sanitization endpoint is a preview only and always returns
`externalExecutionAuthorized = false`.

## Security and authorization rules

- The ProjectPulse backend—not the model—enforces access.
- The actual and effective user are both retained for View-As evidence.
- No Access hides the module and denies its API.
- View does not imply edit, create, delete, approve, export, administration, or
  cross-project access.
- A model cannot request a broader tool result than the user could request
  directly.
- Tool output is filtered before it reaches the model.
- System prompts cannot override backend authorization.
- Prompt injection inside a document cannot grant access, register a tool,
  change a provider route, execute code, or mutate data.

## Hallucination rules

Pulse AI must not invent:

- records;
- documents;
- document versions;
- permission outcomes;
- work performed;
- completion status;
- customer impact;
- financial values;
- formulas;
- rates;
- revenue;
- dates;
- assignments;
- approvals;
- deployments;
- incidents;
- evidence; or
- actions it claims to have completed.

When evidence is insufficient, the response should state:

> I could not find sufficient authorized evidence to answer this question
> completely.

The assistant should then identify what evidence is missing and where an
authorized user can provide or verify it.

## Feedback and learning rules

User feedback can be recorded as:

- accepted;
- accepted with edits;
- rejected;
- inaccurate;
- incomplete;
- unsupported;
- wrong source;
- wrong permission scope;
- wrong calculation;
- privacy concern; or
- unsafe behavior.

Feedback is evaluation evidence, not automatic training data.

Before an example can be used for training it must be:

- sanitized;
- permission-reviewed;
- copyright and license-reviewed;
- purpose-labeled;
- quality-reviewed;
- versioned;
- approved;
- separated from held-out evaluation data; and
- associated with a retention policy.

A model cannot train, approve, promote, deploy, or roll back itself.

## Promotion gates

A candidate model or retrieval change cannot be activated unless it passes:

- answer correctness;
- evidence citation;
- permission isolation;
- document leakage;
- prompt injection resistance;
- unsupported claim rate;
- deterministic calculation consistency;
- structured output compliance;
- refusal behavior;
- latency;
- cost;
- source freshness;
- regression; and
- rollback readiness.

Permission isolation, raw-document leakage, unsupported financial values,
unsafe refusal failover, and authoritative-calculation inconsistency are
promotion-blocking failures.
