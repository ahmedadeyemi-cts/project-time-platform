# US Signal PM Platform — PRD Requirements Traceability Matrix

**Source PRD:** `docs/product/US-Signal-PM-Platform-PRD-v1.1.md`
**Source version:** 1.1, July 16, 2026
**Tracking principle:** A requirement is not marked complete based only on the presence of code. Completion requires source evidence, build/test evidence, authorization verification, and where applicable runtime evidence.

## Status Definitions

| Status | Meaning |
|---|---|
| Complete — verified | Requirement has source, test/build, and applicable runtime evidence. |
| Implemented locally | Source exists locally and builds pass, but it is not committed/pushed or runtime-verified. |
| Partially verified | Some acceptance criteria have evidence; remaining criteria are recorded. |
| Needs repository audit | Existing modules may address it, but no complete evidence package has been produced yet. |
| Environment/security verification required | Requires Azure, Entra, database, network, encryption, or operational evidence. |
| Open decision | Stakeholder decision is required before final acceptance criteria can be fixed. |
| Not yet assessed | No governed review has been completed. |
| Future consideration | P2 item outside the current V1 completion gate. |

## Current FIX-20260717-001 Coverage

| Item | Current status | Evidence | Remaining gate |
|---|---|---|---|
| Engineer draft save authorization | Complete — source verified | Foundation commit `b3d1cf11cc06d4a744605e7b611d73a8a029f2c7` | Runtime regression when deployment is authorized |
| One-hour engineer recall | Complete — source verified | Foundation commit `b3d1cf11cc06d4a744605e7b611d73a8a029f2c7` | Runtime boundary tests |
| Approval Center foundation | Complete — source/build verified | Foundation commit `b3d1cf11cc06d4a744605e7b611d73a8a029f2c7` | Runtime role-by-role test |
| PM approval inbox | Complete — committed and pushed | Commit `b7d173f3e138a86790d35609b4529cd981ffa22b` | Runtime PM/PTC authorization tests |
| PM rejection/return | Complete — committed and pushed | Commit `0cd4e0d64724db4ec6c2c11226f413dc53945fce` | Runtime state-transition and audit tests |
| PTC move correction | Verified locally — deep review passed | Authorization, invoice, status, conservation, audit safeguards and both builds verified | Commit, push, then runtime data-conservation tests |
| PTC split-copy correction | Verified locally — deep review passed | Total-hours conservation, source reduction, approval reset, audit safeguards, and both builds verified | Commit, push, then runtime conservation/approval-reset tests |
| PRD repository copy and traceability | Verified locally — coverage review passed | Sections 1–12, 67 requirements, 10 metrics, and 9 decisions verified | Commit and push |

## Functional and Cross-Cutting Requirements

