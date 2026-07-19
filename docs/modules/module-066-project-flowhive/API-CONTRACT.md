# Module 066A — Project FlowHive API Contract

## Contract status

This contract defines the unregistered 066A read-only foundation. Registration in
`Program.cs` is intentionally deferred to the guarded integration phase.

## Authentication and identity

Both endpoints require a valid ProjectPulse session.

Identity resolution order:

1. `ProjectPulseEffectiveUserId`, when global View-As middleware established it;
2. `ProjectPulseSessionUserId` for the normal authenticated session.

The portfolio response includes actual and effective user identifiers so an
administrator View-As preview remains visible and read-only.

## `GET /api/project-flowhive/capabilities`

Returns the phase identity, enabled/disabled flags, dependencies, and evidence-based
capability statuses. This endpoint does not query or modify the database.

Required response fields:

- `module = "066"`
- `moduleName = "Project FlowHive"`
- `phase = "066A"`
- `status = "foundation_read_only"`
- `databaseMutationEnabled = false`
- `aiGenerationEnabled = false`
- `customerExportEnabled = false`
- `capabilities[]`
- `integration`

## `GET /api/project-flowhive/portfolio`

Returns canonical records filtered by backend role and assignment scope.

Top-level response fields:

- `module`
- `moduleName`
- `phase`
- `status`
- `mode`
- `access`
- `summary`
- `projects[]`
- `tasks[]`
- `assignments[]`
- `planningState`
- `guardrails[]`

### Project fields

- `projectId`
- `projectCode`
- `projectName`
- `customerName`
- `status`
- `startDate`
- `endDate`
- `projectManagerName`
- `taskCount`
- `assignmentCount`
- `source = "canonical_project"`

### Task fields

- `taskId`
- `projectId`
- `projectCode`
- `projectName`
- `taskCode`
- `taskName`
- `taskDescription`
- `billable`
- `assigneeCount`
- `assignedHours`
- `usedHours`
- `remainingHours`
- `structureSource = "canonical_task_code"`
- `isControlledWbs = false`

### Assignment fields

- `assignmentId`
- `projectId`
- `taskId`
- `projectCode`
- `projectName`
- `taskCode`
- `taskName`
- `resourceName`
- `effectiveStartDate`
- `effectiveEndDate`
- `allocationPercent`
- `assignedHours`

## Server authorization matrix

| Actor | Portfolio visibility | Task visibility | Assignment visibility |
|---|---|---|---|
| Engineer | Assigned projects | Assigned tasks or tasks under a project-level assignment | Own assignments |
| Project Manager | Managed and assigned projects | All tasks in managed/assigned scope | Assignments in managed projects |
| PM/Engineering Team Lead | Authorized team projects | All tasks in authorized team scope | Authorized team assignments |
| Project Team Coordinator | Broad business project scope | Broad business task scope | Broad business assignment scope |
| Administrator | Full module scope | Full module scope | Full module scope |
| Executive | Read-only organization scope | Read-only organization scope | Read-only organization scope |

Frontend filters never expand the server-returned dataset.

## Errors

| HTTP | Status/title | Meaning |
|---:|---|---|
| 401 | `session_required` | No valid ProjectPulse session context |
| 403 | `access_denied` | Effective user is not an active ProjectPulse user |
| 503 | `configuration_missing` | Required database configuration is unavailable |
| 500 | `Project FlowHive portfolio unavailable` | Generic server failure; exception details remain in server logs |

## Data authority

066A reads these existing sources:

- `app_users`
- `app_user_role_assignments`
- `app_roles`
- `reporting_relationships`
- `clients`
- `projects`
- `project_tasks`
- `project_assignments`
- `time_entries`

No 066A query inserts, updates, deletes, approves, or baselines records.

## Deferred contract changes

Write endpoints, planning IDs, hierarchy, dependencies, schedules, baselines,
collaboration, AI generation, sharing links, and artifact downloads require a new
versioned contract and separate authorization. They must not be added silently to
the 066A endpoints.
