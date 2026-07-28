#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY="$ROOT/.github/workflows/projectpulse-deploy-security-admin-repair-test.yml"
VALIDATE="$ROOT/.github/workflows/validate-security-admin-repair-test-deployment.yml"
RUNNER="$ROOT/scripts/run-security-admin-repair-test-deployment.sh"
EXPECTED_RELEASE="7d21b93d2a5a3e2eb7681cce8662ffb81bf7c01a"

fail() { echo "ERROR: $*" >&2; exit 1; }
require_file() { [[ -f "$1" ]] || fail "Missing required file: ${1#$ROOT/}"; }
require() { grep -Fq -- "$2" "$1" || fail "Missing contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }

for file in "$DEPLOY" "$VALIDATE" "$RUNNER"; do
  require_file "$file"
done

require "$DEPLOY" 'name: ProjectPulse Deploy Security and Admin Repair Test'
require "$DEPLOY" 'workflow_dispatch:'
require "$DEPLOY" "default: $EXPECTED_RELEASE"
require "$DEPLOY" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$DEPLOY" 'DEPLOY-SECURITY-ADMIN-REPAIR-TO-TEST'
require "$DEPLOY" 'refs/heads/main'
require "$DEPLOY" 'environment: test'
require "$DEPLOY" 'group: projectpulse-deploy-security-admin-repair-test'
require "$DEPLOY" 'cancel-in-progress: false'
require "$DEPLOY" 'Only the verified HAR-confirmed role-policy repair source release may deploy.'
require "$DEPLOY" 'git -C control merge-base --is-ancestor'
require "$DEPLOY" 'projectpulse-role-policy-authoritative-v3'
require "$DEPLOY" "requiredCollections: ['actions', 'scopes']"
require "$DEPLOY" "requiredCollections: ['roles', 'modules', 'grants']"
require "$DEPLOY" 'ScopedRolePolicyResultExecutionCompatibility.cs'
require "$DEPLOY" 'ProjectTime.Api.RouteResultExecutionTests.csproj'
require "$DEPLOY" 'UseScopedRolePolicyResultExecutionCompatibility'
require "$DEPLOY" 'await result.ExecuteAsync(context);'
require "$DEPLOY" 'X-ProjectPulse-Role-Policy-Execution'
require "$DEPLOY" 'explicit-iresult-v1'
require "$DEPLOY" 'ROLE_POLICY_METHOD_GROUP_EMPTY_200_REPRODUCED=PASS'
require "$DEPLOY" 'ROLE_POLICY_EXPLICIT_IRESULT_JSON=PASS'
require "$DEPLOY" 'ROLE_POLICY_RESULT_EXECUTION_SOURCE_VALIDATION=PASS'
require "$DEPLOY" 'UseProjectPulsePublicOriginCompatibility'
require "$DEPLOY" 'trusted_public_origin_unavailable'
require "$DEPLOY" 'ProjectPulse-CRM-ERP-Integration:'
require "$DEPLOY" 'map $http_x_forwarded_proto $projectpulse_forwarded_proto'
require "$DEPLOY" 'data-module-013-volume-control="collapsed-by-default"'
require "$DEPLOY" 'npm run validate:module026'
require "$DEPLOY" 'npm run validate:modules021026'
require "$DEPLOY" 'azure/login@v2'
require "$DEPLOY" 'run-security-admin-repair-test-deployment.sh'
require "$DEPLOY" 'control/evidence/security-admin-repair-test-deployment.json'
require "$DEPLOY" 'security-admin-repair-test-${{ github.run_id }}-${{ github.run_attempt }}'
reject "$DEPLOY" '^[[:space:]]*push:'
reject "$DEPLOY" '^[[:space:]]*schedule:'
reject "$DEPLOY" 'environment:[[:space:]]*production'
reject "$DEPLOY" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$DEPLOY" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$DEPLOY" 'psql|PROJECTPULSE_TEST_DATABASE_URL|PTP_DB_PASSWORD'
reject "$DEPLOY" 'database/migrations|database/rollback|schema_migrations'

INPUT_REFERENCE_COUNT="$(grep -Fc '${{ inputs.' "$DEPLOY")"
[[ "$INPUT_REFERENCE_COUNT" == 3 ]] || fail "Expected exactly three non-shell workflow input references; found $INPUT_REFERENCE_COUNT."
require "$DEPLOY" "DISPATCH_RELEASE_COMMIT: \${{ inputs.release_commit }}"
require "$DEPLOY" "DISPATCH_CONFIRMATION: \${{ inputs.confirmation }}"
require "$DEPLOY" "'\${{ steps.release.outputs.target_commit }}'"
reject "$DEPLOY" 'TARGET_COMMIT[^\n]*\$\{\{[[:space:]]*inputs\.'

require "$RUNNER" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$RUNNER" 'CURRENT_API_IMAGE="$(resolve_digest "$RAW_API_IMAGE")"'
require "$RUNNER" 'CURRENT_WEB_IMAGE="$(resolve_digest "$RAW_WEB_IMAGE")"'
require "$RUNNER" '[[ "${API_MODE,,}" == single && "${WEB_MODE,,}" == single ]]'
require "$RUNNER" 'security-admin-repair-$TARGET_COMMIT'
require "$RUNNER" 'API_SUFFIX="sarapi-${RUN_ID}-${RUN_ATTEMPT}"'
require "$RUNNER" 'WEB_SUFFIX="sarweb-${RUN_ID}-${RUN_ATTEMPT}"'
require "$RUNNER" "probe_api role_summary GET '/api/runtime/v2/role-policy/summary' '401,403'"
require "$RUNNER" "probe_api role_catalog GET '/api/runtime/v2/role-policy/catalog' '401,403'"
require "$RUNNER" "probe_api role_versions GET '/api/runtime/v2/role-policy/versions' '401,403'"
require "$RUNNER" "probe_api role_matrix GET '/api/runtime/v2/role-policy/matrix' '401,403'"
require "$RUNNER" 'ROLE_POLICY_RESULT_EXECUTION_PROTECTED_BOUNDARY=PASS'
require "$RUNNER" "probe_api module065_sso POST '/api/microsoft-integration/sso-apply-profile' '401,403' '{}'"
require "$RUNNER" "probe_api module010_preview POST '/api/admin/azure/users/preview' '401,403' '{}'"
require "$RUNNER" "probe_api module026_providers GET '/api/integrations/026/providers' '401,403'"
require "$RUNNER" 'invalid_forwarded_public_origin|trusted_public_origin_unavailable|outside the approved environment domains'
require "$RUNNER" 'projectpulse-role-policy-authoritative-v3'
require "$RUNNER" 'Show volume details'
require "$RUNNER" 'Hide volume details'
require "$RUNNER" 'module-013-volume-details'
require "$RUNNER" 'SECURITY_ADMIN_REPAIR_API_VALIDATION=PASS'
require "$RUNNER" 'SECURITY_ADMIN_REPAIR_WEB_VALIDATION=PASS'
require "$RUNNER" 'restore_web'
require "$RUNNER" 'restore_api'
require "$RUNNER" 'Web rollback skipped because another image is active'
require "$RUNNER" 'API rollback skipped because another image is active'
require "$RUNNER" '"deploymentType": "api-web-no-migration"'
require "$RUNNER" '"databaseMutation": false'
require "$RUNNER" '"harConfirmedFailure": "http-200-zero-byte-role-policy-response"'
require "$RUNNER" '"rolePolicyResultExecution": "explicit-iresult-v1-source-and-runtime-test-validated"'
require "$RUNNER" '"rolePolicyProtectedRoutes": "summary-catalog-versions-matrix-auth-boundary-verified"'
require "$RUNNER" '"rolePolicyAuthenticatedJsonUat": "required-after-deployment"'
require "$RUNNER" '"credentialValuesChanged": false'
require "$RUNNER" '"externalProviderCallsPerformedByDeployment": false'
require "$RUNNER" '"authenticatedFunctionalUat": "required"'
reject "$RUNNER" 'psql|PROJECTPULSE_TEST_DATABASE_URL|PTP_DB_PASSWORD'
reject "$RUNNER" 'database/migrations|database/rollback|schema_migrations'
reject "$RUNNER" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$RUNNER" 'environment:[[:space:]]*production'
reject "$RUNNER" 'api\.getbase\.com|graph\.microsoft\.com|smtp\.office365\.com'

require "$VALIDATE" 'name: Validate Security and Admin Repair Test Deployment'
require "$VALIDATE" 'release/repin-role-policy-result-execution-test-*'
require "$VALIDATE" 'Enforce exact deployment-control scope'
require "$VALIDATE" 'bash -n scripts/run-security-admin-repair-test-deployment.sh'
require "$VALIDATE" 'bash -n scripts/validate-security-admin-repair-test-deployment.sh'
require "$VALIDATE" 'scripts/validate-security-admin-repair-test-deployment.sh'
require "$VALIDATE" 'ProjectTime.Api.RouteResultExecutionTests.csproj'
require "$VALIDATE" 'Reproduce zero-byte 200 and validate explicit IResult execution'
require "$VALIDATE" 'ProjectTime.Api.AuthorizationTests.csproj'
require "$VALIDATE" 'validate-security-role-policy-public-origin.mjs'
require "$VALIDATE" 'validate-admin-runtime-stability.mjs'
require "$VALIDATE" 'validate-microsoft-integration-authoritative-connection.mjs'
require "$VALIDATE" 'validate-microsoft-sso-runtime-wiring.mjs'
require "$VALIDATE" 'validate-group-2a-provider-neutral-platform-operations.mjs'
require "$VALIDATE" 'validate-global-view-as-drawer.mjs'
require "$VALIDATE" 'npm run validate:module026'
require "$VALIDATE" 'npm run validate:modules021026'
require "$VALIDATE" 'npm run build'
require "$VALIDATE" 'security-admin-repair/deployment-controls'
reject "$VALIDATE" 'azure/login'
reject "$VALIDATE" 'az[[:space:]]+containerapp[[:space:]]+(update|job|create|delete)'
reject "$VALIDATE" 'environment:[[:space:]]*production'
reject "$VALIDATE" 'psql|PROJECTPULSE_TEST_DATABASE_URL|PTP_DB_PASSWORD'

echo 'SECURITY_ADMIN_REPAIR_TEST_DEPLOYMENT_GUARD=PASS'
