# Module 001 authoritative task-source mapping

## Decision

My Work Queue must be a read projection over authoritative ProjectPulse project/task/assignment records. It must not create a duplicate task directory.

## Candidate source precedence

1. Durable task assignment records used by Work Task Builder / Project Workspace.
2. Project FlowHive assignments when they resolve to the same durable project and task identifiers.
3. Work Register project membership as project-level eligibility, not as a substitute for a task assignment.
4. Authorized non-project activities from the existing Timesheet activity catalog.

## Required normalized projection

Each queue item should expose: `customerId`, `customerName`, `projectId`, `projectCode`, `projectName`, `taskId`, `workItemId`, `assignmentId`, `activityTypeId`, `taskName`, `taskDescription`, `workType`, `assignedEngineerId`, `projectManagerId`, `projectManagerName`, `dueDate`, `status`, `estimatedHours`, `weekHours`, and `remainingHours` when available.

## Deduplication

Use the durable assignment identifier first. Fall back to project plus task/work-item plus authenticated user only when a legacy record has no assignment identifier. Never deduplicate solely by display name.

## Security

The backend derives the authenticated user from the session. Client-supplied user IDs do not expand scope. View-As remains read-only. Unassigned organization-wide tasks are excluded.

## Integration discovery still required

After PR #83 merges, inspect the current backend schemas and route registrations to select the exact repository/query source. This Phase 0 package intentionally avoids inventing a production API.
