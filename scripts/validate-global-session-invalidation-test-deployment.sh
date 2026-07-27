#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY="$ROOT/.github/workflows/projectpulse-deploy-global-session-invalidation-test.yml"
VALIDATE="$ROOT/.github/workflows/validate-global-session-invalidation-test-deployment.yml"
EXPECTED_RELEASE="54a0b678b86dde32c409d20aa3751ce0293f079d"

fail() { echo "ERROR: $*" >&2; exit 1; }
require_file() { [[ -f "$1" ]] || fail "Missing required file: ${1#$ROOT/}"; }
require() { grep -Fq -- "$2" "$1" || fail "Missing contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }

require_file "$DEPLOY"
require_file "$VALIDATE"

require "$DEPLOY" 'name: ProjectPulse Deploy Authoritative Session and Theme Test'
require "$DEPLOY" 'workflow_dispatch:'
require "$DEPLOY" "default: $EXPECTED_RELEASE"
require "$DEPLOY" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$DEPLOY" 'DEPLOY-AUTHORITATIVE-SESSION-THEME-TO-TEST'
require "$DEPLOY" 'environment: test'
require "$DEPLOY" 'refs/heads/main'
require "$DEPLOY" 'group: projectpulse-deploy-global-session-invalidation-test'
require "$DEPLOY" 'cancel-in-progress: false'
require "$DEPLOY" 'DISPATCH_RELEASE_COMMIT: ${{ inputs.release_commit }}'
require "$DEPLOY" 'DISPATCH_CONFIRMATION: ${{ inputs.confirmation }}'
require "$DEPLOY" 'WORKFLOW_SOURCE_REF: ${{ github.ref }}'
require "$DEPLOY" 'WORKFLOW_SOURCE_SHA: ${{ github.sha }}'
require "$DEPLOY" 'git -C control merge-base --is-ancestor'
require "$DEPLOY" 'Only the verified authoritative session and theme release may deploy.'

require "$DEPLOY" 'test-authoritative-session-transport-theme-runtime.mjs'
require "$DEPLOY" 'validate-global-session-invalidation.mjs'
require "$DEPLOY" 'validate-modules-008-009-admin-experience.mjs'
require "$DEPLOY" 'function installProtectedFetchReadinessGate()'
require "$DEPLOY" 'function globalXhrBridgeCanSupplyToken(token)'
require "$DEPLOY" 'globalXhrBridgeToken() === token'
require "$DEPLOY" 'if (token && !globalXhrBridgeCanSupplyToken(token))'
require "$DEPLOY" "error.code = 'session_not_ready';"
require "$DEPLOY" "error.code = 'session_transport_conflict';"
require "$DEPLOY" "! grep -Fq 'projectpulse-authoritative-session-invalidation-v1'"
require "$DEPLOY" "! grep -Fq 'projectpulse:session-invalidated'"
require "$DEPLOY" "! grep -Fq 'invalidateProjectPulseSession'"
require "$DEPLOY" "! grep -Fq 'window.location.reload();'"

require "$DEPLOY" 'STRAY_THEME_TEXT'
require "$DEPLOY" 'Switch to ${target} mode'
require "$DEPLOY" "[data-projectpulse-theme-control='true']"
require "$DEPLOY" "content: 'Dark mode'"
require "$DEPLOY" "content: 'Light mode'"

require "$DEPLOY" 'Capture current immutable web image'
require "$DEPLOY" 'Build exact web candidate'
require "$DEPLOY" 'Deploy web candidate only'
require "$DEPLOY" 'Wait for exact web revision'
require "$DEPLOY" 'Validate served session and theme assets'
require "$DEPLOY" 'session_not_ready'
require "$DEPLOY" 'session_transport_conflict'
require "$DEPLOY" '/api/admin/audit-history/events'
require "$DEPLOY" 'Manager team scope'
require "$DEPLOY" 'projectpulse-theme-control'
require "$DEPLOY" 'Switch to dark mode'
require "$DEPLOY" 'data-projectpulse-theme-control'
require "$DEPLOY" 'backdrop-filter'
require "$DEPLOY" 'AUTHORITATIVE_SESSION_THEME_WEB_VALIDATION=PASS'
require "$DEPLOY" '"sessionRuntimeHarness": "passed-before-deployment"'
require "$DEPLOY" '"servedJavaScriptValidation": "passed"'
require "$DEPLOY" '"servedThemeCssValidation": "passed"'
require "$DEPLOY" '"apiDeployment": "unchanged"'
require "$DEPLOY" '"migrations": "unchanged"'
require "$DEPLOY" '"database": "unchanged"'
require "$DEPLOY" 'Restore captured web image on failure'
require "$DEPLOY" 'Rollback skipped because another image is active'
require "$DEPLOY" 'wait-containerapp-ready-revision.sh'
require "$DEPLOY" 'sessthemerb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'

reject "$DEPLOY" '^[[:space:]]*push:'
reject "$DEPLOY" '^[[:space:]]*schedule:'
reject "$DEPLOY" 'environment:[[:space:]]*production'
reject "$DEPLOY" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$DEPLOY" 'AZURE_API_APP'
reject "$DEPLOY" 'Deploy API|api_image|apiImage'
reject "$DEPLOY" 'psql|PROJECTPULSE_TEST_DATABASE_URL|PTP_DB_'
reject "$DEPLOY" 'database/migrations|database/rollback|Apply or verify migration'
reject "$DEPLOY" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$DEPLOY" 'functionalUatStatus":"passed"'

require "$VALIDATE" 'name: Validate Authoritative Session and Theme Test Deployment'
require "$VALIDATE" 'push:'
require "$VALIDATE" 'pull_request:'
require "$VALIDATE" 'Enforce deployment-control allowlist'
require "$VALIDATE" 'scripts/validate-global-session-invalidation-test-deployment.sh'
require "$VALIDATE" 'npm run validate:global-session-invalidation'
require "$VALIDATE" 'test-authoritative-session-transport-theme-runtime.mjs'
require "$VALIDATE" 'npm run validate:modules008009'
require "$VALIDATE" 'npm run build'
require "$VALIDATE" 'authoritative-session-theme/deployment-controls'
require "$VALIDATE" '.github/workflows/projectpulse-deploy-global-session-invalidation-test.yml'
require "$VALIDATE" '.github/workflows/validate-global-session-invalidation-test-deployment.yml'
require "$VALIDATE" 'scripts/validate-global-session-invalidation-test-deployment.sh'

reject "$VALIDATE" 'az[[:space:]]+containerapp[[:space:]]+update'
reject "$VALIDATE" 'azure/login'
reject "$VALIDATE" 'environment:[[:space:]]*production'

echo 'AUTHORITATIVE_SESSION_THEME_TEST_DEPLOYMENT_GUARD=PASS'
