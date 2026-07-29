# Module 011 Pulse AI — Authoritative Intelligence Scope

## Product mission

Pulse AI is the private, permission-aware intelligence layer for ProjectPulse.
It is not merely a model-training console. It exists to understand approved
internal documents and live ProjectPulse data so users can receive useful,
source-grounded assistance across the application.

The authoritative primary uses are:

1. document-grounded timesheet suggestions;
2. system-wide Help and Search;
3. document-grounded FlowHive project planning;
4. reporting, financial, and cross-system insight; and
5. future role-authorized intelligence functions built on the same private
   retrieval, tool, evaluation, and governance foundation.

Module 064 remains the provider, secret, health, usage, and routing boundary.
Module 011 owns knowledge, retrieval, evaluation, model lifecycle, intelligence
orchestration, and the evidence needed to decide whether a private or external
reasoning path may be used.

## 1. Document-grounded timesheet suggestions

### User experience

When an Engineer selects a row under **Regular Tasks** or **Requests / Service
Request** in Module 001 and chooses **Generate AI suggestion**, Pulse AI should:

1. identify the selected project, task, request, work date, time type, and
   engineer assignment;
2. read the Engineer's rough note as the primary statement of what was actually
   performed;
3. locate the current approved SOW, GSD, and other engineering-visible project
   documents;
4. retrieve only document sections relevant to the selected work item;
5. identify the documented scope, deliverables, technologies, constraints, and
   terminology that can safely improve the description;
6. generate a concise customer-facing description that aligns with the
   documents without claiming work the Engineer did not report;
7. record the source document IDs, versions, retrieved chunks, confidence,
   provider path, and as-of time in non-secret evidence; and
8. require the Engineer to review and explicitly apply the suggestion.

### Source precedence

The sources should be used in this order:

1. Engineer rough note;
2. selected task or service request;
3. current Engineer assignment and project scope;
4. approved SOW and GSD;
5. approved related engineering documents; and
6. governed project or service templates.

The SOW or GSD may improve terminology and scope alignment, but it must not
cause Pulse AI to claim that an activity occurred when the Engineer's note and
selected work item do not support it.

### Non-negotiable restrictions

Pulse AI cannot:

- change entered hours;
- alter the selected date, project, task, request, or time type;
- create a project, task, request, or assignment;
- save or submit a timesheet;
- approve or reject time;
- state that work is complete without supporting evidence; or
- expose restricted document text to an unauthorized user.

The existing Module 001 contract already sends the selected row and Engineer
note to `/api/timesheets/ai-description-suggestions`. A future Module 011
implementation should enrich the request on the backend after authorization,
rather than sending SOW/GSD content from the browser.

## 2. System-wide Help and Search

### User experience

A user should be able to ask natural-language questions from ProjectPulse Help,
a global Search surface, or a future Pulse AI conversation page.

Examples include:

- How do I submit or correct my timesheet?
- Which projects am I assigned to this month?
- Why can I not open a particular module?
- Which project documents are available to Engineering?
- What is blocking billing readiness for a project?
- What changed in the last release?
- Which open defects affect Module 001?
- What is the current status of an integration?
- How many hours remain on a contract or assignment?
- Which reports are available to my role?

### Two distinct answer modes

#### Product and process Help

Uses:

- Module 999;
- approved module documentation;
- the module catalog;
- workflow and status definitions;
- role and permission explanations; and
- approved operating runbooks.

#### Live system Search and question answering

Uses permission-filtered, read-only ProjectPulse tools to retrieve current:

- projects, customers, tasks, assignments, and resource requests;
- time, approvals, utilization, and compliance;
- documents and document-processing status;
- opportunities, contracts, rates, expenses, billing, and invoices;
- releases, defects, integrations, health, diagnostics, and audit evidence; and
- any later registered ProjectPulse data domain.

### Required answer evidence

A live answer should normally include:

- the source module or document;
- the applied role, project, customer, date, and other material filters;
- the as-of timestamp;
- record counts or calculation scope;
- assumptions and conflicts;
- an uncertainty statement when evidence is incomplete; and
- a direct navigation target when the user can inspect the source record.

### No arbitrary database access

The model must not invent SQL or receive unrestricted database credentials.
Live information must come through approved read-only APIs or a governed
semantic tool layer that enforces row- and field-level authorization.

## 3. Document-grounded FlowHive planning

### User experience

For Module 066, Pulse AI should create a proposed project plan from:

- the approved SOW and GSD;
- architecture, order, quote, proposal, and supporting project documents;
- project dates, constraints, assumptions, and dependencies;
- available resource roles and capacity;
- working calendars and holidays;
- relevant approved project templates; and
- known risks or unresolved inputs.

The result should include:

