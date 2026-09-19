# FlowHive PSA benchmark and delivery gates

Reviewed 2026-09-19 for PR #1114. This is a requirements benchmark against vendor
primary sources, not a claim of verified feature parity. Product plans, add-ons,
integration depth, and deployment scale differ. An exhaustive union of every PSA
and project-management product is not a finite release scope. The following
representative set covers general work management, enterprise PSA, and MSP PSA.

## Reference products

| Product / official source | Capabilities used as the benchmark |
| --- | --- |
| [Smartsheet](https://www.smartsheet.com/platform) | Grid/Gantt/calendar views, workflow approvals and notifications, portfolio budgets/resources/risks, contextual AI, document versions, governed dashboards. |
| [ClickUp](https://clickup.com/features) | Tasks and dependencies, multiple views, time tracking, documents, dashboards, automations, AI assistance and integrations. |
| [Microsoft Planner / Project](https://www.microsoft.com/en-us/microsoft-365/planner/microsoft-planner) | My Tasks, Teams integration, Gantt, advanced dependencies with lead/lag, task history, resource requests, program and demand management; availability depends on plan. |
| [Asana](https://asana.com/features) | Goals, portfolios, reporting, intake forms, reusable workflows, capacity and workload planning, time/budget tracking and access controls. |
| [Wrike](https://www.wrike.com/features/) | Gantt dependencies, calendars, resource bookings, cross-project workload and skills, delivery collaboration. |
| [Jira](https://www.atlassian.com/software/jira/features) | Hierarchical work, dependencies, customizable workflows, boards/timelines/calendars, automation, AI risk summaries, reporting and integration APIs. |
| [ConnectWise PSA](https://www.connectwise.com/platform/psa) | Project/resource planning, client profitability and utilization, time entry, service/SLA workflows and quote-to-invoice integration. |
| [Kantata](https://www.kantata.com/psa) | Integrated delivery, resource management, financial tracking, invoicing, revenue risk and profitability. |
| [Certinia](https://www.certinia.com/solutions/professional-services-cloud/) | Services estimation, skills/availability staffing, delivery, billing/financials, AI project and meeting assistance. |
| [Teamwork.com](https://www.teamwork.com/) | Client work intake, project delivery, resource forecasting, utilization, personal calendar and performance reporting. |

## FlowHive capability assessment

“Existing” means source is present; it does not substitute for production acceptance.
“In this PR” means implementation and regression coverage are included, with live
integration acceptance still required. “Gap” means parity must not be claimed.

| Capability | Existing integration / PR state | Acceptance needed for parity |
| --- | --- | --- |
| Authoritative SOW → WBS | Existing Module 064 orchestration, strict executable builder, five phases, stable IDs, citations, reviewed regeneration | Real SOW deliverable coverage; no repeated generic tasks; bounded generation must not silently omit committed scope. |
| Tasks, dependencies, hierarchy | Existing WBS CRUD, assignments, effort, duration, status, phase rollup, dependency validation | Large-plan usability; consistent bulk edits, reusable templates, custom fields, deep subtasks and task history. |
| Schedule forecasting | Existing weekday dependency calculation, critical path, float, constraints and baselines | Gap: allocation-aware dates, holidays, PTO, cross-project reservations, resource leveling, baseline variance, what-if scenarios and explanations. |
| Team availability | In this PR: authorized Outlook free/busy, week/month navigation, member filter, partial-error/unknown states | Graph tenant acceptance; user working hours and local timezone; freshness and subscription lifecycle. Free time must not be treated as staffing capacity. |
| Proactive assignment/due notifications | In this PR: approved-WBS events, 3-day/due-day/overdue defaults, quiet hours, idempotency, stale-event recheck, existing Module 065 queue and delivery audit | Apply migration 112; validate provider and project/global policies with approved test identities. Add escalation/digests and channel preferences. |
| Teams collaboration | Module 065 event contract supplied by this PR; transport owned by concurrent Module 065 work | Gap: per-channel delivery outcomes, tenant/app consent, authorized destination mapping, cards and safe action callbacks. Do not report Teams as connected from an email check. |
| PM / engineer oversight | In this PR: delivery overview, My work, overdue/blocked/unassigned/critical filters, scoped event history, AE and document visibility | Task-linked time entry, completion evidence, cross-project My Work, role-specific commercial and leadership summaries. |
| Time, expenses, financials | Existing authoritative modules and FlowHive financial readback/controls | Contract/rate-card linkage, billable vs nonbillable, EAC/ETC with provenance, margin and revenue forecasts, invoice/revenue handoff and reconciliation. Keep accounting authority outside FlowHive. |
| Commercial handoff | Existing Work Register / SOW and canonical project identities; AE visible in this PR | SOW revision impact, approved change orders, scope creep, contract commitments and acceptance-to-invoice traceability. |
| RAID / decisions / status | Existing RAID authority, immutable changes, status reports and branded exports | Owner escalation, approval workflow, consistent portfolio rollups, customer acceptance and closure evidence. |
| Meetings | Existing recording storage; live availability added by this PR | Gap: booking/invitations, recurrence, RSVP, rescheduling, meeting links, transcription and reviewed action-item extraction. |
| Portfolio management | Existing scoped project list; AE and visible-document counts in this PR | Portfolio staffing/forecast variance, demand intake, prioritization, scenario planning, utilization and program dependencies. |
| AI assistance | Existing cited plan generation and reviewed regeneration; existing summary helpers | Gap: grounded risk/status/blocker analysis across fresh execution data, estimate rationale, scope coverage and proposed changes with impact. No fabricated readiness or autonomous commitment changes. |
| Security and enterprise operations | Existing RBAC, project scope, View-As write fences, immutable versions, optimistic concurrency, Module 065 boundaries | Tenant role acceptance; retention/restore; accessible keyboard workflows; large-project load tests; delivery latency and operational failure dashboards. No certification claims. |
| Service desk / inventory / CRM | Owned by other system modules/integrations | Define project handoffs and shared identifiers. Replicating a ticketing system or ledger inside Module 066 would create conflicting authorities. |

## Integration decisions

- Module 066 owns project planning, approved WBS task events and project preferences.
- Module 064 owns AI routing and evidence retrieval; no duplicate provider stack.
- Module 065 owns email/Teams credentials, policy, channel transport and delivery audit.
- Canonical project, user and document IDs remain shared across modules. Calendar
  subjects/bodies are never used for AI prompts or returned by the availability API.
- Time, expenses, accounting and enterprise resource capacity remain authoritative
  in their existing modules. FlowHive forecasts consume those facts with freshness
  and provenance rather than copying mutable ledgers.
- Published-baseline notifications and editable-working-copy oversight are labeled
  separately. A draft edit is not a published customer commitment.

## Release sequence and exit criteria

1. **Workspace and reliable notifications (this PR):** green source, browser and
   PostgreSQL checks; exact file scope; tenant acceptance for calendar; controlled
   notification acceptance. No migration or outbound send is executed by opening the PR.
2. **Forecast engine:** real calendar/capacity inputs and snapshot provenance;
   deterministic reschedule of dependency networks; overload and uncertainty
   reporting; approved baseline comparison and isolated what-if plans. Test holiday,
   PTO, simultaneous projects, timezone, deadline, cycle and unschedulable cases.
3. **AI delivery coverage and assistance:** compare a representative set of real
   approved SOWs to WBS outputs; every deliverable maps to executable work or a PM
   exclusion; reviewed regeneration preserves actuals and manual work. Evaluate
   factuality, task completeness and estimate explanation, not just JSON validity.
4. **Complete PSA lifecycle:** task-to-time-to-cost-to-invoice traceability, commercial
   changes, approvals, customer acceptance, portfolio staffing and role journeys.
   Demonstrate PM, engineer, AE, SA, leadership and customer access independently.
5. **Enterprise readiness:** agree and measure concurrency, plan-size, response-time,
   worker-latency and recovery targets using realistic datasets. Verify accessibility,
   operational alerting and restore behavior before declaring general availability.

Feature parity is accepted per capability with evidence. A green PR validates its
implemented change set; it does not certify the entire backlog or live tenant setup.