| Requirement ID | PRD section | Priority | Requirement | Current status | Evidence or next verification |
|---|---:|---|---|---|---|
| EXP-P0-01 | 7.1 | P0 | Log expense with category, amount, date, submitter, and description. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| EXP-P0-02 | 7.1 | P0 | Attach receipt or supporting document. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| EXP-P0-03 | 7.1 | P0 | Real-time budget versus actual by project and category. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| EXP-P0-04 | 7.1 | P0 | Assigned PM or Team Lead expense approval. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| EXP-P0-05 | 7.1 | P0 | Configurable budget alert thresholds. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| EXP-P0-06 | 7.1 | P0 | CSV/Excel expense export by project and date. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| EXP-P1-01 | 7.1 | P1 | Multi-currency support. | Not yet assessed | Produce source map, acceptance tests, and runtime evidence before changing status. |
| EXP-P1-02 | 7.1 | P1 | Recurring expense templates. | Not yet assessed | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CO-P0-01 | 7.2 | P0 | Create project-linked change order with affected SOW/task links. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CO-P0-02 | 7.2 | P0 | Capture change description, reason, cost/schedule impact, requester, and date. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CO-P0-03 | 7.2 | P0 | Configurable approval workflow. | Open decision and repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CO-P0-04 | 7.2 | P0 | Approved change updates budget and project plan automatically. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CO-P0-05 | 7.2 | P0 | Generate client-facing PDF. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CO-P0-06 | 7.2 | P0 | Complete history/audit with lifecycle states. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CO-P0-07 | 7.2 | P0 | Internal approval/rejection notifications. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CO-P1-01 | 7.2 | P1 | Automatic client notification for approved cost/schedule changes. | Not yet assessed | Produce source map, acceptance tests, and runtime evidence before changing status. |
| AI-P0-01 | 7.3 | P0 | Accept Word/PDF SOW/GSD or pasted text. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| AI-P0-02 | 7.3 | P0 | AI task, duration, dependency, and milestone proposal. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| AI-P0-03 | 7.3 | P0 | PM review and editing before publication. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| AI-P0-04 | 7.3 | P0 | Approved plan with dependencies, milestones, owners, dates, and preferred Gantt. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| AI-P0-05 | 7.3 | P0 | Manual task creation/editing. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| AI-P0-06 | 7.3 | P0 | Source SOW retention and ambiguity flags. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| AI-P1-01 | 7.3 | P1 | Suggest owners from skills and availability. | Not yet assessed | Produce source map, acceptance tests, and runtime evidence before changing status. |
| AI-P1-02 | 7.3 | P1 | Amended-SOW re-extraction and diff. | Not yet assessed | Produce source map, acceptance tests, and runtime evidence before changing status. |
| AI-P2-01 | 7.3 | P2 | Learn from PM edits. | Future consideration | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CAL-P0-01 | 7.4 | P0 | Graph create/update/remove Outlook events. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CAL-P0-02 | 7.4 | P0 | Calendar event content and platform link. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CAL-P0-03 | 7.4 | P0 | Automatic reassignment/reschedule/cancellation sync. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CAL-P0-04 | 7.4 | P0 | Free/busy check before assignment. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CAL-P0-05 | 7.4 | P0 | In-platform team scheduling view. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CAL-P1-01 | 7.4 | P1 | Engineer-proposed Outlook reschedule. | Not yet assessed | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CAL-P1-02 | 7.4 | P1 | Recurring/blocked non-project calendar time. | Not yet assessed | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CLS-P0-01 | 7.5 | P0 | Deliverable tracking. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CLS-P0-02 | 7.5 | P0 | Closure guard or explicit override. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CLS-P0-03 | 7.5 | P0 | Closure summary with financial/change/schedule evidence. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CLS-P0-04 | 7.5 | P0 | Automatic post-project survey. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CLS-P0-05 | 7.5 | P0 | Reusable survey template. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CLS-P0-06 | 7.5 | P0 | Store responses and aggregate reporting. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CLS-P1-01 | 7.5 | P1 | Incomplete-survey reminders. | Not yet assessed | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CLS-P1-02 | 7.5 | P1 | Controlled PM survey customization. | Not yet assessed | Produce source map, acceptance tests, and runtime evidence before changing status. |
| TIME-P0-01 | 7.6 | P0 | Task/project time with billable designation. | Partially verified | Timesheet source, PM workflow, and PTC correction source provide partial evidence. Verify billable/non-billable behavior end to end. |
| TIME-P0-02 | 7.6 | P0 | Daily, weekly, and mobile-browser entry. | Partially verified | Daily and weekly source exists. Responsive mobile-browser acceptance test remains. |
| TIME-P0-03 | 7.6 | P0 | Actual versus estimated at task/project/portfolio levels. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| TIME-P0-04 | 7.6 | P0 | Configurable submission and approval cadence. | Partially verified | Submission, manager approval, PM approval, and rejection are source/build verified. Cadence configurability remains. |
| TIME-P0-05 | 7.6 | P0 | Approved time feeds cost and margin. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| TIME-P0-06 | 7.6 | P0 | Missing/late flagging and notifications. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| TIME-P1-01 | 7.6 | P1 | Timer entry. | Not yet assessed | Produce source map, acceptance tests, and runtime evidence before changing status. |
| TIME-P1-02 | 7.6 | P1 | Bulk time entry. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CAP-P0-01 | 7.7 | P0 | Skills/certification profiles and expirations. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CAP-P0-02 | 7.7 | P0 | Capacity and skills shown during staffing. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CAP-P0-03 | 7.7 | P0 | Team utilization dashboard. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CAP-P0-04 | 7.7 | P0 | Certification-expiration alerts. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CAP-P0-05 | 7.7 | P0 | Capacity-versus-pipeline view. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CAP-P1-01 | 7.7 | P1 | Best-fit engineer recommendations. | Not yet assessed | Produce source map, acceptance tests, and runtime evidence before changing status. |
| CAP-P1-02 | 7.7 | P1 | Non-billable commitments in capacity. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| EST-P0-01 | 7.8 | P0 | Estimated and actual hours for completed tasks. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| EST-P0-02 | 7.8 | P0 | Variance by task and engagement type. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| EST-P0-03 | 7.8 | P0 | Variance trend reporting. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| EST-P0-04 | 7.8 | P0 | Historical variance used by AI generation. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| EST-P1-01 | 7.8 | P1 | Variance by engineer/team as coaching input. | Not yet assessed | Produce source map, acceptance tests, and runtime evidence before changing status. |
| EST-P1-02 | 7.8 | P1 | Sales-facing historical actuals. | Not yet assessed | Produce source map, acceptance tests, and runtime evidence before changing status. |
| X-RBAC | 8.1 | Cross-cutting | Role-based module and data access. | Partially verified | Current timesheet, PM, and PTC routes contain explicit role/scope guards. Audit all modules against the PRD role table. |
| X-AUDIT | 8.2 | Cross-cutting | Timestamped actor-attributed retained audit evidence. | Partially verified | Current PM and PTC workflows write audit evidence. Retention and complete module coverage remain. |
| X-PORTFOLIO | 8.3 | Cross-cutting | Portfolio dashboard. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| X-PROJECT | 8.3 | Cross-cutting | Unified individual-project dashboard. | Needs repository audit | Produce source map, acceptance tests, and runtime evidence before changing status. |
| X-ENCRYPT | 8.4 | Cross-cutting | Encryption at rest and in transit. | Environment/security verification required | Validate storage encryption, TLS, document storage, secrets, backups, and evidence. |
| X-SSO | 8.4 | Cross-cutting | Microsoft 365 tenant SSO. | Repository and environment verification required | Validate Microsoft 365/Entra configuration and role mapping in the authorized environment. |

