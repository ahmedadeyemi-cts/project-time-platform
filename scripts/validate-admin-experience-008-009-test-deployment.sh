#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-admin-experience-008-009-test.yml"
VALIDATOR_WORKFLOW="$ROOT/.github/workflows/validate-admin-experience-008-009-test-deployment.yml"
APPLY="$ROOT/scripts/apply-admin-experience-008-009-test-migration.sh"
RUNNER="$ROOT/scripts/run-admin-experience-008-009-test-migration-job.sh"
EXPECTED_RELEASE="55ff9c3a07535ae7c7e2469cf69cdb075c51d1b3"

fail() { echo "ERROR: $*" >&2; exit 1; }
require_file() { [[ -f "$1" ]] || fail "Missing required file: ${1#$ROOT/}"; }
require() { grep -Fq -- "$2" "$1" || fail "Missing required contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }

for file in "$WORKFLOW" "$VALIDATOR_WORKFLOW" "$APPLY" "$RUNNER"; do
  require_file "$file"
done
bash -n "$APPLY"
bash -n "$RUNNER"

echo 'ADMIN_EXPERIENCE_DEPLOYMENT_SHELL_SYNTAX=PASS'

require "$WORKFLOW" 'name: ProjectPulse Deploy Admin Experience 008 009 Test'
require "$WORKFLOW" 'workflow_dispatch:'
require "$WORKFLOW" "default: $EXPECTED_RELEASE"
require "$WORKFLOW" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$WORKFLOW" 'DEPLOY-ADMIN-EXPERIENCE-008-009-TO-TEST'
require "$WORKFLOW" 'environment: test'
require "$WORKFLOW" 'refs/heads/main'
require "$WORKFLOW" 'DISPATCH_RELEASE_COMMIT: ${{ inputs.release_commit }}'
require "$WORKFLOW" 'DISPATCH_CONFIRMATION: ${{ inputs.confirmation }}'
require "$WORKFLOW" 'WORKFLOW_SOURCE_REF: ${{ github.ref }}'
require "$WORKFLOW" 'WORKFLOW_SOURCE_SHA: ${{ github.sha }}'
require "$WORKFLOW" 'Only the verified Modules 008 and 009 source release may deploy.'
require "$WORKFLOW" 'git -C control merge-base --is-ancestor'
require "$WORKFLOW" 'cancel-in-progress: false'
require "$WORKFLOW" 'projectpulse-deploy-admin-experience-008-009-test'

require "$WORKFLOW" '048_admin_audit_and_manager_team_scope.sql'
require "$WORKFLOW" 'projectpulse_system_audit_events'
require "$WORKFLOW" 'user_admin_manager_team_assignments'
require "$WORKFLOW" 'BEFORE UPDATE OR DELETE'
require "$WORKFLOW" 'ux_user_admin_one_active_manager_per_team'
require "$WORKFLOW" 'MapAdminAuditHistoryEndpoints'
require "$WORKFLOW" 'MapUserAdministrationTeamScopeEndpoints'
require "$WORKFLOW" '/api/admin/audit-history/events'
require "$WORKFLOW" '/api/admin/user-admin/manager-team-assignments'
require "$WORKFLOW" 'manager_email = @manager_email'
require "$WORKFLOW" 'ApplicationStarted'
require "$WORKFLOW" 'ApplicationStopping'
require "$WORKFLOW" 'Manage users'
require "$WORKFLOW" 'Manager team scope'
require "$WORKFLOW" 'Switch to dark mode'
require "$WORKFLOW" '.theme-toggle.projectpulse-theme-control'
require "$WORKFLOW" 'validate-modules-008-009-admin-experience.mjs'
require "$WORKFLOW" 'cd release/src/frontend/project-time-web'

require "$WORKFLOW" 'Verify database environment configuration'
require "$WORKFLOW" 'PTP_DB_HOST PTP_DB_PORT PTP_DB_NAME PTP_DB_USER PTP_DB_PASSWORD'
require "$WORKFLOW" 'Load test database connection'
require "$WORKFLOW" 'export-pr55-test-database-url.sh'
require "$WORKFLOW" 'Capture current immutable API and web images'
require "$WORKFLOW" 'Build immutable API and web candidates'
require "$WORKFLOW" 'Build checksum-pinned migration 048 image'
require "$WORKFLOW" 'deployment/containers/pr55-migrator/Dockerfile'
require "$WORKFLOW" 'sha256sum 048_admin_audit_and_manager_team_scope.sql > SHA256SUMS'
require "$WORKFLOW" 'Apply or verify migration 048 inside private network'
require "$WORKFLOW" 'run-admin-experience-008-009-test-migration-job.sh'
require "$WORKFLOW" 'ADMIN_EXPERIENCE_MIGRATION_IMAGE'
require "$WORKFLOW" 'ADMIN_EXPERIENCE_MIGRATION_JOB_NAME'
require "$WORKFLOW" "printf '%s\\n' '\${{ steps.release.outputs.target_commit }}' > \"\$CONTEXT/release-commit\""

require "$WORKFLOW" 'Deploy API candidate'
require "$WORKFLOW" 'Wait for exact API candidate revision'
require "$WORKFLOW" 'Validate protected Modules 008 and 009 API routes'
require "$WORKFLOW" 'audit-history GET'
require "$WORKFLOW" 'manager-scope GET'
require "$WORKFLOW" 'manager-scope-write PUT'
require "$WORKFLOW" 'AUTHENTICATED_ADMIN_EXPERIENCE_UAT=REQUIRED'
require "$WORKFLOW" 'Deploy web candidate'
require "$WORKFLOW" 'Wait for exact web candidate revision'
require "$WORKFLOW" 'Validate served Modules 008 and 009 assets'
require "$WORKFLOW" '.audit-event-card'
require "$WORKFLOW" '.user-admin-v2-tabs'
require "$WORKFLOW" 'ready_api_revision'
require "$WORKFLOW" 'ready_web_revision'
require "$WORKFLOW" 'ready_api_image'
require "$WORKFLOW" 'ready_web_image'

require "$WORKFLOW" '"deploymentType":"migration-api-web"'
require "$WORKFLOW" '"databaseMutation":true'
require "$WORKFLOW" '"migration":"048_admin_audit_and_manager_team_scope"'
require "$WORKFLOW" '"migrationStatus":"applied-or-verified"'
require "$WORKFLOW" '"functionalUatStatus":"pending-user-session-validation"'
require "$WORKFLOW" 'admin-experience-008-009-test-${{ github.run_id }}-${{ github.run_attempt }}'
require "$WORKFLOW" 'Roll back API and web images on failure'
require "$WORKFLOW" "if [[ '\${{ steps.deploy_api.outputs.started }}' == 'true' ]]; then"
require "$WORKFLOW" "if [[ '\${{ steps.deploy_web.outputs.started }}' == 'true' ]]; then"
require "$WORKFLOW" 'adexpapirb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'adexpwebrb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'ADMIN_EXPERIENCE_MIGRATION_ROLLBACK=NOT_ATTEMPTED_ADDITIVE_MIGRATION_REMAINS'

require "$APPLY" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$APPLY" 'PROJECTPULSE_TEST_DATABASE_URL is not configured.'
require "$APPLY" 'sha256sum --check --strict SHA256SUMS'
require "$APPLY" 'Migration checksum manifest must contain exactly one SQL file.'
require "$APPLY" '048_admin_audit_and_manager_team_scope.sql'
require "$APPLY" 'ADMIN_EXPERIENCE_MIGRATION=ALREADY_REGISTERED'
require "$APPLY" 'ADMIN_EXPERIENCE_MIGRATION=APPLYING'
require "$APPLY" 'Migration 048 is not registered exactly once.'
require "$APPLY" 'trg_projectpulse048_system_audit_immutable'
require "$APPLY" 'ux_user_admin_one_active_manager_per_team'
require "$APPLY" 'Migration 048 changed existing user profile, team, or manager data.'
require "$APPLY" 'Migration 048 changed Module 010 configuration.'
require "$APPLY" 'Migration 048 changed Module 065 or legacy Module 067 configuration.'
require "$APPLY" 'Migration 048 changed Microsoft Integration secret evidence.'
require "$APPLY" 'Migration 048 fabricated administrative audit events.'
require "$APPLY" 'Migration 048 fabricated manager-to-team assignments.'
require "$APPLY" 'ADMIN_EXPERIENCE_MIGRATION_048=APPLIED_OR_VERIFIED'

require "$RUNNER" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$RUNNER" 'ADMIN_EXPERIENCE_MIGRATION_IMAGE'
require "$RUNNER" 'ADMIN_EXPERIENCE_MIGRATION_JOB_NAME'
require "$RUNNER" 'The migration image must be an immutable digest from the approved ACR.'
require "$RUNNER" 'az acr login --name "$ACR_NAME" --expose-token'
require "$RUNNER" 'projectpulse-db-url=$DATABASE_URL'
require "$RUNNER" 'PROJECTPULSE_TEST_DATABASE_URL=secretref:projectpulse-db-url'
require "$RUNNER" 'projectpulse-scope=admin-experience-008-009-test'
require "$RUNNER" 'ADMIN_EXPERIENCE_MIGRATION_REGISTRY_AUTH=EPHEMERAL_AZURE_TOKEN'
require "$RUNNER" 'az containerapp job delete'
require "$RUNNER" 'ADMIN_EXPERIENCE_MIGRATION_JOB_CLEANUP=COMPLETE'
require "$RUNNER" 'ADMIN_EXPERIENCE_MIGRATION_JOB_STATUS=Succeeded'

require "$VALIDATOR_WORKFLOW" 'name: Validate Admin Experience 008 009 Test Deployment'
require "$VALIDATOR_WORKFLOW" 'push:'
require "$VALIDATOR_WORKFLOW" 'pull_request:'
require "$VALIDATOR_WORKFLOW" 'validate-admin-experience-008-009-test-deployment.sh'
require "$VALIDATOR_WORKFLOW" 'test-admin-experience-migration-048.sh'
require "$VALIDATOR_WORKFLOW" 'validate:modules008009'
require "$VALIDATOR_WORKFLOW" 'dotnet build'
require "$VALIDATOR_WORKFLOW" 'npm run build'
require "$VALIDATOR_WORKFLOW" 'admin-experience-008-009/deployment-controls'

reject "$WORKFLOW" 'push:'
reject "$WORKFLOW" 'schedule:'
reject "$WORKFLOW" 'environment:[[:space:]]*production'
reject "$WORKFLOW" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$WORKFLOW" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$WORKFLOW" 'automatic_sync_enabled[[:space:]]*=[[:space:]]*TRUE'
reject "$WORKFLOW" '\[\[[^\n]*\$\{\{[[:space:]]*inputs\.'
reject "$WORKFLOW" 'TARGET_COMMIT[^\n]*\$\{\{[[:space:]]*inputs\.'
reject "$WORKFLOW" 'psql[^\n]*database/rollback'
reject "$WORKFLOW" 'database/rollback[^\n]*--file'
reject "$WORKFLOW" 'AUTHENTICATED_ADMIN_EXPERIENCE_UAT=PASS'
reject "$WORKFLOW" 'functionalUatStatus":"passed"'
reject "$WORKFLOW" 'ACTIVE_API=.*properties[.]template[.]containers\[0\][.]image'
reject "$WORKFLOW" 'ACTIVE_WEB=.*properties[.]template[.]containers\[0\][.]image'
reject "$WORKFLOW" 'test[[:space:]]+-x[[:space:]]+control/scripts/wait-containerapp-ready-revision[.]sh'
reject "$WORKFLOW" '899399e43b7f054e4d151dd1ed241faaa05d7ef2|fb44072a172a54b04841f9136708b382a4a5ed03'

reject "$APPLY" 'DROP[[:space:]]+TABLE|TRUNCATE[[:space:]]+TABLE|DELETE[[:space:]]+FROM[[:space:]]+(app_users|app_roles|azure_entra_settings|projectpulse_native_admin_documents|microsoft_integration_client_secrets|microsoft_integration_sso_client_secrets|microsoft_integration_audit_events|projectpulse_system_audit_events|user_admin_manager_team_assignments)'
reject "$APPLY" 'database/rollback'
reject "$RUNNER" 'registry-password[[:space:]]+[^"$]'
reject "$RUNNER" 'environment:[[:space:]]*production'

INPUT_REFERENCE_COUNT="$(grep -Fc '${{ inputs.' "$WORKFLOW")"
[[ "$INPUT_REFERENCE_COUNT" == "3" ]] || fail "Expected exactly three non-shell input references; found $INPUT_REFERENCE_COUNT."

CONTROL_FILE_REFERENCE_COUNT="$(grep -Ec "^[[:space:]]+- '(\.github/workflows/projectpulse-deploy-admin-experience-008-009-test\.yml|\.github/workflows/validate-admin-experience-008-009-test-deployment\.yml|scripts/apply-admin-experience-008-009-test-migration\.sh|scripts/run-admin-experience-008-009-test-migration-job\.sh|scripts/validate-admin-experience-008-009-test-deployment\.sh)'$" "$VALIDATOR_WORKFLOW")"
[[ "$CONTROL_FILE_REFERENCE_COUNT" == "10" ]] || fail "Deployment validation workflow must reference the five deployment-control files once in paths and once in the exact-scope array."

echo 'ADMIN_EXPERIENCE_008_009_TEST_DEPLOYMENT_GUARD=PASS'
