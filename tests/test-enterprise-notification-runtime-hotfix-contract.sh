#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BOOTSTRAP="$ROOT/src/backend/ProjectTime.Api/Modules/EnterpriseNotificationRuntimeBootstrap.cs"
ORCHESTRATOR="$ROOT/src/backend/ProjectTime.Api/Modules/EnterpriseNotificationOrchestrationService.cs"
REPOSITORY="$ROOT/src/backend/ProjectTime.Api/Modules/EnterpriseNotificationRepository.cs"
WORK_LIFECYCLE="$ROOT/src/backend/ProjectTime.Api/Modules/WorkLifecycleModule.cs"
ROLE_DASHBOARD="$ROOT/src/frontend/project-time-web/src/RoleWelcomeDashboard.jsx"
MIGRATION="$ROOT/database/migrations/065_enterprise_notification_runtime_completion.sql"
ROLLBACK="$ROOT/database/rollback/065_enterprise_notification_runtime_completion_rollback.sql"
PROGRAM="$ROOT/src/backend/ProjectTime.Api/Program.cs"

for file in "$BOOTSTRAP" "$ORCHESTRATOR" "$REPOSITORY" "$WORK_LIFECYCLE" "$ROLE_DASHBOARD" "$MIGRATION" "$ROLLBACK" "$PROGRAM"; do
  test -s "$file"
done

require() {
  local file="$1" marker="$2" label="$3"
  grep -Fq "$marker" "$file" || {
    echo "CONTRACT_FAILED $label missing marker: $marker" >&2
    exit 1
  }
  echo "CONTRACT_PASSED $label"
}

require "$PROGRAM" 'app.MapCrmErpIntegrationEndpoints();' program_invokes_bootstrap_hook
require "$BOOTSTRAP" 'MapCrmErpIntegrationEndpoints(this WebApplication app)' webapplication_overload_registered
require "$BOOTSTRAP" 'CrmErpIntegrationModule.MapCrmErpIntegrationEndpoints((IEndpointRouteBuilder)app);' existing_module026_routes_preserved
require "$BOOTSTRAP" '/api/enterprise-notifications/runtime/readiness' runtime_readiness_endpoint
require "$BOOTSTRAP" '/api/enterprise-notifications/runtime/run' authorized_manual_run_endpoint
require "$BOOTSTRAP" 'EnterpriseNotificationOrchestrationService.RunAsync' orchestration_service_reachable
require "$BOOTSTRAP" 'runType: "scheduled_worker"' scheduled_worker_registered
require "$BOOTSTRAP" 'IHostApplicationLifetime' application_lifetime_shutdown_boundary
require "$BOOTSTRAP" 'RUN_ENTERPRISE_NOTIFICATIONS_065' governed_run_permission
require "$BOOTSTRAP" 'RuntimeMigrationId = "065_enterprise_notification_runtime_completion"' runtime_requires_migration065
require "$BOOTSTRAP" 'GetField("TimeEntryRoles"' pm_time_entry_role_bridge
require "$BOOTSTRAP" 'GetField("TimeEntryExcludedRoles"' pm_time_entry_exclusion_bridge
require "$BOOTSTRAP" 'PROJECT_MANAGEMENT_TEAM_LEAD' pm_alias_coverage
require "$ORCHESTRATOR" 'await EnterpriseNotificationRepository.CompleteRunAsync' orchestration_completes_run_evidence
require "$REPOSITORY" 'UPDATE enterprise_notification_run_history' repository_completion_update_preserved
require "$WORK_LIFECYCLE" 'private static readonly HashSet<string> TimeEntryRoles' reflected_time_entry_field_exists
require "$WORK_LIFECYCLE" 'private static readonly HashSet<string> TimeEntryExcludedRoles' reflected_exclusion_field_exists
require "$ROLE_DASHBOARD" '...PROJECT_MANAGEMENT_ROLES' frontend_pm_time_entry_contract
require "$MIGRATION" "OLD.run_status <> 'running'" finalization_transition_guard
require "$MIGRATION" "NEW.run_status NOT IN ('completed', 'partial', 'failed')" final_status_allowlist
require "$MIGRATION" 'NEW.correlation_id IS DISTINCT FROM OLD.correlation_id' origin_evidence_immutable
require "$MIGRATION" "'065_enterprise_notification_runtime_completion'" migration_registered
require "$ROLLBACK" 'projectpulse064_block_enterprise_notification_evidence_mutation' rollback_restores_strict_guard

if grep -Fq 'QueueExpenseUploadAsync' "$ORCHESTRATOR"; then
  echo 'CONTRACT_PASSED expense_native_bridge_preserved'
else
  echo 'CONTRACT_FAILED expense_native_bridge_preserved' >&2
  exit 1
fi

if grep -Eq 'PROJECT_MANAGER|PROJECT_MANAGEMENT' "$ROLE_DASHBOARD"; then
  echo 'CONTRACT_PASSED frontend_project_management_aliases_present'
else
  echo 'CONTRACT_FAILED frontend_project_management_aliases_present' >&2
  exit 1
fi

echo 'ENTERPRISE_NOTIFICATION_RUNTIME_HOTFIX_CONTRACT=PASS'