## Success Metric Tracking

| Metric ID | PRD target | Current status | Required instrumentation/evidence |
|---|---|---|---|
| MET-01 | AI task list used for 80% of new projects within 90 days | Not measured | Persist plan origin and adoption telemetry. |
| MET-02 | 50% reduction from SOW approval to published plan | Not measured | Capture SOW approval and plan publication timestamps; establish baseline. |
| MET-03 | 100% of schedule changes reflected in Outlook without re-entry | Not measured | Calendar sync success/failure telemetry and reconciliation report. |
| MET-04 | 95%+ on-time timesheet submission | Not measured | Configured cadence, due timestamps, submission timestamps, and reporting. |
| MET-05 | 100% of certification alerts actioned before lapse | Not measured | Alert event, acknowledgment/action, and expiration telemetry. |
| MET-06 | 70%+ post-project survey response rate | Not measured | Survey send, delivery, response, and reminder telemetry. |
| MET-07 | Client satisfaction baseline in Q1 and quarterly trend | Not measured | Standard scoring model and quarterly reporting. |
| MET-08 | 95%+ projects closed with complete deliverables and expense reconciliation | Not measured | Closure gate evidence and completeness report. |
| MET-09 | Utilization baseline and leadership target | Not measured | Available-hours policy, billable-hours calculation, exclusions, and dashboard. |
| MET-10 | Estimate variance narrows quarter over quarter | Not measured | Task/engagement classification and historical variance trend. |

## Open Decision Register

| Decision ID | PRD question | Status | Required owner |
|---|---|---|---|
| DEC-01 | Engineer self-service scope and approval steps | Partially resolved by current time-entry workflow; formal decision required | Professional Services Manager |
| DEC-02 | Fixed or dollar-threshold change-order approval | Open | Professional Services Manager / Finance |
| DEC-03 | AI provider, hosting, and data residency | Open | Engineering / Security |
| DEC-04 | Multi-currency and recurring expenses in V1 | Open | Professional Services Manager / Finance |
| DEC-05 | Native or third-party closure survey | Open | Professional Services Manager / Marketing |
| DEC-06 | SSO required at launch or phased | Open | IT / Security |
| DEC-07 | Timesheet cadence and approval chain | Partially implemented; formal policy required | Professional Services Manager / Finance |
| DEC-08 | Source and import method for skills/certifications | Open | Professional Services Manager / HR |
| DEC-09 | Sales access to estimate-accuracy data | Open | Professional Services Manager / Sales Leadership |

## Definition of Done for Each Requirement

A requirement can move to **Complete — verified** only when all applicable gates pass:

1. PRD acceptance criterion is unambiguous.
2. Source implementation is identified by file and route/component/schema.
3. Authorization is verified for allowed and denied roles.
4. Database behavior and migration state are verified when applicable.
5. Backend and frontend builds pass.
6. Automated or repeatable tests cover success, validation, authorization, concurrency, and immutable-state behavior.
7. Runtime evidence is collected in an authorized environment.
8. Audit evidence is validated.
9. Regression checks protect previously completed modules.
10. Commit, remote SHA, PR, deployment revision, and rollback evidence are recorded.

## Required Regression Scope

Every PRD checkpoint must preserve Modules 001, 042, 057, 058, 059, and 060 and must regress authentication, View-As, password reset, time entry, approval, audit, reporting, and any directly affected project/billing workflows.
