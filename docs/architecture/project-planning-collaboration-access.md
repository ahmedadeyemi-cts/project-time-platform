# Project Planning Collaboration Access

Policy version: `PROJECT_PLANNING_COLLABORATION_V1`

## Purpose

Module catalog ownership is descriptive accountability metadata. It does not
provide module or project access. Project Planning access for Module 033
Project Forge and Module 066 Project FlowHive is derived from the effective
user's relationship to each selected project and from capability-level RBAC.

## Functional ownership

Project Managers and Project Manager Leads remain the functional owners of the
planning process. They retain control over final baselines, canonical adoption,
financial controls, customer sharing, and project-planning administration.

## Project scope

A user may see a project in FlowHive or Project Forge only when at least one of
the following is true:

1. The user is the assigned Project Manager.
2. The user is a Project Manager Lead whose governed reporting scope includes
   the assigned Project Manager.
3. The user has an active `project_planning_collaborators` assignment.
4. The user is an Engineer with an active assignment on a task in the project.
5. The user is an Engineering Lead responsible for an actively assigned
   engineer on the project.
6. The user is the project's recorded Account Executive.
7. The user is the project's recorded Solution Architect.
8. The user has governed Super Administrator support authority.

The resolver denies access when none of these relationships exists. A module
owner assignment is never evaluated.

## Capability matrix

| Actor | Project scope | View | Review | Edit planner | Administer | Canonical adoption | Financial controls | Customer sharing |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| Project Manager | Assigned projects | Yes | Yes | Yes | Yes | Yes | Permission-gated | Permission-gated |
| Project Manager Lead | Assigned PM portfolio or explicit collaboration | Yes | Yes | Yes | Yes | Yes | Permission-gated | Permission-gated |
| Engineering Lead | Assigned-team projects or explicit collaboration | Yes | Yes | Yes | No | No | No | No |
| Engineer | Actively assigned projects or explicit collaboration | Yes | Yes | Yes | No | No | No | No |
| Account Executive | Recorded AE projects | Yes | No | No | No | No | No | No |
| Solution Architect | Recorded SA projects | Yes | No | No | No | No | No | No |
| View-As | Effective user's project scope | Yes | Yes when normally allowed | No | No | No | No | No |

## Planner-edit boundary

Engineering planner edits include review-plan tasks, technical descriptions,
estimates, durations, technical dependencies, acceptance criteria, validation
steps, notes, risks, and assigned review completion. Engineering planner access
does not authorize:

- contract or financial control changes;
- customer-share creation or revocation;
- final baseline approval;
- adoption into canonical tasks;
- unrestricted project or canonical-task deletion;
- unrestricted assignment administration.

## Shared server authority

`ProjectPlanningAccessResolver` returns server-derived capabilities and a
`scopeReason` for a project. FlowHive and Project Forge must use this resolver
for both list queries and individual project endpoints. Frontend role-name
checks are presentation helpers only and may not broaden the server response.

## Collaboration administration

Project Managers and authorized Project Manager Leads can create, revise, and
deactivate explicit project-planning collaborator assignments. Every change is
revision-controlled and creates immutable evidence containing the project,
collaborator, actor, prior state, new state, reason, and timestamp.

## Required acceptance tests

- An assigned Engineer can view and edit Project A's planner.
- The same Engineer cannot view Project B.
- An Engineering Lead can view and edit only projects in assigned team scope.
- An AE can view only projects where `account_executive_user_id` matches.
- An SA can view only projects where `solution_architect_user_id` matches.
- AE and SA cannot perform planner writes.
- Engineers cannot change FlowHive financial controls or customer shares.
- Engineers cannot adopt Project Forge plans into canonical tasks.
- Assigned PMs retain the complete PM governance boundary.
- PM Leads cannot access projects outside their assigned PM scope.
- View-As is read-only for every write endpoint.
- Module owner identity never grants project access.
