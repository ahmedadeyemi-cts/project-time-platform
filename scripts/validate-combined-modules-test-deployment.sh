#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-combined-modules-test.yml"
APPLY="$ROOT/scripts/apply-combined-modules-test-migrations.sh"
RUNNER="$ROOT/scripts/run-combined-modules-test-migration-job.sh"
EXPECTED_RELEASE="68deed805d99088f6432eb5cea28663a003e4953"

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
require "$WORKFLOW" '/api/public/combined-modules/readiness'
require "$WORKFLOW" 'combined-modules-001-005-012-037-038-public-v1'
require "$WORKFLOW" '/api/public/project-expenses/readiness'
require "$WORKFLOW" 'project-expense-certify-public-v1'
require "$WORKFLOW" 'operationalCountsReturned = false'
require "$WORKFLOW" "'/api/runtime/v2/timesheet/steward/users': '/api/timesheet/ptc/users'"
require "$WORKFLOW" "replace(/\/workspace$/, '/entries')"
require "$WORKFLOW" 'allActiveUsersAllowed: true'
require "$WORKFLOW" 'function synchronizeViewButtons()'
require "$WORKFLOW" 'const assignedTasks = mergeByKey(snapshot.assignedTasks, authoritativeAssignments);'
require "$WORKFLOW" 'const nonProjectCategories = mergeByKey('
require "$WORKFLOW" "grep -Fq 'Ready to start' release/src/frontend/project-time-web/src/module001/TimesheetTimerView.jsx"
require "$WORKFLOW" "grep -Fq 'Select Start timer to begin the live clock.' release/src/frontend/project-time-web/src/module001/TimesheetTimerView.jsx"
require "$WORKFLOW" 'All active users'
require "$WORKFLOW" "button.textContent = 'Re-upload'"
require "$WORKFLOW" 'Re-upload ready. Choose the replacement CSV or Excel file'
require "$WORKFLOW" "const MODULE005_NAME = 'Project Expense Upload'"
require "$WORKFLOW" 'certify-sync-control-card'
require "$WORKFLOW" 'Test connection to unlock'
require "$WORKFLOW" 'publicReadinessContracts=ready'
require "$WORKFLOW" 'protectedAuthBoundary=ready'
require "$WORKFLOW" 'operationalCountsSuppressed=true'
require "$WORKFLOW" 'Apply or verify combined database readiness and migrations 044 and 044a inside private network'
require "$WORKFLOW" 'COMBINED_MODULES_MIGRATION_JOB_NAME: cmbmods-${{ github.run_id }}-${{ github.run_attempt }}'
require "$WORKFLOW" '$BASE/api/public/combined-modules/readiness'
require "$WORKFLOW" '.status == "combined_module_runtime_ready"'
require "$WORKFLOW" '.contractVersion == "combined-modules-001-005-012-037-038-public-v1"'
require "$WORKFLOW" '.roleContractReady == true'
require "$WORKFLOW" '.moduleContractReady == true'
require "$WORKFLOW" '.publishedPolicyReady == true'
require "$WORKFLOW" '.grantContractReady == true'
require "$WORKFLOW" '.eligibleUserContractReady == true'
require "$WORKFLOW" '.operatorContractReady == true'
require "$WORKFLOW" '.foundationalMigrationsReady == true'
require "$WORKFLOW" '.expenseMigrationsReady == true'
require "$WORKFLOW" '.expenseTablesReady == true'
require "$WORKFLOW" '.nonProjectCategoriesReady == true'
require "$WORKFLOW" '.operationalCountsReturned == false'
require "$WORKFLOW" '$BASE/api/public/project-expenses/readiness'
require "$WORKFLOW" '.status == "project_expense_runtime_ready"'
require "$WORKFLOW" '.contractVersion == "project-expense-certify-public-v1"'
require "$WORKFLOW" '.migrationContractReady == true'
require "$WORKFLOW" '.tableContractReady == true'
require "$WORKFLOW" '.safeProfileReady == true'
require "$WORKFLOW" '.permissionContractReady == true'
require "$WORKFLOW" '.automaticSyncEnabled == false'
require "$WORKFLOW" '.secretsReturned == false'
require "$WORKFLOW" 'unexpectedly required authentication'
require "$WORKFLOW" '/api/runtime/v2/role-policy/summary'
require "$WORKFLOW" '/api/runtime/v2/role-policy/matrix'
require "$WORKFLOW" '/api/runtime/v2/timesheet/steward/users'
require "$WORKFLOW" '/api/project-expenses/context'
require "$WORKFLOW" '/api/certify/connection'
require "$WORKFLOW" 'wait_json_contract()'
require "$WORKFLOW" 'release_check=cmb-api-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}-${attempt}'
require "$WORKFLOW" "jq -e \"\$filter\""
require "$WORKFLOW" 'COMBINED_MODULES_LIVE_API_CONTRACT=READY'
require "$WORKFLOW" 'Capture combined API validation diagnostics'
require "$WORKFLOW" 'Upload combined API validation diagnostics'
require "$WORKFLOW" 'combined-api-validation-${{ github.run_id }}-${{ github.run_attempt }}'
require "$WORKFLOW" 'latestReadyRevisionName'
require "$WORKFLOW" 'cmbapi-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'cmbweb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'cmbapirb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'cmbwebrb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" "if [[ '\${{ steps.deploy_api.outputs.started }}' == 'true' ]]; then"
require "$WORKFLOW" "if [[ '\${{ steps.deploy_web.outputs.started }}' == 'true' ]]; then"
require "$WORKFLOW" 'Roll back API and web images on failure'
require "$WORKFLOW" 'Combined UAT repair release markers were not served.'
require "$WORKFLOW" '"uatRepairModules":["001","005","038"]'
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
require "$APPLY" 'Expected exactly one published policy; found %.'
require "$APPLY" 'The published permission matrix contains no effective grants.'
require "$APPLY" 'No active eligible Engineering or Project Management users were found.'
require "$APPLY" 'No active Project Team Coordinator or Super Administrator operator exists.'
require "$APPLY" "to_jsonb(task)->>'work_task_category'"
require "$APPLY" "to_jsonb(task)->>'work_type'"
require "$APPLY" "to_jsonb(task)->>'service_request_number'"
require "$APPLY" 'The governed default Certify profile is missing or automatic sync is unsafe.'
require "$APPLY" 'Module 005/038 permissions are incomplete.'
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
reject "$WORKFLOW" 'grep[[:space:]]+-Fq[[:space:]]+.*combined_module_runtime_ready'
reject "$WORKFLOW" 'grep[[:space:]]+-Fq[[:space:]]+.*project_expense_runtime_ready'
reject "$WORKFLOW" 'application/json[*].*return[[:space:]]+1'
reject "$WORKFLOW" 'combined-readiness.*[$]BASE/api/runtime/v2/readiness'
reject "$WORKFLOW" 'combined-readiness.*[$]BASE/health/combined-modules'
reject "$WORKFLOW" 'project-expense-readiness.*[$]BASE/api/project-expenses/readiness'
reject "$WORKFLOW" '[.]roleCount[[:space:]]*=='
reject "$WORKFLOW" '[.]moduleCount[[:space:]]*=='
reject "$WORKFLOW" '[.]migrationCount[[:space:]]*=='
reject "$WORKFLOW" '[.]tableCount[[:space:]]*=='
reject "$WORKFLOW" '[.]permissionCount[[:space:]]*=='
reject "$WORKFLOW" 'The server returned 0 eligible users[.]'
reject "$APPLY" 'task[.]task_classification'
reject "$APPLY" 'DROP[[:space:]]+TABLE|TRUNCATE[[:space:]]+TABLE|DELETE[[:space:]]+FROM[[:space:]]+(app_users|projects|project_assignments|timesheets|time_entries|project_tasks)'
reject "$RUNNER" 'registry-password[[:space:]]+[^"$]'

echo 'COMBINED_MODULES_TEST_DEPLOYMENT_GUARD=PASS'
