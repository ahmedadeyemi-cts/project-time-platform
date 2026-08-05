#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY="$ROOT/.github/workflows/projectpulse-deploy-dynamic-rbac-group2a-test.yml"
VALIDATE="$ROOT/.github/workflows/validate-dynamic-rbac-group2a-test-deployment.yml"
RUN="$ROOT/scripts/run-dynamic-rbac-group2a-test-deployment.sh"
EXPECTED_RELEASE="175579682b24c27daa8ca7b73c348e4ff4cab687"

fail() { echo "ERROR: $*" >&2; exit 1; }
require_file() { [[ -f "$1" ]] || fail "Missing required file: ${1#$ROOT/}"; }
require() { grep -Fq -- "$2" "$1" || fail "Missing contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }

for file in "$DEPLOY" "$VALIDATE" "$RUN"; do
  require_file "$file"
done

require "$DEPLOY" 'name: ProjectPulse Deploy Dynamic RBAC and Group 2A Test'
require "$DEPLOY" 'workflow_dispatch:'
require "$DEPLOY" "default: $EXPECTED_RELEASE"
require "$DEPLOY" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$DEPLOY" 'DEPLOY-DYNAMIC-RBAC-GROUP2A-TO-TEST'
require "$DEPLOY" 'refs/heads/main'
require "$DEPLOY" 'environment: test'
require "$DEPLOY" 'group: projectpulse-deploy-test'
require "$DEPLOY" 'cancel-in-progress: false'
require "$DEPLOY" 'Only the verified dynamic RBAC and Group 2A source release may deploy.'
require "$DEPLOY" 'git -C control merge-base --is-ancestor'
require "$DEPLOY" 'DynamicRbacAdministrationModule.cs'
require "$DEPLOY" 'projectpulse-rbac-v1-2026-07-28'
require "$DEPLOY" '"/api/rbac/v1/bootstrap"'
require "$DEPLOY" '"/api/rbac/v1/matrix"'
require "$DEPLOY" '"/api/rbac/v1/role-memberships/assign"'
require "$DEPLOY" '"/api/rbac/v1/modules/register"'
require "$DEPLOY" 'fixedModuleCountRequired = false'
require "$DEPLOY" 'permanentFullControl = true'
require "$DEPLOY" "api('/api/rbac/v1/bootstrap')"
require "$DEPLOY" "api('/api/rbac/v1/matrix')"
require "$DEPLOY" 'No 70-module requirement'
require "$DEPLOY" "nativeFetch('/api/rbac/v1/bootstrap'"
require "$DEPLOY" "nativeFetch('/api/rbac/v1/matrix'"
require "$DEPLOY" 'Search by page name'
require "$DEPLOY" '/api/platform-operations/overview'
require "$DEPLOY" '/api/platform-operations/evidence'
require "$DEPLOY" '/api/platform-operations/architecture'
require "$DEPLOY" 'npm run validate:dynamic-rbac'
require "$DEPLOY" 'npm run validate:module012'
require "$DEPLOY" 'npm run validate:module037'
require "$DEPLOY" 'validate-group-1-navigation-work-consolidation.mjs'
require "$DEPLOY" 'validate-group-2a-provider-neutral-platform-operations.mjs'
require "$DEPLOY" 'npm run validate:module068'
require "$DEPLOY" 'npm run build'
require "$DEPLOY" 'azure/login@v2'
require "$DEPLOY" 'run-dynamic-rbac-group2a-test-deployment.sh'
require "$DEPLOY" 'control/evidence/dynamic-rbac-group2a-test-deployment.json'
reject "$DEPLOY" '^[[:space:]]*push:'
reject "$DEPLOY" '^[[:space:]]*schedule:'
reject "$DEPLOY" 'environment:[[:space:]]*production'
reject "$DEPLOY" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$DEPLOY" 'PROJECTPULSE_TEST_DATABASE_URL'
reject "$DEPLOY" 'export-pr55-test-database-url'
reject "$DEPLOY" 'database/migrations'
reject "$DEPLOY" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$DEPLOY" 'az[[:space:]]+containerapp[[:space:]]+job'

require "$RUN" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$RUN" 'CURRENT_API_IMAGE="$(resolve_digest "$RAW_API_IMAGE")"'
require "$RUN" 'CURRENT_WEB_IMAGE="$(resolve_digest "$RAW_WEB_IMAGE")"'
require "$RUN" '[[ -n "$CURRENT_API_IMAGE" && -n "$CURRENT_WEB_IMAGE" ]]'
require "$RUN" 'DYNAMIC_RBAC_GROUP2A_API_IMAGE'
require "$RUN" 'DYNAMIC_RBAC_GROUP2A_WEB_IMAGE'
require "$RUN" "probe_api rbac_bootstrap GET '/api/rbac/v1/bootstrap' '401,403'"
require "$RUN" "probe_api rbac_matrix GET '/api/rbac/v1/matrix' '401,403'"
require "$RUN" "probe_api rbac_users GET '/api/rbac/v1/users' '401,403'"
require "$RUN" "probe_api rbac_validate POST '/api/rbac/v1/policies/validate' '401,403'"
require "$RUN" "probe_api rbac_membership POST '/api/rbac/v1/role-memberships/assign' '401,403'"
require "$RUN" "probe_api platform_overview GET '/api/platform-operations/overview' '401,403'"
require "$RUN" "probe_api platform_apis GET '/api/platform-operations/apis' '401,403'"
require "$RUN" "probe_api platform_evidence GET '/api/platform-operations/evidence' '401,403'"
require "$RUN" "probe_api platform_architecture GET '/api/platform-operations/architecture' '401,403'"
require "$RUN" "probe_api module065_sso POST '/api/microsoft-integration/sso-apply-profile' '401,403'"
require "$RUN" "probe_api module010_preview POST '/api/admin/azure/users/preview' '401,403'"
require "$RUN" "probe_api module026_providers GET '/api/integrations/026/providers' '401,403'"
require "$RUN" "grep -Fq '/api/rbac/v1/bootstrap'"
require "$RUN" "grep -Fq '/api/rbac/v1/matrix'"
require "$RUN" "grep -Fq 'Role-Based Access Control'"
require "$RUN" "grep -Fq 'No 70-module requirement'"
require "$RUN" "grep -Fq 'Permanent organization-wide Full Control'"
require "$RUN" "grep -Fq 'Search by page name'"
require "$RUN" "grep -Fq '/api/platform-operations/overview'"
require "$RUN" "grep -Fq '/api/platform-operations/evidence'"
require "$RUN" "grep -Fq '/api/platform-operations/architecture'"
require "$RUN" "! grep -Fq 'Expected 12 roles and 70 modules'"
require "$RUN" 'DYNAMIC_RBAC_GROUP2A_API_VALIDATION=PASS'
require "$RUN" 'DYNAMIC_RBAC_GROUP2A_WEB_VALIDATION=PASS'
require "$RUN" 'restore_web'
require "$RUN" 'restore_api'
require "$RUN" 'Web rollback skipped because another image is active'
require "$RUN" 'API rollback skipped because another image is active'
require "$RUN" '"databaseMutation": false'
require "$RUN" '"fixedModuleCountRequired": false'
require "$RUN" '"roleMembershipMutationPerformedByDeployment": false'
require "$RUN" '"policyPublishPerformedByDeployment": false'
require "$RUN" '"moduleCatalogMutationPerformedByDeployment": false'
require "$RUN" '"group2A": "modules-013-016-068-preserved-and-served"'
require "$RUN" '"externalProviderCallsPerformedByDeployment": false'
require "$RUN" '"credentialValuesChanged": false'
require "$RUN" '"imageRollbackOnFailure": "candidate-only"'
reject "$RUN" 'PROJECTPULSE_TEST_DATABASE_URL|psql|database/migrations|database/rollback'
reject "$RUN" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$RUN" 'az[[:space:]]+containerapp[[:space:]]+job'
reject "$RUN" 'environment:[[:space:]]*production'
reject "$RUN" 'api\.getbase\.com'

require "$VALIDATE" 'name: Validate Dynamic RBAC and Group 2A Test Deployment'
require "$VALIDATE" 'Enforce exact deployment-control scope'
require "$VALIDATE" 'release/dynamic-rbac-group2a-test-deployment-*'
require "$VALIDATE" 'scripts/validate-dynamic-rbac-group2a-test-deployment.sh'
require "$VALIDATE" 'bash -n scripts/run-dynamic-rbac-group2a-test-deployment.sh'
require "$VALIDATE" 'bash -n scripts/validate-dynamic-rbac-group2a-test-deployment.sh'
require "$VALIDATE" 'ProjectTime.Api.AuthorizationTests.csproj'
require "$VALIDATE" 'test-projectpulse-api-startup.sh'
require "$VALIDATE" 'npm run validate:dynamic-rbac'
require "$VALIDATE" 'npm run validate:module012'
require "$VALIDATE" 'npm run validate:module037'
require "$VALIDATE" 'validate-group-1-navigation-work-consolidation.mjs'
require "$VALIDATE" 'validate-group-2a-provider-neutral-platform-operations.mjs'
require "$VALIDATE" 'npm run validate:module068'
require "$VALIDATE" 'npm run build'
reject "$VALIDATE" 'azure/login'
reject "$VALIDATE" 'az[[:space:]]+containerapp[[:space:]]+(update|job)'
reject "$VALIDATE" 'environment:[[:space:]]*production'
reject "$VALIDATE" 'PROJECTPULSE_TEST_DATABASE_URL|psql'

echo 'DYNAMIC_RBAC_GROUP2A_TEST_DEPLOYMENT_GUARD=PASS'
