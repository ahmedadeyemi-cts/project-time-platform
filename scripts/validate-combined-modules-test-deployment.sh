#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-combined-modules-test.yml"
APPLY="$ROOT/scripts/apply-combined-modules-test-migrations.sh"
RUNNER="$ROOT/scripts/run-combined-modules-test-migration-job.sh"
EXPECTED_RELEASE="0979c2bbd877b93fa112d22a43a748e105d958bb"

fail() { echo "ERROR: $*" >&2; exit 1; }
for file in "$WORKFLOW" "$APPLY" "$RUNNER"; do [[ -f "$file" ]] || fail "Missing deployment-control file: $file"; done

require() { local file="$1" text="$2"; grep -Fq -- "$text" "$file" || fail "Missing required contract in ${file#$ROOT/}: $text"; }
reject() { local file="$1" pattern="$2"; ! grep -Eiq -- "$pattern" "$file" || fail "Forbidden pattern in ${file#$ROOT/}: $pattern"; }

bash -n "$APPLY"
bash -n "$RUNNER"

require "$WORKFLOW" 'name: ProjectPulse Deploy Combined Modules 001 005 012 037 038 Test'
require "$WORKFLOW" 'workflow_dispatch:'
require "$WORKFLOW" "default: $EXPECTED_RELEASE"
require "$WORKFLOW" 'DEPLOY-COMBINED-MODULES-001-005-012-037-038-TO-TEST'
require "$WORKFLOW" 'environment: test'
require "$WORKFLOW" 'refs/heads/main'
require "$WORKFLOW" 'Check out exact combined release'
require "$WORKFLOW" 'release/database/migrations/$migration'
require "$WORKFLOW" '040_scoped_role_policy_versions.sql'
require "$WORKFLOW" '041_module_001_timesheet_timer_and_task_association.sql'
require "$WORKFLOW" '042_module_availability_controls.sql'
require "$WORKFLOW" '043_ptc_time_steward_permissions.sql'
require "$WORKFLOW" 'release/database/migrations/044_project_expense_upload_certify_connection.sql'
require "$WORKFLOW" 'release/database/migrations/044a_project_expense_self_certify_permission.sql'
require "$WORKFLOW" 'MapModule005ProjectExpenseUploadEndpointsSafe'
require "$WORKFLOW" 'DeleteUploadFromRequestAsync'
require "$WORKFLOW" 'Apply or verify combined database readiness and migrations 044 and 044a inside private network'
require "$WORKFLOW" 'COMBINED_MODULES_MIGRATION_JOB_NAME: cmbmods-${{ github.run_id }}-${{ github.run_attempt }}'
require "$WORKFLOW" '/health/combined-modules'
require "$WORKFLOW" '"status":"combined_module_runtime_ready"'
require "$WORKFLOW" '/api/project-expenses/readiness'
require "$WORKFLOW" '/api/runtime/v2/role-policy/summary'
require "$WORKFLOW" '/api/runtime/v2/role-policy/matrix'
require "$WORKFLOW" '/api/runtime/v2/timesheet/steward/users'
require "$WORKFLOW" '/api/project-expenses/context'
require "$WORKFLOW" '/api/certify/connection'
require "$WORKFLOW" 'cmbapi-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'cmbweb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'cmbapirb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'cmbwebrb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'Roll back API and web images on failure'
require "$WORKFLOW" 'combined-modules-test-deployment-${{ github.run_id }}-${{ github.run_attempt }}'

require "$APPLY" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$APPLY" '040_scoped_role_policy_versions'
require "$APPLY" '043_ptc_time_steward_permissions'
require "$APPLY" '044_project_expense_upload_certify_connection.sql'
require "$APPLY" '044a_project_expense_self_certify_permission.sql'
require "$APPLY" 'Migration checksum manifest must contain exactly 2 SQL files.'
require "$APPLY" 'Combined module preflight changed operational user, project, assignment, timesheet, or time-entry counts.'
require "$APPLY" 'Expected 70 active scoped modules; found %.'
require "$APPLY" 'Expected 12 active canonical roles; found %.'
require "$APPLY" 'No active eligible Engineering or Project Management users were found.'
require "$APPLY" 'No active Project Team Coordinator or Super Administrator operator exists.'
require "$APPLY" "to_jsonb(task)->>'work_task_category'"
require "$APPLY" "to_jsonb(task)->>'work_type'"
require "$APPLY" "to_jsonb(task)->>'service_request_number'"
require "$APPLY" 'The governed default Certify profile is missing or automatic sync is unsafe.'
require "$APPLY" 'COMBINED_MODULES_001_005_012_037_038_DATABASE=APPLIED_OR_VERIFIED'

require "$RUNNER" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$RUNNER" 'COMBINED_MODULES_MIGRATION_IMAGE'
require "$RUNNER" 'PROJECTPULSE_TEST_DATABASE_URL'
require "$RUNNER" 'EPHEMERAL_AZURE_TOKEN'
require "$RUNNER" 'az containerapp job delete'
require "$RUNNER" 'COMBINED_MODULES_MIGRATION_JOB_STATUS=Succeeded'

reject "$WORKFLOW" 'push:'
reject "$WORKFLOW" 'schedule:'
reject "$WORKFLOW" 'environment:[[:space:]]*production'
reject "$WORKFLOW" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$WORKFLOW" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$APPLY" 'task[.]task_classification'
reject "$APPLY" 'DROP[[:space:]]+TABLE|TRUNCATE[[:space:]]+TABLE|DELETE[[:space:]]+FROM[[:space:]]+(app_users|projects|project_assignments|timesheets|time_entries|project_tasks)'
reject "$RUNNER" 'registry-password[[:space:]]+[^"$]'

echo 'COMBINED_MODULES_TEST_DEPLOYMENT_GUARD=PASS'