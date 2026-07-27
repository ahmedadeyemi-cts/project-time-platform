#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY="$ROOT/.github/workflows/projectpulse-deploy-global-session-invalidation-test.yml"
VALIDATE="$ROOT/.github/workflows/validate-global-session-invalidation-test-deployment.yml"
EXPECTED_RELEASE="f626222c6de103a30db88a071517c6cc2587d96a"

fail() { echo "ERROR: $*" >&2; exit 1; }
require_file() { [[ -f "$1" ]] || fail "Missing required file: ${1#$ROOT/}"; }
require() { grep -Fq -- "$2" "$1" || fail "Missing contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }

require_file "$DEPLOY"
require_file "$VALIDATE"

require "$DEPLOY" 'name: ProjectPulse Deploy Role Policy Audit Module 065 Test'
require "$DEPLOY" 'workflow_dispatch:'
require "$DEPLOY" "default: $EXPECTED_RELEASE"
require "$DEPLOY" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$DEPLOY" 'DEPLOY-ROLE-POLICY-AUDIT-MODULE065-TO-TEST'
require "$DEPLOY" 'refs/heads/main'
require "$DEPLOY" 'environment: test'
require "$DEPLOY" 'group: projectpulse-deploy-global-session-invalidation-test'
require "$DEPLOY" 'cancel-in-progress: false'
require "$DEPLOY" 'Only the verified PR 181, PR 182, and PR 184 combined release may deploy.'
require "$DEPLOY" 'git -C control merge-base --is-ancestor'

# Release-root execution and fail-closed rollback points.
require "$DEPLOY" 'cd release'
require "$DEPLOY" 'test -d src/backend/ProjectTime.Api'
require "$DEPLOY" 'dotnet build src/backend/ProjectTime.Api/ProjectTime.Api.csproj --configuration Release'
require "$DEPLOY" '--project tests/ProjectTime.Api.AuthorizationTests/ProjectTime.Api.AuthorizationTests.csproj'
reject "$DEPLOY" 'dotnet build release/src/backend/ProjectTime.Api/ProjectTime.Api.csproj'
reject "$DEPLOY" '--project release/tests/ProjectTime.Api.AuthorizationTests/ProjectTime.Api.AuthorizationTests.csproj'
require "$DEPLOY" 'CURRENT_API_IMAGE="$(resolve_digest "$RAW_API_IMAGE")"'
require "$DEPLOY" 'CURRENT_WEB_IMAGE="$(resolve_digest "$RAW_WEB_IMAGE")"'
require "$DEPLOY" '[[ -n "$CURRENT_API_IMAGE" && -n "$CURRENT_WEB_IMAGE" ]]'
reject "$DEPLOY" 'echo[[:space:]]+"current_api_image=\$\(resolve_digest'
reject "$DEPLOY" 'echo[[:space:]]+"current_web_image=\$\(resolve_digest'
require "$DEPLOY" '"releaseWorkingDirectory": "verified"'
require "$DEPLOY" '"rollbackImageResolution": "fail-closed"'

# PR 181, PR 182, and PR 184 source contracts.
require "$DEPLOY" '"ROLE_ASSIGN"'
require "$DEPLOY" 'NormalizeRoutePath(path)'
require "$DEPLOY" 'const ROLE_POLICY_SESSION_WAIT_MS = 3500;'
require "$DEPLOY" 'TRAILING_SLASH_POST_BOUNDARY'
require "$DEPLOY" 'projectpulse-authoritative-native-fetch-fallback-v1'
require "$DEPLOY" "recoveredFrom: 'xhr-success-missing-collections'"
require "$DEPLOY" "! grep -Fq 'projectpulse-authoritative-session-invalidation-v1'"
require "$DEPLOY" "! grep -Fq \"from 'react-dom/client'\""
require "$DEPLOY" 'STRAY_THEME_TEXT'
require "$DEPLOY" "content: '☾'"
require "$DEPLOY" "content: '☀'"
require "$DEPLOY" 'UseMicrosoftPublicSsoOriginCompatibility'
require "$DEPLOY" 'X-Forwarded-Host'
require "$DEPLOY" 'X-Forwarded-Proto'
require "$DEPLOY" 'invalid_forwarded_public_origin'
require "$DEPLOY" 'MapMicrosoftServicesRuntimeProfileEndpoints'
require "$DEPLOY" "async function persistConfiguration(purpose = 'integration')"
require "$DEPLOY" "if (purpose !== 'sso')"
require "$DEPLOY" 'Save SSO connection'
require "$DEPLOY" 'Use current callback'
require "$DEPLOY" 'tenant?.environmentMode === runtimeEnvironment'
require "$DEPLOY" 'applyPayload?.runtimeActivated !== true'
require "$DEPLOY" 'module_065_services_profile_not_active'
require "$DEPLOY" 'npm run validate:microsoft-connection'
require "$DEPLOY" 'npm run validate:microsoft-sso-runtime'
require "$DEPLOY" 'npm run build'

# Exact API/web deployment and protected route probes.
require "$DEPLOY" 'Capture current immutable API and web images'
require "$DEPLOY" 'Build exact immutable API and web candidates'
require "$DEPLOY" 'Deploy exact API candidate'
require "$DEPLOY" 'Wait for exact API revision'
require "$DEPLOY" "probe role-assign POST '/api/admin/users/roles' '401' '{}'"
require "$DEPLOY" "probe role-assign-slash POST '/api/admin/users/roles/' '401' '{}'"
require "$DEPLOY" "probe audit-history GET '/api/admin/audit-history/events' '401'"
require "$DEPLOY" "probe module065-sso POST '/api/microsoft-integration/sso-apply-profile' '401' '{}'"
require "$DEPLOY" "probe module065-services POST '/api/microsoft-integration/services-apply-profile' '401' '{}'"
require "$DEPLOY" "probe module065-mail PUT '/api/microsoft-integration/mail-runtime' '401,403' '{}'"
require "$DEPLOY" 'ROLE_POLICY_AUDIT_MODULE065_API_VALIDATION=PASS'
require "$DEPLOY" 'Deploy exact web candidate'
require "$DEPLOY" 'Wait for exact web revision'
require "$DEPLOY" 'Validate served role-policy, audit, theme, and Module 065 assets'
require "$DEPLOY" '/api/microsoft-integration/sso-apply-profile'
require "$DEPLOY" '/api/microsoft-integration/services-apply-profile'
require "$DEPLOY" 'module_065_services_profile_not_active'
require "$DEPLOY" 'ROLE_POLICY_AUDIT_MODULE065_WEB_VALIDATION=PASS'

# Evidence and safe rollback.
require "$DEPLOY" '"module065PublicSsoOrigin": "forwarded-origin-validated"'
require "$DEPLOY" '"module065PreviewEnvironment": "running-environment-required"'
require "$DEPLOY" '"module065IndependentSsoSave": "source-and-served-assets-validated"'
require "$DEPLOY" '"module065Secrets": "unchanged"'
require "$DEPLOY" '"migrations": "unchanged"'
require "$DEPLOY" '"database": "unchanged"'
require "$DEPLOY" '"configuration": "unchanged"'
require "$DEPLOY" 'Restore captured web and API images on failure'
require "$DEPLOY" 'Web rollback skipped because another image is active'
require "$DEPLOY" 'API rollback skipped because another image is active'
require "$DEPLOY" 'rpam065webrb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$DEPLOY" 'rpam065apirb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'

reject "$DEPLOY" '^[[:space:]]*push:'
reject "$DEPLOY" '^[[:space:]]*schedule:'
reject "$DEPLOY" 'environment:[[:space:]]*production'
reject "$DEPLOY" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$DEPLOY" 'psql|PROJECTPULSE_TEST_DATABASE_URL|PTP_DB_'
reject "$DEPLOY" 'database/migrations|database/rollback|Apply or verify migration|migration image'
reject "$DEPLOY" 'az[[:space:]]+role[[:space:]]+assignment'

require "$VALIDATE" 'name: Validate Role Policy Audit Module 065 Test Deployment'
require "$VALIDATE" 'Enforce exact deployment-control scope'
require "$VALIDATE" 'Validate release-root and rollback contracts'
require "$VALIDATE" 'scripts/validate-global-session-invalidation-test-deployment.sh'
require "$VALIDATE" 'ProjectTime.Api.AuthorizationTests.csproj'
require "$VALIDATE" 'npm run validate:global-session-invalidation'
require "$VALIDATE" 'test-authoritative-session-transport-theme-runtime.mjs'
require "$VALIDATE" 'npm run validate:modules008009'
require "$VALIDATE" 'npm run validate:microsoft-connection'
require "$VALIDATE" 'npm run validate:microsoft-sso-runtime'
require "$VALIDATE" 'npm run build'
require "$VALIDATE" 'role-policy-audit-module065/deployment-controls'
reject "$VALIDATE" 'azure/login'
reject "$VALIDATE" 'az[[:space:]]+containerapp[[:space:]]+update'
reject "$VALIDATE" 'environment:[[:space:]]*production'

echo 'ROLE_POLICY_AUDIT_MODULE065_TEST_DEPLOYMENT_GUARD=PASS'
