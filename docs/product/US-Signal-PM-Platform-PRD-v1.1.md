# US Signal — Professional Services Project Management Platform

## Product Requirements Document (PRD)

**Prepared for:** Development Team
**Prepared by:** Professional Services Manager, US Signal
**Date:** July 16, 2026
**Version:** 1.1 — Draft for Review

> Version 1.1 adds Time Tracking, Resource Capacity & Utilization, and the Estimate Accuracy Feedback Loop.

## 1. Document Purpose

This document defines product requirements for a new, purpose-built Project Management platform for US Signal's Professional Services organization. It is intended to guide the development team through discovery, design, and build.

This is a high-level PRD focused on features, user stories, and acceptance criteria. Detailed technical specifications, including data models, API contracts, and infrastructure, should be defined collaboratively with engineering after this document is reviewed and approved.

## 2. Problem Statement

US Signal's Professional Services team delivers infrastructure and network engagements, including Cisco SDA campus migrations, using a mix of disconnected tools and manual processes for expense tracking, change order management, translating an approved Statement of Work (SOW) into a project plan, and scheduling engineer time.

This creates:

- Duplicate data entry.
- Delayed visibility into project financials and scope changes.
- Inconsistent scheduling for a 15-person, highly-tenured Enterprise Network Team with an average of 12 years' experience.
- Inconsistent project closeout, including whether client feedback is collected.

The PMO consists of one Team Lead, two Senior Project Managers, and one Project Coordinator. It needs a single platform that connects SOW scope, project plan, schedule, cost, change control, and closure so projects are delivered on time, on budget, and through a consistent, auditable process from kickoff through client sign-off.

## 3. Goals

- Reduce the time required to convert an approved SOW into a working project plan by automating initial task identification from SOW text.
- Eliminate double-entry between project scheduling and Outlook calendars for the 15-person Enterprise Network Team.
- Give PMs and Professional Services leadership real-time visibility into project expenses against budget.
- Establish a consistent, auditable change order process tied to the project plan and budget.
- Standardize project closure so every engagement has documented deliverables and a completed stakeholder survey.
- Capture billable and non-billable engineer time against tasks so utilization and project cost are based on actual hours rather than estimates.
- Give PMs and leadership a real-time view of engineer capacity, utilization, and skills/certifications so staffing decisions are based on data rather than memory.
- Improve the accuracy of AI-generated task estimates over time by measuring estimated versus actual hours on completed work.

## 4. Non-Goals

- Replacing US Signal's financial ERP/accounting system. The platform tracks and reports project-level expenses, not payroll, AP/AR, or general ledger entries.
- Managing pre-sale CRM activity such as opportunities and quoting. The platform's scope begins at SOW hand-off from Sales.
- Full cross-team portfolio/resource-capacity forecasting beyond the Enterprise Network Team. This is a future consideration (P2).
- Native iOS or Android applications for V1. The platform will be a responsive web application.
- Auto-publishing AI-generated task lists or plans without human review. AI output always requires PM review and approval before becoming the plan of record.

## 5. Target Users and Personas

| Persona | Relationship to the Platform |
|---|---|
| Professional Services Manager | Executive oversight; portfolio-level reporting on budget, schedule, and closure health across all projects. |
| Senior Project Manager (x3) | Primary platform user; manages SOW-to-plan conversion, expenses, change orders, and closure for assigned projects. |
| Project Coordinator | Supports PMs with expense entry, engineer scheduling, and administrative tracking. |
| Enterprise Network Engineer (x15) | Receives task assignments and schedule through Outlook; time tracking requires platform access in V1. |
| Client Stakeholder (external) | Receives the post-project survey by email and requires no platform account. |

## 6. User Stories

### 6.1 Expense Tracking

- As a Project Coordinator, I want to log project expenses against a project budget so that spend is visible in real time.
- As a Senior PM, I want to see expenses versus budget for my projects at a glance so that I can flag overruns early.
- As a Professional Services Manager, I want a portfolio-wide expense dashboard so that I can see which projects are at financial risk.

### 6.2 Change Order Management

