#!/usr/bin/env bash
set -euo pipefail

# Keep validation portable across runner images. Prefer ripgrep when present;
# otherwise map only the option forms used below to GNU grep and reject
# unexpected invocations instead of weakening a source assertion.
if ! command -v rg >/dev/null 2>&1; then
  rg() {
    local option="${1:-}"
    shift || true
    case "$option" in
      -Fq) grep -Fq -- "$@" ;;
      -Fo) grep -Fo -- "$@" ;;
      -n) grep -En -- "$@" ;;
      -o) grep -Eo -- "$@" ;;
      *)
        echo "Unsupported rg compatibility invocation: $option" >&2
        return 2
        ;;
    esac
  }
fi

root="${1:-.}"
module="$root/src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs"
interactive="$root/src/backend/ProjectTime.Api/Modules/ProjectForgeInteractiveModule.cs"
contracts="$root/src/backend/ProjectTime.Api/Modules/ProjectForgeContracts.cs"
migration="$root/database/migrations/073_module_033_project_forge_interactive.sql"
rollback="$root/database/rollback/073_module_033_project_forge_interactive_rollback.sql"
test_sql="$root/scripts/tests/073_module_033_project_forge_interactive_migration_test.sql"
capability_routing="$root/src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs"
external_reasoning="$root/src/backend/ProjectTime.Api/Ai/CelarAiExternalReasoningService.cs"
enterprise_ai="$root/src/backend/ProjectTime.Api/Ai/CelarAiEnterprisePlatformService.cs"

for required in "$module" "$interactive" "$contracts" "$migration" "$rollback" "$test_sql" "$capability_routing" "$external_reasoning" "$enterprise_ai"; do
  test -s "$required" || { echo "Missing required Project Forge artifact: $required" >&2; exit 1; }
done

for route in \
  '/api/project-forge/projects/{projectId:guid}/tasks' \
  '/api/project-forge/tasks/{taskId:guid}/details' \
  '/api/project-forge/tasks/{taskId:guid}/workflow' \
  '/api/project-forge/tasks/{taskId:guid}/schedule' \
  '/api/project-forge/tasks/{taskId:guid}/decision' \
  '/api/project-forge/tasks/{taskId:guid}/composite' \
  '/api/project-forge/tasks/{taskId:guid}/assignee' \
  '/api/project-forge/projects/{projectId:guid}/task-dependencies' \
  '/api/project-forge/plans/{planId:guid}/tasks/{planTaskId:guid}/review-completion'; do
  rg -Fq "$route" "$module" || { echo "Missing Project Forge route: $route" >&2; exit 1; }
done

rg -Fq '[Microsoft.AspNetCore.Mvc.FromBody] ProjectForgeTaskArchiveRequest request' "$interactive"
rg -Fq '[Microsoft.AspNetCore.Mvc.FromBody] ProjectForgeTaskDependencySaveRequest request' "$interactive"
rg -Fq "@workspace='canonical'" "$module"
rg -Fq "@workspace='review_plan' AND pt.plan_id=@plan_filter" "$module"
rg -Fq 'projectTeam' "$module"
rg -Fq 'AS "assigneeUserId"' "$module"
rg -Fq 'canViewFinancials = CanViewFinancials' "$module"
rg -Fq "CASE WHEN @can_view_ai_citations THEN plan.ai_citations ELSE '[]'::jsonb END" "$module"
rg -Fq 'CASE WHEN @can_view_financials THEN COALESCE(p.planned_total_project_cost,0) ELSE NULL END' "$module"
rg -Fq "ELSE jsonb_build_object(" "$module"
rg -Fq "'financialDetailsRedacted',TRUE" "$module"
rg -Fq 'CASE WHEN @can_view_financials THEN audit.event_metadata' "$module"
rg -Fq 'EnsureProjectWritableAsync(connection, transaction, plan.Value.ProjectId' "$module"
rg -Fq 'reviewed_task_revision IS DISTINCT FROM task.revision_number' "$module"
rg -Fq 'project_task_dependencies' "$interactive"
rg -Fq 'module001_timer_sessions' "$interactive"
rg -Fq 'projectpulse073_add_working_days' "$interactive"
rg -Fq 'task_dependency_constraint_violation' "$interactive"
rg -Fq 'status = "task_composite_updated"' "$interactive"
rg -Fq '"TASK_COMPOSITE_UPDATED"' "$interactive"
rg -Fq 'composite_change_required' "$interactive"
rg -Fq 'scheduled_duration_requires_schedule_change' "$interactive"
rg -Fq 'review_plan_schedule_cascade_not_supported' "$interactive"
rg -Fq 'PreserveCompletedReviewRevisionsAsync' "$interactive"
rg -Fq "plan_status=CASE WHEN @invalidated THEN 'in_review' ELSE plan_status END" "$interactive"
test "$(rg -Fo 'planRevision, stateChanged = true' "$interactive" | wc -l)" -ge 5
rg -Fq 'await TouchPlanAsync(connection, transaction, changed.PlanId.Value' "$interactive"
rg -Fq 'PreserveCompletedReviewRevisionsAsync' "$interactive"
test "$(rg -Fo 'planRevision, stateChanged = true' "$interactive" | wc -l)" -ge 7
rg -Fq 'review_plan_schedule_cascade_not_supported' "$interactive"
rg -Fq 'EnsureProjectWritableAsync(connection, transaction, plan.Value.ProjectId, cancellationToken)' "$module"
rg -Fq 'EnsureProjectWritableAsync(connection, transaction, request.ProjectId, cancellationToken)' "$module"
create_method="$(awk '/private static async Task<IResult> CreateTaskAsync/{capture=1} /private static async Task<IResult> PatchTaskCompositeAsync/{capture=0} capture' "$interactive")"
printf '%s' "$create_method" | rg -Fq 'createDuration = await WorkingDayDurationAsync'
printf '%s' "$create_method" | rg -Fq 'AddWithValue("duration", Math.Clamp(createDuration, 0, 730))'
if printf '%s' "$create_method" | rg -Fq 'request.DurationWorkingDays != createDuration'; then
  echo 'Scheduled task creation must derive its authoritative duration without requiring the caller to precompute working days.' >&2
  exit 1
