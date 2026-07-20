# Module 066 — Project FlowHive Capability Matrix

## Evidence rules

- **Source ready:** module-owned implementation and validator evidence exist.
- **Preview ready:** side-effect-free computation/UI exists but no authoritative persistence.
- **Request ready:** a governed downstream request exists but execution is locked.
- **Locked:** the API/UI explicitly refuses the capability pending authorization.
- **Active:** reserved for merged, registered, deployed, and runtime-validated behavior.

No capability in this source-only package is labeled active.

## Tracker requirements

| Requirement | Source disposition |
|---|---|
| GOV-015 | Capability matrix, evidence rules, phase/release gates |
| RBAC-019 | Existing backend portfolio scope preserved; future mutation authorization specified |
| WRK-011 | Full Project FlowHive source workspace and phased contracts |
| AI-008 | Version lifecycle/persistence interfaces specified and locked |
| AI-019 | GSD/SOW Module 064 request preview and citation/conflict rules |
| RPT-013 | US Signal branded internal PDF/XLSX previews; customer delivery locked |

## Capability status

| Capability | Phase | Status | Evidence / remaining gate |
|---|---|---|---|
| Shared endpoint mapping | 066A.1 | Source integrated | Exactly one guarded `Program.cs` registration |
| Route/navigation/UI mapping | 066A.1 | Source integrated | Role-aware route, navigation, registry, and mount present |
| Build-validator wiring | 066A.1 | Source integrated | Protected build chain and container validation context present |
| Role/assignment-scoped portfolio | 066A | Source ready | Existing canonical backend query preserved |
| Module 062 identity references | 066B | Source ready | Assignment user IDs and shared identity component/hook |
| Controlled WBS validation | 066B | Preview ready | Numeric dotted hierarchy, duplicate/parent checks |
| Task editing | 066B | Preview ready | Browser-memory planner; no persistence |
| FS/SS/FF/SF dependencies | 066B/066C | Preview ready | Directed dependency engine and type-specific offsets |
| Leads/lags | 066B/066C | Preview ready | -365–365 working-day validation/calculation |
| Cycle detection | 066C | Preview ready | Topological validation blocks cyclic network |
| Weekday schedule | 066C | Preview ready | Earliest/latest dates and project finish |
| Critical path | 066C | Preview ready | Zero-total-float critical task evidence |
| Total/free float | 066C | Preview ready | Per-task deterministic values |
| Gantt/timeline | 066C | Preview ready | Responsive proportional timeline UI |
| Working calendars | 066C | Locked | Module 057 holiday/resource integration required |
| Workload | 066C | Partial preview | Identity assignments/planned hours; cross-project persistence required |
| Baseline approval | 066B | Locked | HTTP 423; Module 002/persistence/audit required |
| Revision/supersession | 066B | Locked design | Persistence design only |
| Comments/mentions/attachments | 066B | Locked design | Security/history model only |
| Activity history | 066B | Locked design | Immutable persistence required |
| GSD/SOW AI request | 066D | Request ready | Sanitized `project_flowhive_plan` request |
| Claude/OpenAI/local routing | 066D | Shared router registered; FlowHive execution locked | Module 064 is integrated in the release train; FlowHive exposes request preview only |
| AI refusal safety | 066D | Source ready rule | Refusal must stop provider failover |
| Local deterministic fallback | 066D | Request ready | Preserves supplied tasks; adds no commitment |
| Automations/alerts | 066D | Locked design | Event/outbox/delivery authorization required |
| Internal PDF preview | 066E | Preview ready | Actual US Signal JPEG embedded, draft watermark |
| Internal Excel preview | 066E | Preview ready | Actual US Signal JPEG, schedule/dependency/control sheets |
| Customer-safe baseline export | 066E | Locked | Baseline, redaction, approval, visual QA required |
| Customer sharing links | 066E | Locked | No token, link, customer state, or delivery source active |
| API/webhooks | 066D/066E | Locked design | Versioned auth/signing/retry/audit required |
| Accessibility/mobile | 066C | Source ready for build review | Semantic controls/tables; browser acceptance still required |

## External-state statement

This package creates no database object, Azure or Entra change, deployment,
AI-provider request, customer link, email, webhook, or external record.