- As a Senior PM, I want to submit a change order against an active project so that scope, budget, and schedule changes are tracked with an approval trail.
- As a Professional Services Manager, I want to approve or reject change orders so that scope changes do not proceed without sign-off.
- As a Professional Services Manager, I want to see all pending and historical change orders so that I understand how project scope has evolved.

### 6.3 AI-Based Task Identification and Project Plan Generation

- As a Senior PM, I want to upload an approved SOW and receive an AI-generated list of tasks so that I do not have to manually transcribe scope into a project plan.
- As a Senior PM, I want to review, edit, and approve AI-suggested tasks before they become the official project plan.
- As a Senior PM, I want to add tasks manually that the AI parser did not capture.

### 6.4 Engineer Scheduling Through Outlook

- As a Project Coordinator, I want to schedule engineer time for project tasks directly onto their Outlook calendar.
- As an Enterprise Network Engineer, I want project task assignments to appear on my Outlook calendar with task details.
- As a Senior PM, I want to see engineer availability before assigning tasks so that I avoid double-booking.

### 6.5 Project Closure

- As a Senior PM, I want to track project deliverables so that I know what remains outstanding before closure.
- As a Professional Services Manager, I want a closure report showing final deliverables and expenses.
- As a Professional Services Manager, I want the platform to send a post-project survey automatically.
- As a Client Stakeholder, I want to receive and complete a short survey by email without a platform account.

### 6.6 Time Tracking

- As an Enterprise Network Engineer, I want to log hours against the specific task and project I worked on.
- As a Senior PM, I want to see logged hours versus estimated hours for each task.
- As a Professional Services Manager, I want to see billable versus non-billable hours by project and engineer.
- As a Project Coordinator, I want to remind engineers with missing or incomplete timesheets.

### 6.7 Resource Capacity and Utilization Management

- As a Senior PM, I want to see which engineers have the right skills/certifications and available capacity before assignment.
- As a Professional Services Manager, I want a utilization dashboard across the Enterprise Network Team.
- As a Professional Services Manager, I want visibility into upcoming certification expirations.
- As a Professional Services Manager, I want to forecast team capacity against the current project pipeline.

### 6.8 Estimate Accuracy Feedback Loop

- As a Senior PM, I want to see actual hours compared with AI-estimated hours on completed tasks.
- As a Professional Services Manager, I want estimate-accuracy trends by engagement type.
- As a Senior PM, I want AI-generated project plans to use historical actuals for similar task types.

## 7. Functional Requirements

The MoSCoW categories used in this PRD are:

- **P0:** Must-Have.
- **P1:** Nice-to-Have.
- **P2:** Future Consideration.

Modules 7.1 through 7.8 are treated as P0-level platform capabilities, with individual requirements prioritized below.

### 7.1 Expense Tracking Module

#### Must-Have (P0)

- [ ] Log an expense against a project with category, amount, date, submitter, and description.
- [ ] Attach a receipt or supporting document to an expense entry.
- [ ] Track budget versus actual spend by project, in total and by category, updated in real time.
- [ ] Require approval from the assigned PM or Team Lead before expenses count against reported actuals.
- [ ] Support configurable budget alert thresholds such as 80% and 100%.
- [ ] Export expense data by project and date range to CSV or Excel.

#### Nice-to-Have (P1)

- [ ] Support multi-currency project costs.
- [ ] Support recurring or scheduled expense templates.

### 7.2 Change Order Management Module

#### Must-Have (P0)

- [ ] Create a change order linked to an active project and affected SOW line items or tasks.
- [ ] Capture description, reason, cost impact, schedule impact, requested-by, and date.
- [ ] Support a configurable approval workflow, including fixed chains or dollar-threshold routing.
- [ ] On approval, update the project budget and project plan automatically.
- [ ] Generate a client-facing PDF for external signature or approval.
- [ ] Maintain complete per-project history and audit trail with draft, pending, approved, and rejected states.
- [ ] Notify internal stakeholders when a change order is approved or rejected.

#### Nice-to-Have (P1)

- [ ] Notify the client stakeholder automatically when an approved change affects cost or schedule.

