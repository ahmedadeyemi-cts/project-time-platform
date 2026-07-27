#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-microsoft-integration-test.yml"
APPLY="$ROOT/scripts/apply-microsoft-integration-test-migration.sh"
RUNNER="$ROOT/scripts/run-microsoft-integration-test-migration-job.sh"
WAITER="$ROOT/scripts/wait-containerapp-ready-revision.sh"
TEST_045="$ROOT/tests/test-microsoft-integration-migration-045.sh"
TEST_046="$ROOT/tests/test-microsoft-sso-connection-migration-046.sh"
TEST_047="$ROOT/tests/test-microsoft-integration-connection-carryover-047.sh"
EXPECTED_RELEASE="c38edbb63f50bf736092e3f71c581eead5bdb13a"

fail() { echo "ERROR: $*" >&2; exit 1; }
for file in "$WORKFLOW" "$APPLY" "$RUNNER" "$WAITER" "$TEST_045" "$TEST_046" "$TEST_047"; do
  [[ -f "$file" ]] || fail "Missing deployment-control file: ${file#$ROOT/}"
done
require() { grep -Fq -- "$2" "$1" || fail "Missing required contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }

bash -n "$APPLY"
bash -n "$RUNNER"
bash -n "$WAITER"
bash -n "$TEST_045"
bash -n "$TEST_046"
bash -n "$TEST_047"

require "$WORKFLOW" 'name: ProjectPulse Deploy Microsoft Integration 010 065 Test'
require "$WORKFLOW" 'workflow_dispatch:'
require "$WORKFLOW" "default: $EXPECTED_RELEASE"
require "$WORKFLOW" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$WORKFLOW" 'DEPLOY-MICROSOFT-INTEGRATION-010-065-TO-TEST'
require "$WORKFLOW" 'environment: test'
require "$WORKFLOW" 'refs/heads/main'
require "$WORKFLOW" 'DISPATCH_RELEASE_COMMIT: ${{ inputs.release_commit }}'
require "$WORKFLOW" 'DISPATCH_CONFIRMATION: ${{ inputs.confirmation }}'
require "$WORKFLOW" 'Only the verified Microsoft Integration Connection release may deploy.'
require "$WORKFLOW" 'git -C control merge-base --is-ancestor'
require "$WORKFLOW" 'bash release/tests/test-microsoft-integration-migration-045.sh'
require "$WORKFLOW" 'bash release/tests/test-microsoft-sso-connection-migration-046.sh'
require "$WORKFLOW" 'bash release/tests/test-microsoft-integration-connection-carryover-047.sh'
require "$WORKFLOW" '045_microsoft_integration_consolidation.sql'
require "$WORKFLOW" '046_microsoft_sso_connection_profiles.sql'
require "$WORKFLOW" '047_microsoft_integration_connection_carryover.sql'
require "$WORKFLOW" "FROM azure_entra_settings settings"
require "$WORKFLOW" "module_number = '067'"
require "$WORKFLOW" "'module062IdentityProfile', 'services'"
require "$WORKFLOW" "'globalMailTransport', 'services'"
require "$WORKFLOW" "displayName: 'Microsoft Integration Connection'"
require "$WORKFLOW" '.entra-secret-center[data-module="065"]'
require "$WORKFLOW" '.azure-config-card, .azure-sync-summary-card'
require "$WORKFLOW" 'Microsoft 365 / SMTP'
require "$WORKFLOW" 'Module 062 identity/profile/presence'
require "$WORKFLOW" 'Build checksum-pinned migrations 045 046 047 image'
require "$WORKFLOW" 'Apply or verify migrations 045 046 047 inside private network'
require "$WORKFLOW" 'MICROSOFT_INTEGRATION_MIGRATION_JOB_NAME: msconn-${{ github.run_id }}-${{ github.run_attempt }}'
require "$WORKFLOW" 'wait-containerapp-ready-revision.sh'
require "$WORKFLOW" 'Deploy API candidate'
require "$WORKFLOW" 'Wait for exact API candidate revision'
require "$WORKFLOW" 'Validate protected Microsoft API routes'
require "$WORKFLOW" '/api/microsoft-integration/overview'
require "$WORKFLOW" '/api/microsoft-integration/directory-users/import-selected'
require "$WORKFLOW" '/api/microsoft-integration/client-secret'
require "$WORKFLOW" '/api/microsoft-integration/test-connection'
require "$WORKFLOW" '/api/microsoft-integration/sso-readiness'
require "$WORKFLOW" '/api/microsoft-integration/sso-client-secret'
require "$WORKFLOW" '/api/microsoft-integration/sso-test'
require "$WORKFLOW" '/api/microsoft-integration/sso-apply-profile'
require "$WORKFLOW" '{"tenantId":"00000000-0000-0000-0000-000000000000"}'
require "$WORKFLOW" 'Deploy web candidate'
require "$WORKFLOW" 'Wait for exact web candidate revision'
require "$WORKFLOW" 'Validate served authoritative Microsoft assets'
require "$WORKFLOW" 'Microsoft Integration Connection'
require "$WORKFLOW" 'data-microsoft-integration-authoritative'
require "$WORKFLOW" '.route-azure-admin .azure-config-card'
require "$WORKFLOW" '.route-azure-admin .azure-sync-summary-card'
require "$WORKFLOW" 'readyApiRevision'
require "$WORKFLOW" 'readyWebRevision'
require "$WORKFLOW" 'readyApiImage'
require "$WORKFLOW" 'readyWebImage'
require "$WORKFLOW" 'AUTHENTICATED_MICROSOFT_CONNECTION_UAT=REQUIRED'
require "$WORKFLOW" 'functionalUatStatus":"pending-user-session-validation"'
require "$WORKFLOW" '"secretMutation":false'
require "$WORKFLOW" 'Roll back API and web images on failure'
require "$WORKFLOW" "if [[ '\${{ steps.deploy_api.outputs.started }}' == 'true' ]]; then"
require "$WORKFLOW" "if [[ '\${{ steps.deploy_web.outputs.started }}' == 'true' ]]; then"
require "$WORKFLOW" 'msconnapi-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'msconnweb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'msconnapirb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'msconnwebrb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'microsoft-integration-connection-test-${{ github.run_id }}-${{ github.run_attempt }}'

require "$APPLY" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$APPLY" 'Migration checksum manifest must contain exactly three SQL files.'
require "$APPLY" '045_microsoft_integration_consolidation'
require "$APPLY" '046_microsoft_sso_connection_profiles'
require "$APPLY" '047_microsoft_integration_connection_carryover'
require "$APPLY" 'GRAPH_SECRET_ROWS_BEFORE'
require "$APPLY" 'SSO_SECRET_ROWS_BEFORE'
require "$APPLY" 'MODULE010_SOURCE_HASH_BEFORE'
require "$APPLY" 'MODULE067_SOURCE_HASH_BEFORE'
require "$APPLY" 'MODULE065_MARKER_BEFORE'
require "$APPLY" 'Migration 047 changed existing Graph or SSO secret evidence counts.'
require "$APPLY" 'Migration 047 changed or removed the Module 010 or Module 067 source configuration.'
require "$APPLY" 'Module 010 services client ID was not carried into Module 065.'
require "$APPLY" 'Module 067 sender address was not carried into Module 065.'
require "$APPLY" 'Module 065 Microsoft connection ownership metadata is incomplete.'
require "$APPLY" 'MICROSOFT_INTEGRATION_CONNECTION_DATABASE=APPLIED_OR_VERIFIED'

require "$RUNNER" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$RUNNER" 'MICROSOFT_INTEGRATION_MIGRATION_IMAGE'
require "$RUNNER" 'PROJECTPULSE_TEST_DATABASE_URL'
require "$RUNNER" 'EPHEMERAL_AZURE_TOKEN'
require "$RUNNER" 'az containerapp job delete'
require "$RUNNER" 'MICROSOFT_INTEGRATION_MIGRATION_JOB_STATUS=Succeeded'
require "$RUNNER" 'projectpulse-scope=microsoft-integration-connection-test'

require "$WAITER" 'properties.latestReadyRevisionName'
require "$WAITER" 'az containerapp revision show'
require "$WAITER" 'CONTAINERAPP_CANDIDATE_READY'
require "$WAITER" 'The expected Container Apps revision did not become the latest ready revision'

require "$TEST_045" 'MICROSOFT_INTEGRATION_MIGRATION_045_TEST=PASS'
require "$TEST_046" 'MICROSOFT_SSO_CONNECTION_MIGRATION_046_TEST=PASS'
require "$TEST_047" 'MICROSOFT_INTEGRATION_CONNECTION_CARRYOVER_047_TEST=PASS'
require "$TEST_047" 'module010_source_preserved'
require "$TEST_047" 'module067_source_preserved'
require "$TEST_047" 'graph_secret_preserved'
require "$TEST_047" 'sso_secret_preserved'
require "$TEST_047" 'module062_uses_services_connection'

reject "$WORKFLOW" 'push:'
reject "$WORKFLOW" 'schedule:'
reject "$WORKFLOW" 'environment:[[:space:]]*production'
reject "$WORKFLOW" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$WORKFLOW" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$WORKFLOW" 'automatic_sync_enabled[[:space:]]*=[[:space:]]*TRUE'
reject "$WORKFLOW" '\[\[[^\n]*\$\{\{[[:space:]]*inputs\.'
reject "$WORKFLOW" 'TARGET_COMMIT[^\n]*\$\{\{[[:space:]]*inputs\.'
reject "$WORKFLOW" 'AUTHENTICATED_MICROSOFT_CONNECTION_UAT=PASS'
reject "$WORKFLOW" 'functionalUatStatus":"passed"'
reject "$WORKFLOW" 'psql[^\n]*database/rollback/04[567]'
reject "$WORKFLOW" 'ACTIVE_API=.*properties[.]template[.]containers\[0\][.]image'
reject "$WORKFLOW" 'ACTIVE_WEB=.*properties[.]template[.]containers\[0\][.]image'
reject "$WORKFLOW" 'sso-test[^\n]*sso-test[^\n]*\{\}'
reject "$APPLY" 'DROP[[:space:]]+TABLE|TRUNCATE[[:space:]]+TABLE|DELETE[[:space:]]+FROM[[:space:]]+(app_users|app_roles|azure_entra_settings|projectpulse_native_admin_documents|microsoft_integration_client_secrets|microsoft_integration_sso_client_secrets|microsoft_integration_audit_events)'
reject "$RUNNER" 'registry-password[[:space:]]+[^"$]'

INPUT_REFERENCE_COUNT="$(grep -Fc '${{ inputs.' "$WORKFLOW")"
[[ "$INPUT_REFERENCE_COUNT" == "3" ]] || fail "Expected exactly three non-shell input references; found $INPUT_REFERENCE_COUNT."

echo 'MICROSOFT_INTEGRATION_CONNECTION_TEST_DEPLOYMENT_GUARD=PASS'
