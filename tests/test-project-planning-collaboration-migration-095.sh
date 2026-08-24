#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MIGRATION="$ROOT/database/migrations/095_project_planning_collaboration_access.sql"
ROLLBACK="$ROOT/database/rollback/095_project_planning_collaboration_access_rollback.sql"
RESOLVER="$ROOT/src/backend/ProjectTime.Api/Modules/ProjectPlanningAccessResolver.cs"
FLOWHIVE="$ROOT/src/backend/ProjectTime.Api/Modules/ProjectFlowHiveEnterpriseModule.cs"
FORGE="$ROOT/src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs"
DEPLOYMENT="$ROOT/.github/workflows/projectpulse-deploy-test.yml"
RUNNER="$ROOT/scripts/release-test/run-project-planning-collaboration-migration-job.sh"

for file in "$MIGRATION" "$ROLLBACK" "$RESOLVER" "$FLOWHIVE" "$FORGE" "$DEPLOYMENT" "$RUNNER"; do
  test -f "$file" || { echo "Missing project planning collaboration artifact: $file" >&2; exit 1; }
done

for marker in \
  project_planning_collaborators \
  project_planning_collaboration_audit_events \
  VIEW_ASSOCIATED_FLOWHIVE_PROJECT_066 \
  EDIT_FLOWHIVE_PLANNER_066 \
  VIEW_ASSOCIATED_PROJECT_FORGE_033 \
  EDIT_PROJECT_FORGE_REVIEW_PLAN_033 \
  095_project_planning_collaboration_access; do
  grep -Fq "$marker" "$MIGRATION"
done

grep -Fq 'PROJECT_PLANNING_COLLABORATION_V1' "$RESOLVER"
grep -Fq 'associated_account_executive' "$RESOLVER"
grep -Fq 'associated_solution_architect' "$RESOLVER"
grep -Fq 'assigned_engineering_team_scope' "$RESOLVER"
grep -Fq 'Project Stakeholder — Read Only' "$RESOLVER"
! grep -Fq 'scoped_role_policy_modules' "$RESOLVER"
grep -Fq 'PreserveCollaboratorRestrictedFieldsAsync' "$FORGE"
grep -Fq 'RestrictNewCollaboratorPlan' "$FORGE"
grep -Fq 'FlowHiveAccessRequirement.EditPlanner' "$FLOWHIVE"
grep -Fq 'FlowHiveAccessRequirement.CustomerShare' "$FLOWHIVE"
grep -Fq '095_project_planning_collaboration_access.sql' "$DEPLOYMENT"
grep -Fq 'Apply and verify Migrations 086, 088, 093, 094, 095, and 096 inside Test private network' "$DEPLOYMENT"
grep -Fq '095-project-planning-collaboration' "$RUNNER"
grep -Fq 'projectpulse095_runtime_grants' "$MIGRATION"
grep -Fq "ARRAY['ptp_app', 'projectpulse_app']" "$MIGRATION"
grep -Fq 'IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = role_name)' "$MIGRATION"
! grep -Fq 'TO "ptp_app";' "$MIGRATION"
grep -Fq 'Rollback 095 refused: project planning collaborator assignments exist.' "$ROLLBACK"
grep -Fq 'immutable project planning collaboration audit evidence exists' "$ROLLBACK"

echo 'PROJECT_PLANNING_COLLABORATION_MIGRATION_095=PASS'