### 7.3 AI-Based Task Identification and Project Plan Generation Module

#### Must-Have (P0)

- [ ] Accept an SOW/GSD upload in Word or PDF format, or pasted text.
- [ ] Use an AI/LLM process to propose tasks, estimated durations, dependencies, and milestones.
- [ ] Require PM review with edit, reorder, reassign, add, and remove controls before publication.
- [ ] On PM approval, generate the project plan with dependencies, milestones, owners, dates, and preferably a Gantt view.
- [ ] Support fully manual task creation and editing.
- [ ] Retain the source SOW with the generated plan and flag ambiguous language for PM review.

#### Nice-to-Have (P1)

- [ ] Suggest task owners based on required skills and availability/utilization.
- [ ] Re-run AI extraction against an amended SOW and show a proposed diff.

#### Future Consideration (P2)

- [ ] Learn from PM edits over time to improve future task extraction.

### 7.4 Engineer Scheduling Through Outlook Integration Module

#### Must-Have (P0)

- [ ] Integrate with Microsoft Graph to create, update, and remove Outlook calendar events from task assignments.
- [ ] Include task name, project name, description, and a platform link in calendar events.
- [ ] Synchronize reassignments, rescheduling, and cancellations automatically.
- [ ] Check engineer Outlook free/busy before assignment.
- [ ] Provide an in-platform team scheduling view of assignments and availability.

#### Nice-to-Have (P1)

- [ ] Allow engineers to propose a reschedule from Outlook for PM review.
- [ ] Account for recurring or blocked non-project calendar time.

### 7.5 Project Closure Module

#### Must-Have (P0)

- [ ] Track deliverable name, description, due date, and pending/delivered/accepted status.
- [ ] Require delivered/accepted status or an explicit override with justification before closure.
- [ ] Generate a closure summary with deliverables, final expenses, change history, and schedule variance.
- [ ] Generate and send a post-project survey automatically upon closure.
- [ ] Provide a configurable, reusable survey template.
- [ ] Store survey responses with the project and roll them into Professional Services reporting.

#### Nice-to-Have (P1)

- [ ] Send configurable reminders when the survey remains incomplete.
- [ ] Allow PM preview and controlled customization of the survey.

### 7.6 Time Tracking Module

#### Must-Have (P0)

- [ ] Allow engineers to log hours against a task and project with billable/non-billable designation.
- [ ] Support daily entry, weekly timesheet view, and responsive mobile-browser entry.
- [ ] Compare actual and estimated hours at task, project, and portfolio levels.
- [ ] Require timesheet submission and approval on a configurable cadence.
- [ ] Feed approved time into project cost and margin calculations with tracked expenses.
- [ ] Flag missing or late timesheets and notify the engineer and Project Coordinator.

#### Nice-to-Have (P1)

- [ ] Support start/stop timer entry.
- [ ] Support bulk entry across multiple days and tasks.

### 7.7 Resource Capacity and Utilization Management Module

#### Must-Have (P0)

- [ ] Maintain skills and certification profiles with expiration dates.
- [ ] Show capacity/availability and skills together during staffing.
- [ ] Provide a team utilization dashboard by engineer and week/month.
- [ ] Alert PMs and the Team Lead before certifications expire.
- [ ] Provide a capacity-versus-pipeline view.

#### Nice-to-Have (P1)

- [ ] Recommend best-fit engineers based on skills, certifications, and availability.
- [ ] Include non-billable internal commitments in capacity calculations.

### 7.8 Estimate Accuracy Feedback Loop Module

#### Must-Have (P0)

- [ ] Capture estimated and actual hours for every completed task.
- [ ] Report variance by task type and engagement type.
- [ ] Surface variance trends to PMs and the Professional Services Manager.
- [ ] Feed historical variance into future AI task generation.

#### Nice-to-Have (P1)

- [ ] Break variance reporting down by engineer/team as a coaching input.
- [ ] Provide Sales with historical actuals by engagement type.

## 8. Cross-Cutting Requirements

### 8.1 Roles and Permissions

