# Project Forge interactive workspace

This follow-up turns the 15 workbook-derived Project Forge tabs into one governed project-management workspace. Every view reads the same selected live project or the same explicitly selected review plan; draft and canonical tasks are never mixed on one board or calendar.

## Tab behavior

| Tab | Interactive behavior | System authority |
|---|---|---|
| Instructions | Explains Live Project, Review Plan, AI review, permissions, and Module 065 delivery | Module 033 contract and current access response |
| Setup | Shows the project team, workweek, statuses, priorities, and company holidays | Existing identity, assignment, and holiday records |
| Overall Dashboard | Shows the authorized portfolio and opens a selected project/task | Canonical project/task/time summaries |
| Monthly Calendar | Shows task spans, holidays, and bounded projected recurrence instances; opens the canonical task or reschedules an authorized canonical task by drag or date control | Forge schedule fields and non-persisted recurrence projection with revision checks |
| Weekly Calendar | Shows all work spanning each day, including clearly labeled projected recurrence instances; opens or reschedules an authorized canonical task | Same schedule and recurrence projection |
| Project Overview | Shows canonical project facts and opens the unified task editor | Existing project identity plus Forge task planning details |
| Project Manager | Shows the server-scoped PM portfolio and drills into one project | Existing PM relationship and governed team scope |
| Project Budget | Shows authorized estimates and current project-linked expense uploads without inventing approval state or currency conversion. Upload totals stay separated by recorded currency, and planned variance includes only uploads matching the governed project currency. | Existing project cost and expense authorities; read-only actuals |
| Variable Tasks | Creates, edits, assigns, archives, and links non-recurring tasks | Canonical tasks/assignments plus Forge planning details |
| Recurring Tasks | Creates and edits recurrence rules and previews bounded future dates without creating duplicate task rows | Forge recurrence rule |
| Tasks Schedule | Opens the date-ordered task editor and dependency controls | Holiday-aware schedule and dependency records |
| Tasks Filter | Searches and filters the same normalized task collection | Client projection of the selected workspace |
| Decision Matrix | Moves tasks among Do, Delegate, Decide, and Delete by drag or Move control | Atomic decision/importance/urgency fields |
| Kanban Board | Moves and reorders tasks across Backlog, Ready, In Progress, Review, Blocked, and Done | Atomic workflow and display-order fields |
| Gantt Chart | Opens, moves, or resizes tasks with day/week/month zoom and button alternatives | Same schedule fields and dependencies |

## Write guarantees

- Every mutation is re-authorized against the effective user and selected project on the server.
- Administrator View-As is read-only.
- Project Managers can manage only their own projects; Project Management Leads can select only PMs in their governed team scope; Super Administrators and Administrators can select all authorized PMs.
- Material writes require an expected revision and client mutation identifier. A stale write returns HTTP 409 and the browser reloads the authoritative row.
- Saving an existing task submits its changed details, workflow, schedule, and decision fields through one composite endpoint and one database transaction. Validation failure rolls the entire save back, with one audit record and one coalesced Module 065 update event on success.
- Closed, completed, archived, and cancelled projects reject writes.
- Tasks with time evidence are archived rather than deleted; a task with an active Module 001 timer cannot be archived.
- Dependency cycles, cross-project dependencies, invalid dates, and invalid Engineer assignments are rejected before commit.
- Daily, weekly, monthly, and yearly recurrence rules are projected only across the visible calendar range, respect active and end-date controls, stay bounded in the browser, and never materialize occurrence rows.
- An AI plan stays in the review workspace. Its assigned Engineer edits the task narrative, dates, duration, and estimate, then explicitly completes review. Adoption requires current review evidence for every AI task.
- Adoption creates canonical task, assignment, planning-detail, and dependency records once under a project lock.

## Notifications and AI

Module 033 writes idempotent enterprise notification events only. Module 065 owns recipient resolution and Microsoft 365 delivery, while Module 032 retains dispatch evidence. Assignment and review events are immediate; material task edits are coalesced for five minutes; order-only Kanban movement is audited but does not send an email.

Module 064 remains the provider authority for `project_forge_plan_estimate`. Project Forge uses authorized SOW, GSD, design, and supporting evidence through the private document-grounding path and never publishes or assigns an AI result automatically.

## Deliberately bounded follow-ups

This phase does not invent new authorities for project master data, approved expenses, rates, milestones, risks, or baselines. Per-occurrence recurrence exceptions, immutable schedule baselines, advanced critical-path/resource leveling, saved custom views, exports, comments, and real-time presence remain separate enhancements. Their absence does not make the workbook tabs read-only: task creation/editing, assignment, recurrence rules, dependencies, calendars, Decision Matrix, Kanban, and Gantt persistence are included here.
