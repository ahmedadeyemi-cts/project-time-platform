#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-module-005-project-expense-test.yml"
APPLY="$ROOT/scripts/apply-module-005-project-expense-test-migrations.sh"
RUNNER="$ROOT/scripts/run-module-005-project-expense-test-migration-job.sh"
EXPECTED_RELEASE="3b68790b7fa320d96b96158ab7414bad335bc767"

fail() { echo "ERROR: $*" >&2; exit 1; }
for file in "$WORKFLOW" "$APPLY" "$RUNNER"; do [[ -f "$file" ]] || fail "Missing deployment-control file: $file"; done

require() { local file="$1" text="$2"; grep -Fq -- "$text" "$file" || fail "Missing required contract in ${file#$ROOT/}: $text"; }
reject() { local file="$1" pattern="$2"; ! grep -Eiq -- "$pattern" "$file" || fail "Forbidden pattern in ${file#$ROOT/}: $pattern"; }

require "$WORKFLOW" 'name: ProjectPulse Deploy Module 005 Project Expense Upload Test'
require "$WORKFLOW" 'workflow_dispatch:'
require "$WORKFLOW" "default: $EXPECTED_RELEASE"
require "$WORKFLOW" 'DEPLOY-MODULE-005-PROJECT-EXPENSE-TO-TEST'
require "$WORKFLOW" 'environment: test'
require "$WORKFLOW" 'refs/heads/main'
require "$WORKFLOW" 'Check out exact Module 005 release'
require "$WORKFLOW" 'release/database/migrations/044_project_expense_upload_certify_connection.sql'
require "$WORKFLOW" 'release/database/migrations/044a_project_expense_self_certify_permission.sql'
require "$WORKFLOW" 'Apply or verify migrations 044 and 044a inside private network'
require "$WORKFLOW" 'MODULE_005_EXPENSE_MIGRATION_JOB_NAME: m005exp-${{ github.run_id }}-${{ github.run_attempt }}'
require "$WORKFLOW" 'm005api-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'm005web-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'm005apirb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'm005webrb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" '/api/project-expenses/context'
require "$WORKFLOW" '/api/project-expenses/uploads'
require "$WORKFLOW" '/api/certify/connection'
require "$WORKFLOW" 'Module 067 Global Mail Configuration'
require "$WORKFLOW" 'Roll back API and web images on failure'
require "$WORKFLOW" 'module-005-project-expense-test-deployment-${{ github.run_id }}-${{ github.run_attempt }}'

require "$APPLY" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$APPLY" '044_project_expense_upload_certify_connection.sql'
require "$APPLY" '044a_project_expense_self_certify_permission.sql'
require "$APPLY" 'Migration checksum manifest must contain exactly 2 SQL files.'
require "$APPLY" 'Module 005 migrations changed operational user, project, assignment, timesheet, or time-entry counts.'
require "$APPLY" 'Engineer Certify self-import permission is missing.'
require "$APPLY" 'Project Management on-behalf upload permission is missing.'
require "$APPLY" 'Accounting Certify connection permission is missing.'
require "$APPLY" 'MODULE_005_PROJECT_EXPENSE_MIGRATIONS=APPLIED_OR_VERIFIED'

require "$RUNNER" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$RUNNER" 'MODULE_005_EXPENSE_MIGRATION_IMAGE'
require "$RUNNER" 'PROJECTPULSE_TEST_DATABASE_URL'
require "$RUNNER" 'EPHEMERAL_AZURE_TOKEN'
require "$RUNNER" 'az containerapp job delete'
require "$RUNNER" 'MODULE_005_EXPENSE_MIGRATION_JOB_STATUS=Succeeded'

reject "$WORKFLOW" 'push:'
reject "$WORKFLOW" 'schedule:'
reject "$WORKFLOW" 'environment:[[:space:]]*production'
reject "$WORKFLOW" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$WORKFLOW" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$APPLY" 'DROP[[:space:]]+TABLE|TRUNCATE[[:space:]]+TABLE|DELETE[[:space:]]+FROM[[:space:]]+(app_users|projects|project_assignments|timesheets|time_entries)'
reject "$RUNNER" 'registry-password[[:space:]]+[^"$]'

echo 'MODULE_005_PROJECT_EXPENSE_TEST_DEPLOYMENT_GUARD=PASS'