| Role | Access Level |
|---|---|
| Professional Services Manager | Full visibility across all projects; portfolio reporting; approves change orders and project plans; no module restriction. |
| PMO Team Lead | Full access to own projects and assigned team's projects. |
| Senior Project Manager | Full access to own projects. |
| Project Coordinator | Expense entry and engineer scheduling on behalf of assigned PMs; read access to plans. |
| Enterprise Network Engineer | Logs time against assigned tasks; views own skills/certifications; no financial, change-order, or other-engineer access. |
| Client Stakeholder | No platform account; receives a unique unauthenticated survey link. |

### 8.2 Audit Trail

All changes to expenses, change orders, project plans, and schedules must be timestamped, attributed to the acting user, and retained for the life of the project plus a retention period to be defined with Finance and Legal.

### 8.3 Reporting and Dashboards

- Portfolio dashboard with active projects, budget health, schedule status, and at-risk flags.
- Individual project dashboard with plan, expenses, change orders, deliverables, and closure status.

### 8.4 Security and Data Handling

- Enforce role-based access control on all modules.
- Encrypt SOWs and financial data at rest and in transit.
- Prefer authentication through US Signal's Microsoft 365 tenant using SSO, subject to IT/Security confirmation.

## 9. Success Metrics

### Leading Indicators

| Metric | Target |
|---|---|
| Use of AI-generated task list as the plan starting point | 80% of new projects within 90 days of launch |
| Time from SOW approval to published project plan | 50% reduction versus the current manual process |
| Engineer schedule changes reflected in Outlook without manual re-entry | 100% |
| On-time timesheet submission rate | 95% or greater within the configured weekly window |
| Certification-expiration alerts actioned before lapse | 100% |

### Lagging Indicators

| Metric | Target |
|---|---|
| Post-project survey response rate | 70% or greater |
| Average client satisfaction score | Establish baseline in Q1 and track quarterly |
| Projects closed with complete deliverables and expense reconciliation | 95% or greater |
| Team utilization rate | Establish baseline in Q1 and set target with the Professional Services Manager |
| Estimate-versus-actual variance on AI-generated task estimates | Narrowing trend quarter over quarter by engagement type |

## 10. Open Questions

1. Should engineer accounts provide full self-service for own schedule and hours, or require additional approval steps?
2. Should change-order approvals use a fixed hierarchy or dollar-threshold routing?
3. Which AI/LLM provider and hosting model should be used, and what data-residency constraints apply?
4. Should multi-currency and recurring expense templates be included in V1?
5. Should closure surveys be native or integrate with an existing licensed survey tool?
6. Is Microsoft 365 SSO required at launch or acceptable as a phased capability?
7. What is the required timesheet cadence and approval chain?
8. Where does skills/certification data currently live, and can it be imported?
9. Should estimate-accuracy data be visible to Sales in V1?

## 11. Timeline Considerations and Phasing

| Phase | Scope |
|---|---|
| Phase 1 — Foundation | Core project/task data model, roles and permissions, expense tracking, time tracking, and manual project-plan creation. |
| Phase 2 — Scheduling and Change Control | Outlook/Graph scheduling, resource capacity/utilization, and change-order management. |
| Phase 3 — AI Plan Generation | AI SOW parsing and project-plan generation with PM review and approval. |
| Phase 4 — Closure and Feedback Loop | Deliverables, closure workflow, automated survey, and estimate-accuracy feedback into AI generation. |

No hard external deadline is specified. Target dates must be confirmed with the Professional Services Manager.

## 12. Glossary

| Term | Definition |
|---|---|
| SOW | Statement of Work — contractual document defining project scope, deliverables, and terms. |
| PMO | Project Management Office. |
| PS | Professional Services. |
| Change Order | Formally documented change to project scope, cost, or schedule after SOW approval. |
| Plan of Record | Approved current project plan used for scheduling and reporting. |

## Source Control Note

This Markdown document is the repository-readable canonical transcription of the source Word PRD, `US_Signal_PM_Platform_PRD.docx`, version 1.1 dated July 16, 2026. The original Word document should also be retained in the repository when its binary file is placed in the governed workspace.
