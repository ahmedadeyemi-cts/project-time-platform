#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-microsoft-integration-test.yml"
APPLY="$ROOT/scripts/apply-microsoft-integration-test-migration.sh"
RUNNER="$ROOT/scripts/run-microsoft-integration-test-migration-job.sh"
MIGRATION_TEST="$ROOT/tests/test-microsoft-integration-migration-045.sh"
EXPECTED_RELEASE="13a7b2bddd76026421f833841daf79340c973e18"

fail() { echo "ERROR: $*" >&2; exit 1; }
for file in "$WORKFLOW" "$APPLY" "$RUNNER" "$MIGRATION_TEST"; do
  [[ -f "$file" ]] || fail "Missing deployment-control file: ${file#$ROOT/}"
done
require() { grep -Fq -- "$2" "$1" || fail "Missing required contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }

bash -n "$APPLY"
bash -n "$RUNNER"
bash -n "$MIGRATION_TEST"

require "$WORKFLOW" 'name: ProjectPulse Deploy Microsoft Integration 010 065 Test'
require "$WORKFLOW" 'workflow_dispatch:'
require "$WORKFLOW" "default: $EXPECTED_RELEASE"
require "$WORKFLOW" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$WORKFLOW" 'DEPLOY-MICROSOFT-INTEGRATION-010-065-TO-TEST'
require "$WORKFLOW" 'environment: test'
require "$WORKFLOW" 'refs/heads/main'
require "$WORKFLOW" 'DISPATCH_RELEASE_COMMIT: ${{ inputs.release_commit }}'
require "$WORKFLOW" 'DISPATCH_CONFIRMATION: ${{ inputs.confirmation }}'
require "$WORKFLOW" 'WORKFLOW_SOURCE_REF: ${{ github.ref }}'
require "$WORKFLOW" 'WORKFLOW_SOURCE_SHA: ${{ github.sha }}'
require "$WORKFLOW" 'Only the verified Microsoft Integration release may deploy.'
require "$WORKFLOW" 'git -C control merge-base --is-ancestor'
require "$WORKFLOW" 'bash control/tests/test-microsoft-integration-migration-045.sh'
require "$WORKFLOW" '045_microsoft_integration_consolidation.sql'
require "$WORKFLOW" 'UseMicrosoftIntegrationSecurityCompatibility'
require "$WORKFLOW" 'client_selected_import_role_not_allowed'
require "$WORKFLOW" 'microsoft_integration_manage_access_required'
require "$WORKFLOW" 'HydrateEveryConfiguredTenantSecretAsync'
require "$WORKFLOW" 'Build checksum-pinned migration 045 image'
require "$WORKFLOW" 'Apply or verify migration 045 inside private network'
require "$WORKFLOW" 'MICROSOFT_INTEGRATION_MIGRATION_JOB_NAME: msint-${{ github.run_id }}-${{ github.run_attempt }}'
require "$WORKFLOW" 'Deploy Microsoft Integration API'
require "$WORKFLOW" 'Validate API startup and protected Microsoft Integration routes'
require "$WORKFLOW" '/api/microsoft-integration/overview'
require "$WORKFLOW" '/api/microsoft-integration/directory-users/import-selected'
require "$WORKFLOW" '/api/microsoft-integration/client-secret'
require "$WORKFLOW" '/api/microsoft-integration/test-connection'
require "$WORKFLOW" 'AUTHENTICATED_MICROSOFT_INTEGRATION_UAT=REQUIRED'
require "$WORKFLOW" 'Deploy Microsoft Integration web'
require "$WORKFLOW" 'Validate served Microsoft Integration assets and exact images'
require "$WORKFLOW" 'AUTHENTICATED_ENTRA_IMPORT_AND_GRAPH_UAT=REQUIRED'
require "$WORKFLOW" 'functionalUatStatus":"pending-user-session-validation"'
require "$WORKFLOW" 'Roll back API and web images on failure'
require "$WORKFLOW" "if [[ '\${{ steps.deploy_api.outputs.started }}' == 'true' ]]; then"
require "$WORKFLOW" "if [[ '\${{ steps.deploy_web.outputs.started }}' == 'true' ]]; then"
require "$WORKFLOW" 'msintapi-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'msintweb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'msintapirb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'msintwebrb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'microsoft-integration-010-065-test-${{ github.run_id }}-${{ github.run_attempt }}'

require "$APPLY" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$APPLY" 'Migration checksum manifest must contain exactly one SQL file.'
require "$APPLY" '045_microsoft_integration_consolidation'
require "$APPLY" 'Migration 045 changed operational user, role, or native-document counts.'
require "$APPLY" 'One or more Microsoft Integration tables are missing.'
require "$APPLY" 'Expected four Module 067 permission aliases'
require "$APPLY" 'Module 065 Microsoft Integration catalog state is incomplete.'
require "$APPLY" 'Module 067 was not retired non-destructively.'
require "$APPLY" 'Migration 045 unexpectedly created secret or audit records.'
require "$APPLY" 'MICROSOFT_INTEGRATION_DATABASE=APPLIED_OR_VERIFIED'

require "$RUNNER" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$RUNNER" 'MICROSOFT_INTEGRATION_MIGRATION_IMAGE'
require "$RUNNER" 'PROJECTPULSE_TEST_DATABASE_URL'
require "$RUNNER" 'EPHEMERAL_AZURE_TOKEN'
require "$RUNNER" 'az containerapp job delete'
require "$RUNNER" 'MICROSOFT_INTEGRATION_MIGRATION_JOB_STATUS=Succeeded'

require "$MIGRATION_TEST" 'MICROSOFT_INTEGRATION_MIGRATION_045_TEST=PASS'
require "$MIGRATION_TEST" 'migration_registered_once'
require "$MIGRATION_TEST" 'permission_aliases_created'
require "$MIGRATION_TEST" 'legacy_configuration_preserved'
require "$MIGRATION_TEST" 'secret_metadata_blocks_rollback'
require "$MIGRATION_TEST" 'audit_evidence_blocks_rollback'

reject "$WORKFLOW" 'push:'
reject "$WORKFLOW" 'schedule:'
reject "$WORKFLOW" 'environment:[[:space:]]*production'
reject "$WORKFLOW" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$WORKFLOW" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$WORKFLOW" 'automatic_sync_enabled[[:space:]]*=[[:space:]]*TRUE'
reject "$WORKFLOW" '\[\[[^\n]*\$\{\{[[:space:]]*inputs\.'
reject "$WORKFLOW" 'TARGET_COMMIT[^\n]*\$\{\{[[:space:]]*inputs\.'
reject "$WORKFLOW" 'AUTHENTICATED_MICROSOFT_INTEGRATION_UAT=PASS'
reject "$WORKFLOW" 'AUTHENTICATED_ENTRA_IMPORT_AND_GRAPH_UAT=PASS'
reject "$WORKFLOW" 'functionalUatStatus":"passed"'
reject "$WORKFLOW" 'psql[^\n]*database/rollback/045'
reject "$APPLY" 'DROP[[:space:]]+TABLE|TRUNCATE[[:space:]]+TABLE|DELETE[[:space:]]+FROM[[:space:]]+(app_users|app_roles|projectpulse_native_admin_documents)'
reject "$RUNNER" 'registry-password[[:space:]]+[^"$]'

INPUT_REFERENCE_COUNT="$(grep -Fc '${{ inputs.' "$WORKFLOW")"
[[ "$INPUT_REFERENCE_COUNT" == "3" ]] || fail "Expected exactly three non-shell input references; found $INPUT_REFERENCE_COUNT."

echo 'MICROSOFT_INTEGRATION_TEST_DEPLOYMENT_GUARD=PASS'
