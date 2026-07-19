# Module 066 — Project FlowHive Capability Matrix

## Matrix rules

- **Foundation** means working 066A source and a named validation target exist.
- **Planned** means the capability is unavailable and must not be presented as complete.
- **Blocked** means an explicit authorization or upstream dependency is required.
- Every future status advancement requires source, validation, and acceptance evidence.
- Customer-facing PDF and Excel evidence must use the approved US Signal logo.

## Source tracker requirements

| Requirement | Module 066 responsibility | Current phase disposition |
|---|---|---|
| GOV-015 | Maintain the capability matrix and evidence-based phase gates | Foundation in 066A |
| RBAC-019 | Enforce assignment- and role-scoped project planning access | Read-only scope foundation in 066A |
| WRK-011 | Provide the governed Project FlowHive planning workspace | Read-only workspace foundation in 066A |
| AI-008 | Preserve a versioned plan lifecycle | Planned for 066B; no lifecycle claim in 066A |
| AI-019 | Generate draft plans from both approved GSD and SOW sources | Planned for 066D after Module 064 and document authority |
| RPT-013 | Produce customer-safe PDF and portfolio artifacts | Blocked until 066E and approved US Signal logo assets |

## Smartsheet-class planning capabilities

| Capability | Priority | Current status | Planned phase | Dependency | Required acceptance evidence | ProjectPulse integration |
|---|---:|---|---|---|---|---|
| Multi-customer portfolio | P0 | Foundation | 066A | Canonical projects and customers | Backend-scoped portfolio response and filtered UI | Project Workspace, Work Register |
| Read-only task grid | P0 | Foundation | 066A | Canonical project tasks | Authenticated task response and UI grid | Work Task Builder, Timesheet |
| Canonical task reference | P0 | Foundation | 066A | `project_tasks.task_code` | Clearly labeled as a reference, not approved WBS | Work Task Builder |
| Controlled WBS numbering | P0 | Planned | 066B | Versioned planning schema | Unique hierarchical numbers with immutable baseline evidence | Canonical task bridge |
| Parent/child hierarchy | P0 | Planned | 066B | WBS persistence | Expand/collapse, rollups, and server validation | Canonical task bridge |
| Grid editing | P0 | Planned | 066B | Authorized mutation API | Bulk edits validated and audited by role | Project Workspace |
| Milestones and durations | P0 | Planned | 066B | Schedule fields and calendar rules | Milestone and duration calculation tests | Calendar Capacity |
| Working calendars | P0 | Planned | 066B | Module 057 calendar contract | Holiday and resource-calendar schedule evidence | Module 057 |
| Constraints | P0 | Planned | 066B | Schedule engine | Constraint validation and conflict evidence | Calendar Capacity |
| FS/SS/FF/SF dependencies | P0 | Planned | 066B | Dependency persistence | Cycle detection and all four dependency-type tests | Plan versions |
| Leads and lags | P0 | Planned | 066B | Dependency engine | Positive and negative offset calculation tests | Plan versions |
| Gantt view | P0 | Planned | 066C | Schedule engine | Responsive Gantt with dependency and milestone evidence | Project portfolio |
| Timeline view | P1 | Planned | 066C | Schedule engine | Filtered multi-project timeline evidence | PM and Team Lead portfolios |
| Calendar view | P1 | Planned | 066C | Module 057 integration | Schedule/calendar consistency tests | Module 057 |
| Card view | P2 | Planned | 066C | Task status model | Accessible drag/drop or equivalent update evidence | Execution updates |
| Critical path | P0 | Planned | 066C | Dependency network | Independent calculation tests and visible critical tasks | Risk reporting |
| Total and free float | P0 | Planned | 066C | Critical-path engine | Float calculation tests | Risk reporting |
| Approved baselines | P0 | Planned | 066B | Immutable version model | Approval creates immutable baseline snapshot | Change control |
| Revisions and supersession | P0 | Planned | 066B | Baseline version model | New revision preserves prior baseline and audit | Change orders |
| Actual and forecast dates | P0 | Planned | 066B | Execution update model | PM/engineer scoped update evidence | Timesheet, Work Register |
| Percent complete | P0 | Planned | 066B | Execution update model | Server-authorized progress updates with actor/time | Timesheet |
| Remaining effort | P0 | Planned | 066B | Assignment effort model | Rollup and variance evidence | Timesheet, Calendar Capacity |
| Resource assignments | P0 | Foundation | 066A | Canonical assignments | Scope-filtered assignment and hour response | Project Intake, Work Task Builder |
| Workload management | P0 | Planned | 066C | Module 057 integration | Cross-project capacity and overload evidence | Module 057 |
| Rollups | P1 | Planned | 066C | Hierarchy and formulas | Parent rollup calculation tests | Reporting |
| Formulas | P1 | Planned | 066D | Safe expression model | Deterministic formula tests and cycle prevention | Reporting |
| Conditional formatting | P2 | Planned | 066D | Formula/rule model | Accessible rule rendering evidence | Grid and portfolio |
| Filters and grouping | P1 | Planned | 066C | Stable planning fields | Saved filter and grouping tests | Portfolio reporting |
| Forms | P2 | Planned | 066D | Controlled intake schema | Authorized form submission and audit evidence | Work Register intake |
| Approvals | P0 | Planned | 066B | Plan lifecycle | Draft-to-baseline approval and rejection evidence | Approval Center |
| Automations and alerts | P1 | Planned | 066D | Event/outbox model | Deduplication, preview, and delivery audit evidence | Notification center |
| Update requests | P1 | Planned | 066D | Collaboration security | Expiring, scoped request and response evidence | Engineer collaboration |
| Comments and mentions | P0 | Planned | 066B | Collaboration history | Actor/timestamp, edit history, and authorization tests | Identity profile |
| Attachments | P1 | Planned | 066B | Document security | Customer/engineering visibility and retention evidence | Work Register documents |
| Activity history | P0 | Planned | 066B | Immutable audit model | Complete view/edit/approve/baseline timeline | Audit History |
| Templates | P1 | Planned | 066D | Versioned template model | Create, version, apply, and trace template evidence | Work Task Builder |
| Import | P1 | Planned | 066D | Mapping and validation | Preview, validation, rejection, and audit evidence | Work Register |
| Export | P1 | Blocked | 066E | Approved logo assets and audit | Branded, versioned, checksummed artifact evidence | Reporting |
| Customer-safe PDF | P0 | Blocked | 066E | Approved US Signal logo | Restricted-field exclusion and visual QA evidence | Module 030 reporting |
| Customer sharing links | P1 | Planned | 066E | Expiring-token security | Customer isolation, expiration, and audit evidence | External sharing |
| Dashboards and reports | P0 | Planned | 066C | Versioned planning data | PM and Team Lead portfolio acceptance evidence | Module 030 |
| APIs and webhooks | P1 | Planned | 066D | Versioned public contract | Authentication, retry, signing, and audit tests | Integration framework |
| Mobile readiness | P1 | Planned | 066C | Responsive planner UI | Role-based mobile browser validation | Global application shell |
| Accessibility | P0 | Foundation | 066A | Semantic UI foundation | Keyboard controls, labels, tables, and focus evidence | Global application shell |
| Audit and history | P0 | Planned | 066B | Audit persistence | Server-side view/edit/baseline/delegation evidence | Module 008 |
| Assignment-scoped permissions | P0 | Foundation | 066A | Existing role and assignment data | PM, engineer, PTC, Team Lead, and admin query tests | Modules 009, 012, 037 |
| AI GSD/SOW plan generation | P0 | Planned | 066D | Module 064 and approved documents | Dual-document citations, confidence, conflicts, and draft-only output | Modules 024–026, 064 |
| Versioned plan lifecycle | P0 | Planned | 066B | Baseline schema | Draft, review, baseline, revision, superseded, closed, archived evidence | Work Register and closeout |

## Phase gates

### 066A — Read-only foundation

- No database migration.
- No write endpoint.
- No shared application registration while Modules 001, 002, and 062 are active.
- Canonical projects, tasks, assignments, and actual hours are read only.

### 066B — Governed planning persistence

Requires explicit authorization for database source changes and a separate decision
before applying any migration. This phase owns WBS, dependencies, lifecycle,
baselines, execution updates, collaboration history, and server audit.

### 066C — Schedule and portfolio experience

Requires a validated schedule engine, Calendar Capacity integration, workload and
risk calculations, responsive views, and PM/Team Lead acceptance evidence.

### 066D — Automation and AI

Requires Module 064 provider governance, approved GSD/SOW document authority,
versioned templates, safe automation, and draft-only AI output.

### 066E — Customer sharing and artifacts

Requires verified US Signal logo assets, restricted-field exclusion, immutable
export metadata, checksum evidence, visual QA, and explicit external-sharing scope.