fi
composite_method="$(awk '/private static async Task<IResult> PatchTaskCompositeAsync/{capture=1} /private static async Task<int\?> LoadPlanRevisionAsync/{capture=0} capture' "$interactive")"
test "$(printf '%s' "$composite_method" | rg -Fo 'InsertTaskAuditAsync(' | wc -l)" -eq 1
test "$(printf '%s' "$composite_method" | rg -Fo 'InsertCoalescedTaskNotificationAsync(' | wc -l)" -eq 1
printf '%s' "$composite_method" | rg -Fq 'BeginTransactionAsync'
printf '%s' "$composite_method" | rg -Fq 'CommitAsync'
printf '%s' "$composite_method" | rg -Fq 'emitReviewerNotification: false'
test "$(rg -o 'var dependencyViolations = await LoadDependencyViolationsAsync' "$interactive" | wc -l)" -ge 3
test "$(rg -Fo 'projectpulse073_add_working_days(predecessor.planned_end_date,dependency.lag_working_days+1)' "$interactive" | wc -l)" -eq 2
rg -Fq 'task:{taskId}:created:{request.ClientMutationId}' "$interactive"
rg -Fq 'request.PercentComplete is < 0 or > 100' "$interactive"
rg -Fq 'blocked_reason,display_order' "$interactive"
rg -Fq 'own.task_id=te.task_id AND own.project_id=te.project_id' "$module"
rg -Fq 'assigned_plan_task.reviewer_user_id=@effective_user_id' "$module"
rg -Fq 'status = "task_schedule_endpoint_required"' "$module"
rg -Fq '["011", "033"]' "$capability_routing"
rg -Fq '? "033"' "$external_reasoning"
rg -Fq 'code = "project_forge_plan_estimate", owner = "Module 033", state = "document_grounded_review_draft"' "$enterprise_ai"
rg -Fq 'PROJECT_FORGE_TASK_UPDATE_BUCKET' "$interactive"
rg -Fq 'UPDATE_ASSIGNED_PROJECT_FORGE_TASK_STATUS_033' "$migration"
rg -Fq 'No sample projects, tasks, assignments, dependencies, people, or financial data are created.' "$migration"
rg -Fq 'Rollback refused: canonical Project Forge dependencies exist.' "$rollback"
rg -Fq "'TASK_ASSIGNEE_UPDATED'" "$rollback"
rg -Fq 'CHECK (duration_working_days IS NULL OR duration_working_days >= 0);' "$rollback"
rg -Fq 'ON CONFLICT(migration_id) DO NOTHING;' "$migration"
rg -Fq 'Rollback refused: interactive Project Forge audit evidence exists.' "$rollback"
rg -Fq 'BEGIN TRANSACTION READ ONLY;' "$test_sql"
rg -Fq 'MODULE_033_PROJECT_FORGE_INTERACTIVE_MIGRATION_073=PASS' "$test_sql"
rg -Fq "073_module_033_project_forge_interactive" "$migration"

if rg -n 'projectpulse07[12]|_07[12]|PROVISIONAL|provisional' "$migration" "$rollback" "$test_sql" "$interactive"; then
  echo 'Project Forge interactive artifacts must use finalized migration 073 identifiers.' >&2
  exit 1
fi

if rg -n "INSERT INTO (projects|app_users|project_tasks|project_assignments).*VALUES.*('Demo'|'Sample'|'Test Project')" "$migration"; then
  echo 'Project Forge migration must not create sample business data.' >&2
  exit 1
fi

echo 'MODULE033_PROJECT_FORGE_INTERACTIVE=PASS workspace=explicit concurrency=optimistic kanban=persistent schedule=holiday_aware dependencies=canonical review=versioned notifications=module065'
