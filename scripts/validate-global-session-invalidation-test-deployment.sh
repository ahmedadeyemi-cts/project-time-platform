#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY="$ROOT/.github/workflows/projectpulse-deploy-global-session-invalidation-test.yml"
VALIDATE="$ROOT/.github/workflows/validate-global-session-invalidation-test-deployment.yml"
EXPECTED_RELEASE="2e72ad9a95ed7cf027b8e95e237b880824adddf4"

fail() { echo "ERROR: $*" >&2; exit 1; }
require_file() { [[ -f "$1" ]] || fail "Missing required file: ${1#$ROOT/}"; }
require() { grep -Fq -- "$2" "$1" || fail "Missing contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }

require_file "$DEPLOY"
require_file "$VALIDATE"

require "$DEPLOY" 'name: ProjectPulse Deploy Role Policy Audit Stability Test'
require "$DEPLOY" 'workflow_dispatch:'
require "$DEPLOY" "default: $EXPECTED_RELEASE"
require "$DEPLOY" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$DEPLOY" 'DEPLOY-ROLE-POLICY-AUDIT-STABILITY-TO-TEST'
require "$DEPLOY" 'refs/heads/main'
require "$DEPLOY" 'environment: test'
require "$DEPLOY" 'group: projectpulse-deploy-global-session-invalidation-test'
require "$DEPLOY" 'cancel-in-progress: false'
require "$DEPLOY" 'Only the verified PR 181 and PR 182 combined release may deploy.'
require "$DEPLOY" 'git -C control merge-base --is-ancestor'

# The failed run launched a repository-relative executable from the control root.
# The corrected Action must enter the release checkout before both .NET commands.
require "$DEPLOY" 'cd release'
require "$DEPLOY" 'test -d src/backend/ProjectTime.Api'
require "$DEPLOY" 'dotnet build src/backend/ProjectTime.Api/ProjectTime.Api.csproj --configuration Release'
require "$DEPLOY" '--project tests/ProjectTime.Api.AuthorizationTests/ProjectTime.Api.AuthorizationTests.csproj'
reject "$DEPLOY" 'dotnet build release/src/backend/ProjectTime.Api/ProjectTime.Api.csproj'
reject "$DEPLOY" '--project release/tests/ProjectTime.Api.AuthorizationTests/ProjectTime.Api.AuthorizationTests.csproj'
require "$DEPLOY" '"releaseWorkingDirectory": "verified"'

# PR 181 source and authorization contracts.
require "$DEPLOY" 'ScopedRolePolicyRules.cs'
require "$DEPLOY" 'runtime-data-compatibility.js'
require "$DEPLOY" '"ROLE_ASSIGN"'
require "$DEPLOY" 'NormalizeRoutePath(path)'
require "$DEPLOY" 'const ROLE_POLICY_SESSION_WAIT_MS = 3500;'
require "$DEPLOY" 'TRAILING_SLASH_POST_BOUNDARY'
require "$DEPLOY" 'REPEATED_TRAILING_SLASH_POST_BOUNDARY'
require "$DEPLOY" 'TRAILING_SLASH_READ_NOT_RECLASSIFIED'

# PR 182 response recovery, one owner, and theme dock contracts.
require "$DEPLOY" 'projectpulse-authoritative-native-fetch-fallback-v1'
require "$DEPLOY" 'const CAPTURED_NATIVE_FETCH'
require "$DEPLOY" "recoveredFrom: 'xhr-success-missing-collections'"
require "$DEPLOY" 'const nestedCandidate = candidates'
require "$DEPLOY" "! grep -Fq 'projectpulse-authoritative-session-invalidation-v1'"
require "$DEPLOY" "! grep -Fq 'projectpulse:session-invalidated'"
require "$DEPLOY" "! grep -Fq 'invalidateProjectPulseSession'"
require "$DEPLOY" "! grep -Fq \"from 'react-dom/client'\""
require "$DEPLOY" "! grep -Fq 'createRoot('"
require "$DEPLOY" "! grep -Fq 'MutationObserver'"
require "$DEPLOY" 'STRAY_THEME_TEXT'
require "$DEPLOY" "content: '☾'"
require "$DEPLOY" "content: '☀'"
require "$DEPLOY" 'npm run build'

# API/web deployment, smoke tests, immutable evidence, and safe rollback.
require "$DEPLOY" 'Capture current immutable API and web images'
require "$DEPLOY" 'Build exact immutable API and web candidates'
require "$DEPLOY" 'Deploy exact API candidate'
require "$DEPLOY" 'Wait for exact API revision'
require "$DEPLOY" 'Validate public API readiness and protected boundaries'
require "$DEPLOY" "probe role-assign POST '/api/admin/users/roles' '401' '{}'"
require "$DEPLOY" "probe role-assign-slash POST '/api/admin/users/roles/' '401' '{}'"
require "$DEPLOY" "probe audit-history GET '/api/admin/audit-history/events' '401'"
require "$DEPLOY" 'ROLE_POLICY_AUDIT_API_VALIDATION=PASS'
require "$DEPLOY" 'Deploy exact web candidate'
require "$DEPLOY" 'Wait for exact web revision'
require "$DEPLOY" 'Validate served response recovery, Module 008, role-policy and theme assets'
require "$DEPLOY" 'xhr-success-missing-collections'
require "$DEPLOY" 'ROLE_POLICY_AUDIT_WEB_VALIDATION=PASS'
require "$DEPLOY" '"migrations": "unchanged"'
require "$DEPLOY" '"database": "unchanged"'
require "$DEPLOY" '"configuration": "unchanged"'
require "$DEPLOY" '"smokeTests": "passed"'
require "$DEPLOY" 'Restore captured web and API images on failure'
require "$DEPLOY" 'Web rollback skipped because another image is active'
require "$DEPLOY" 'API rollback skipped because another image is active'
require "$DEPLOY" 'rpauditwebrb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$DEPLOY" 'rpauditapirb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'

reject "$DEPLOY" '^[[:space:]]*push:'
reject "$DEPLOY" '^[[:space:]]*schedule:'
reject "$DEPLOY" 'environment:[[:space:]]*production'
reject "$DEPLOY" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$DEPLOY" 'psql|PROJECTPULSE_TEST_DATABASE_URL|PTP_DB_'
reject "$DEPLOY" 'database/migrations|database/rollback|Apply or verify migration|migration image'
reject "$DEPLOY" 'az[[:space:]]+role[[:space:]]+assignment'

require "$VALIDATE" 'name: Validate Role Policy Audit Stability Test Deployment'
require "$VALIDATE" 'Enforce exact deployment-control scope'
require "$VALIDATE" 'Validate release working-directory contract'
require "$VALIDATE" 'scripts/validate-global-session-invalidation-test-deployment.sh'
require "$VALIDATE" 'dotnet build src/backend/ProjectTime.Api/ProjectTime.Api.csproj --configuration Release'
require "$VALIDATE" 'ProjectTime.Api.AuthorizationTests.csproj'
require "$VALIDATE" 'npm run validate:global-session-invalidation'
require "$VALIDATE" 'test-authoritative-session-transport-theme-runtime.mjs'
require "$VALIDATE" 'npm run validate:modules008009'
require "$VALIDATE" 'npm run build'
require "$VALIDATE" 'role-policy-audit-stability/deployment-controls'
reject "$VALIDATE" 'azure/login'
reject "$VALIDATE" 'az[[:space:]]+containerapp[[:space:]]+update'
reject "$VALIDATE" 'environment:[[:space:]]*production'

echo 'ROLE_POLICY_AUDIT_STABILITY_TEST_DEPLOYMENT_GUARD=PASS'
