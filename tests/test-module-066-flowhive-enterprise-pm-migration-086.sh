#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MIGRATION="$ROOT/database/migrations/086_module_066_flowhive_enterprise_pm.sql"
ROLLBACK="$ROOT/database/rollback/086_module_066_flowhive_enterprise_pm_rollback.sql"
BACKEND="$ROOT/src/backend/ProjectTime.Api/Modules/ProjectFlowHiveEnterpriseModule.cs"
FRONTEND="$ROOT/src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx"
HELPERS="$ROOT/src/frontend/project-time-web/src/flowhive-enterprise-helpers.js"

for file in "$MIGRATION" "$ROLLBACK" "$BACKEND" "$FRONTEND" "$HELPERS"; do
  test -f "$file" || { echo "Missing required FlowHive enterprise file: $file" >&2; exit 1; }
done

for table in \
  project_flowhive_working_copies \
  project_flowhive_project_controls \
  project_flowhive_raid_items \
  project_flowhive_status_reports \
  project_flowhive_customer_shares \
  project_flowhive_share_access_events; do
  grep -Fq "CREATE TABLE IF NOT EXISTS $table" "$MIGRATION"
done

grep -Fq "086_module_066_flowhive_enterprise_pm" "$MIGRATION"
grep -Fq "Rollback refused: Project FlowHive enterprise PM records exist." "$ROLLBACK"
grep -Fq "/api/project-flowhive/projects/{projectId:guid}/working-copy" "$BACKEND"
grep -Fq "/api/project-flowhive/projects/{projectId:guid}/controls" "$BACKEND"
grep -Fq "/api/project-flowhive/projects/{projectId:guid}/raid" "$BACKEND"
grep -Fq "/api/project-flowhive/projects/{projectId:guid}/status-reports" "$BACKEND"
grep -Fq "/api/project-flowhive/projects/{projectId:guid}/customer-shares" "$BACKEND"
grep -Fq "/api/project-flowhive/projects/{projectId:guid}/sow-evidence/{documentId:guid}/prepare" "$BACKEND"
grep -Fq "/api/project-flowhive/share/{token}" "$BACKEND"
grep -Fq "Only the assigned Project Manager can manage" "$BACKEND"
grep -Fq "deleteFlowHiveTask" "$HELPERS"
grep -Fq "moveFlowHiveTask" "$HELPERS"
grep -Fq "dependencyTypeHelp" "$HELPERS"
grep -Fq "deriveFlowHiveExecutiveSummary" "$HELPERS"

echo "MODULE_066_FLOWHIVE_ENTERPRISE_PM_MIGRATION_086=PASS"
