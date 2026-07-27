#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-microsoft-integration-test.yml"
APPLY="$ROOT/scripts/apply-microsoft-integration-test-migration.sh"
RUNNER="$ROOT/scripts/run-microsoft-integration-test-migration-job.sh"
WAITER="$ROOT/scripts/wait-containerapp-ready-revision.sh"
TEST_045="$ROOT/tests/test-microsoft-integration-migration-045.sh"
TEST_046="$ROOT/tests/test-microsoft-sso-connection-migration-046.sh"
EXPECTED_RELEASE="1ac741b4c50ce10d73a3b1fb061bfa6fa4eb0d3d"

fail() { echo "ERROR: $*" >&2; exit 1; }
for file in "$WORKFLOW" "$APPLY" "$RUNNER" "$WAITER" "$TEST_045" "$TEST_046"; do
  [[ -f "$file" ]] || fail "Missing deployment-control file: ${file#$ROOT/}"
done
require() { grep -Fq -- "$2" "$1" || fail "Missing required contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }

bash -n "$APPLY"
bash -n "$RUNNER"
bash -n "$WAITER"
bash -n "$TEST_045"
bash -n "$TEST_046"

require "$WORKFLOW" 'name: ProjectPulse Deploy Microsoft Integration 010 065 Test'
require "$WORKFLOW" 'workflow_dispatch:'
require "$WORKFLOW" "default: $EXPECTED_RELEASE"
require "$WORKFLOW" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$WORKFLOW" 'DEPLOY-MICROSOFT-INTEGRATION-010-065-TO-TEST'
require "$WORKFLOW" 'environment: test'
require "$WORKFLOW" 'refs/heads/main'
require "$WORKFLOW" 'DISPATCH_RELEASE_COMMIT: ${{ inputs.release_commit }}'
require "$WORKFLOW" 'DISPATCH_CONFIRMATION: ${{ inputs.confirmation }}'
require "$WORKFLOW" 'Only the verified Microsoft dual-connection release may deploy.'
require "$WORKFLOW" 'git -C control merge-base --is-ancestor'
require "$WORKFLOW" 'bash release/tests/test-microsoft-integration-migration-045.sh'
require "$WORKFLOW" 'bash release/tests/test-microsoft-sso-connection-migration-046.sh'
require "$WORKFLOW" '045_microsoft_integration_consolidation.sql'
require "$WORKFLOW" '046_microsoft_sso_connection_profiles.sql'
require "$WORKFLOW" 'MapMicrosoftSsoConnectionProfileEndpoints'
require "$WORKFLOW" 'MapMicrosoftSsoRuntimeProfileEndpoints'
require "$WORKFLOW" 'UseMicrosoftSsoRuntimeCompatibility'
require "$WORKFLOW" 'MicrosoftAuthority(tenantGuid)'
require "$WORKFLOW" 'PROJECTPULSE_SSO_CLIENT_SECRET'
require "$WORKFLOW" 'Two independent connections'
require "$WORKFLOW" 'microsoft_services_enterprise_application'
require "$WORKFLOW" 'sso_app_registration'
require "$WORKFLOW" 'Build checksum-pinned migrations 045 and 046 image'
require "$WORKFLOW" 'Apply or verify migrations 045 and 046 inside private network'
require "$WORKFLOW" 'MICROSOFT_INTEGRATION_MIGRATION_JOB_NAME: msdual-${{ github.run_id }}-${{ github.run_attempt }}'
require "$WORKFLOW" 'wait-containerapp-ready-revision.sh'
require "$WORKFLOW" 'Deploy Microsoft dual-connection API'
require "$WORKFLOW" 'Wait for exact API candidate revision'
require "$WORKFLOW" 'Validate protected Microsoft dual-connection API routes'
require "$WORKFLOW" '/api/microsoft-integration/overview'
require "$WORKFLOW" '/api/microsoft-integration/directory-users/import-selected'
require "$WORKFLOW" '/api/microsoft-integration/client-secret'
require "$WORKFLOW" '/api/microsoft-integration/test-connection'
require "$WORKFLOW" '/api/microsoft-integration/sso-readiness'
require "$WORKFLOW" '/api/microsoft-integration/sso-client-secret'
require "$WORKFLOW" '/api/microsoft-integration/sso-test'
require "$WORKFLOW" '/api/microsoft-integration/sso-apply-profile'
require "$WORKFLOW" '{"tenantId":"00000000-0000-0000-0000-000000000000"}'
require "$WORKFLOW" 'AUTHENTICATED_MICROSOFT_DUAL_CONNECTIONS_UAT=REQUIRED'
require "$WORKFLOW" 'Deploy Microsoft dual-connection web'
require "$WORKFLOW" 'Wait for exact web candidate revision'
require "$WORKFLOW" 'Validate served dual-connection assets and ready images'
require "$WORKFLOW" 'candidate_api_revision'
require "$WORKFLOW" 'candidate_web_revision'
require "$WORKFLOW" 'readyApiRevision'
require "$WORKFLOW" 'readyWebRevision'
require "$WORKFLOW" 'readyApiImage'
require "$WORKFLOW" 'readyWebImage'
require "$WORKFLOW" 'AUTHENTICATED_TEST_AND_PRODUCTION_MICROSOFT_UAT=REQUIRED'
require "$WORKFLOW" '"test_sso","test_services","production_sso","production_services"'
require "$WORKFLOW" 'functionalUatStatus":"pending-user-session-validation"'
require "$WORKFLOW" 'Roll back API and web images on failure'
require "$WORKFLOW" "if [[ '\${{ steps.deploy_api.outputs.started }}' == 'true' ]]; then"
require "$WORKFLOW" "if [[ '\${{ steps.deploy_web.outputs.started }}' == 'true' ]]; then"
require "$WORKFLOW" 'msdualapi-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'msdualweb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'msdualapirb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'msdualwebrb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'microsoft-integration-010-065-test-${{ github.run_id }}-${{ github.run_attempt }}'

require "$APPLY" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$APPLY" 'Migration checksum manifest must contain exactly two SQL files.'
require "$APPLY" '045_microsoft_integration_consolidation'
require "$APPLY" '046_microsoft_sso_connection_profiles'
require "$APPLY" 'GRAPH_SECRET_ROWS_BEFORE'
require "$APPLY" 'SSO_SECRET_ROWS_BEFORE'
require "$APPLY" 'AUDIT_ROWS_BEFORE'
require "$APPLY" 'Microsoft Integration migrations changed operational user, role, or native-document counts.'
require "$APPLY" 'Migrations 045 and 046 are not both registered exactly once.'
require "$APPLY" 'Microsoft Integration migrations changed existing Graph, SSO, or audit evidence counts.'
require "$APPLY" 'MICROSOFT_DUAL_CONNECTIONS_DATABASE=APPLIED_OR_VERIFIED'

require "$RUNNER" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$RUNNER" 'MICROSOFT_INTEGRATION_MIGRATION_IMAGE'
require "$RUNNER" 'PROJECTPULSE_TEST_DATABASE_URL'
require "$RUNNER" 'EPHEMERAL_AZURE_TOKEN'
require "$RUNNER" 'az containerapp job delete'
require "$RUNNER" 'MICROSOFT_INTEGRATION_MIGRATION_JOB_STATUS=Succeeded'

require "$WAITER" 'properties.latestReadyRevisionName'
require "$WAITER" 'az containerapp revision show'
require "$WAITER" 'CONTAINERAPP_CANDIDATE_READY'
require "$WAITER" 'The expected Container Apps revision did not become the latest ready revision'

require "$TEST_045" 'MICROSOFT_INTEGRATION_MIGRATION_045_TEST=PASS'
require "$TEST_046" 'MICROSOFT_SSO_CONNECTION_MIGRATION_046_TEST=PASS'
require "$TEST_046" 'test_and_production_sso_secrets_supported'
require "$TEST_046" 'graph_service_secret_preserved'
require "$TEST_046" 'safe_rollback_preserved_graph_service_secret'

reject "$WORKFLOW" 'push:'
reject "$WORKFLOW" 'schedule:'
reject "$WORKFLOW" 'environment:[[:space:]]*production'
reject "$WORKFLOW" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$WORKFLOW" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$WORKFLOW" 'automatic_sync_enabled[[:space:]]*=[[:space:]]*TRUE'
reject "$WORKFLOW" '\[\[[^\n]*\$\{\{[[:space:]]*inputs\.'
reject "$WORKFLOW" 'TARGET_COMMIT[^\n]*\$\{\{[[:space:]]*inputs\.'
reject "$WORKFLOW" 'AUTHENTICATED_MICROSOFT_DUAL_CONNECTIONS_UAT=PASS'
reject "$WORKFLOW" 'AUTHENTICATED_TEST_AND_PRODUCTION_MICROSOFT_UAT=PASS'
reject "$WORKFLOW" 'functionalUatStatus":"passed"'
reject "$WORKFLOW" 'psql[^\n]*database/rollback/04[56]'
reject "$WORKFLOW" 'ACTIVE_API=.*properties[.]template[.]containers\[0\][.]image'
reject "$WORKFLOW" 'ACTIVE_WEB=.*properties[.]template[.]containers\[0\][.]image'
reject "$WORKFLOW" 'sso-test[^\n]*sso-test[^\n]*\{\}'
reject "$APPLY" 'DROP[[:space:]]+TABLE|TRUNCATE[[:space:]]+TABLE|DELETE[[:space:]]+FROM[[:space:]]+(app_users|app_roles|projectpulse_native_admin_documents|microsoft_integration_client_secrets|microsoft_integration_sso_client_secrets|microsoft_integration_audit_events)'
reject "$RUNNER" 'registry-password[[:space:]]+[^"$]'

INPUT_REFERENCE_COUNT="$(grep -Fc '${{ inputs.' "$WORKFLOW")"
[[ "$INPUT_REFERENCE_COUNT" == "3" ]] || fail "Expected exactly three non-shell input references; found $INPUT_REFERENCE_COUNT."

echo 'MICROSOFT_DUAL_CONNECTIONS_TEST_DEPLOYMENT_GUARD=PASS'