- WBS hierarchy;
- tasks and descriptions;
- proposed durations;
- dependencies and sequencing;
- milestones and decision points;
- required roles or skills;
- assumptions and constraints;
- risks and mitigations;
- unresolved questions;
- proposed timeline ranges; and
- citations to the source documents and versions.

### Human workflow

1. Pulse AI creates a draft.
2. The Project Manager reviews the source coverage and assumptions.
3. The Project Manager presents the draft to the Engineer or engineering lead.
4. Engineering modifies tasks, durations, dependencies, and technical details.
5. A separately authorized user approves a baseline through the existing
   FlowHive workflow.

Pulse AI cannot baseline a plan, assign a person, reserve capacity, publish a
customer commitment, or change an approved project date by itself.

## 4. Reports, financials, and deep system insight

Pulse AI should support natural-language analysis across all data domains the
user is authorized to access.

### Operational and reporting insight

Relevant sources include Modules 003, 008, 013, 018, 022, 023, 030, 036, 057,
058, 063, 075, 077, 078, 079, 997, and 998.

Example questions:

- Which projects are most at risk of missing their planned dates?
- Where is utilization below target and why?
- Which approval or billing queues are growing?
- What changed in system health over the last seven days?
- Which defects are repeatedly affecting the same module?
- Which projects have document, assignment, or handoff gaps?

### Financial and commercial insight

Relevant sources include Modules 005, 022, 026, 030, 038, 039, 042, 055B, 060,
and 063.

Example questions:

- What revenue, cost, and margin are expected for a project or customer?
- Which projects have expense or billing-readiness exceptions?
- What uninvoiced exposure exists for the selected period?
- Which contracts are nearing expiration or balance exhaustion?
- Which rates, invoice records, or reconciliations require review?
- How do actual hours and costs compare with plan?
- What explains a change in margin or billing readiness?

### Deterministic calculation rule

Financial values and operational KPIs must be calculated by approved code or
semantic definitions. The model may:

- select the correct approved calculation;
- explain the result;
- compare periods and segments;
- identify drivers and anomalies;
- summarize exceptions; and
- recommend questions or follow-up analysis.

The model may not invent formulas, silently change the calculation basis, or
produce unsupported financial values.

Every financial answer should show, as applicable:

- currency;
- period and date basis;
- customer/project/contract scope;
- rate or cost basis;
- formula or metric definition;
- included and excluded records;
- source modules;
- record count;
- as-of timestamp; and
- assumptions or data-quality warnings.

Pulse AI remains read-only. It cannot post an invoice, approve an expense,
change a rate, edit a contract, reconcile accounting, or change a financial
record.

## 5. System-wide data access model

"Answer anything about the system" means Pulse AI can use every approved data
surface appropriate to the question and the caller. It does not mean every user
can see every record.

Every request must resolve:

1. the actual signed-in user;
2. any effective View-As identity;
3. module access from Modules 012 and 037;
4. action permissions;
5. project, customer, team, and record scope;
6. field-level restrictions;
7. document classification and engineering visibility;
8. purpose and requested output; and
9. whether external escalation is permitted for that data class.

Super Administrator may receive organization-wide Full Control where the
underlying module grants it. Other users receive only their effective scope.
No Access hides the feature and denies direct API requests.

## 6. Self-sustaining operating model

Pulse AI should continuously maintain its knowledge and quality without becoming
an uncontrolled self-modifying system.

### Automatic maintenance that is appropriate

- detect approved new or changed documents;
- scan, extract, classify, chunk, embed, and index approved versions;
- remove or restrict indexed content when access changes;
- refresh stale live-data caches;
- detect missing source coverage and low-confidence answers;
- capture whether users accepted, edited, rejected, or reported an answer;
- run scheduled evaluation suites;
- identify repeated failure patterns;
- prepare sanitized candidate training examples; and
- recommend a new model or adapter version for review.

### Actions that remain human-approved

- admitting a new data source;
- changing document classification;
- approving a training dataset;
- starting a fine-tuning job;
- selecting a base model;
- approving a model version;
- enabling an external escalation policy;
- assigning a model to a production feature;
- promoting to production; and
- changing authorization or privacy policy.

Pulse AI must never autonomously modify its own policies, tools, prompts,
training data, model weights, deployment, or production routing.

## Source-phase status

This document defines the authoritative product scope. The current PR remains a
source-only foundation:

- no document extraction or indexing;
- no vector database;
- no private model endpoint;
- no live system question-answering API;
- no financial query tool;
- no external escalation;
- no training job;
- no migration;
- no deployment.

Those capabilities require separately reviewed implementation phases described
in `PRIVATE-DATA-AND-ESCALATION-ARCHITECTURE.md`.
