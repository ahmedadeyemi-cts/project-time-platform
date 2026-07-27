#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY="$ROOT/.github/workflows/projectpulse-deploy-global-session-invalidation-test.yml"
VALIDATE="$ROOT/.github/workflows/validate-global-session-invalidation-test-deployment.yml"
EXPECTED_RELEASE="a8dd45811fbb89c97785a5adea23cbf3813978f8"

fail() { echo "ERROR: $*" >&2; exit 1; }
require_file() { [[ -f "$1" ]] || fail "Missing required file: ${1#$ROOT/}"; }
require() { grep -Fq -- "$2" "$1" || fail "Missing contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }

require_file "$DEPLOY"
require_file "$VALIDATE"

require "$DEPLOY" 'name: ProjectPulse Deploy Global Session Invalidation Test'
require "$DEPLOY" 'workflow_dispatch:'
require "$DEPLOY" "default: $EXPECTED_RELEASE"
require "$DEPLOY" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$DEPLOY" 'DEPLOY-GLOBAL-SESSION-INVALIDATION-TO-TEST'
require "$DEPLOY" 'environment: test'
require "$DEPLOY" 'refs/heads/main'
require "$DEPLOY" 'DISPATCH_RELEASE_COMMIT: ${{ inputs.release_commit }}'
require "$DEPLOY" 'DISPATCH_CONFIRMATION: ${{ inputs.confirmation }}'
require "$DEPLOY" 'WORKFLOW_SOURCE_REF: ${{ github.ref }}'
require "$DEPLOY" 'WORKFLOW_SOURCE_SHA: ${{ github.sha }}'
require "$DEPLOY" 'git -C control merge-base --is-ancestor'
require "$DEPLOY" 'Only the verified global session invalidation release may deploy.'

require "$DEPLOY" 'projectpulse-authoritative-session-invalidation-v1'
require "$DEPLOY" 'projectpulse:session-invalidated'
require "$DEPLOY" 'window.__projectPulseSessionInvalidationStarted'
require "$DEPLOY" 'SESSION_REJECTION_STATUS_CODES'
require "$DEPLOY" "'session_required'"
require "$DEPLOY" 'clearSessionStorage();'
require "$DEPLOY" "window.location.hash = '#dashboard';"
require "$DEPLOY" 'window.location.reload();'
require "$DEPLOY" 'void inspectFetchSessionRejection(input, response);'
require "$DEPLOY" 'if (isSessionRejection(request.status, payload, raw))'
require "$DEPLOY" '! grep -Fq "projectPulsePostLoginRoute"'
require "$DEPLOY" 'npm run validate:global-session-invalidation'

require "$DEPLOY" 'Capture current immutable web image'
require "$DEPLOY" 'Build exact session recovery web candidate'
require "$DEPLOY" 'Deploy session recovery web candidate only'
require "$DEPLOY" 'Wait for exact session recovery web revision'
require "$DEPLOY" 'Validate served global session recovery bundle'
require "$DEPLOY" 'projectPulseSessionInvalidationStarted'
require "$DEPLOY" 'session_required'
require "$DEPLOY" 'projectPulseAuthSession'
require "$DEPLOY" 'projectPulseViewAsUser'
require "$DEPLOY" 'X-ProjectPulse-Session'
require "$DEPLOY" '/api/admin/audit-history/events'
require "$DEPLOY" 'Manager team scope'
require "$DEPLOY" 'GLOBAL_SESSION_INVALIDATION_WEB_VALIDATION=PASS'
require "$DEPLOY" '"apiDeployment": "unchanged"'
require "$DEPLOY" '"migrations": "unchanged"'
require "$DEPLOY" '"database": "unchanged"'
require "$DEPLOY" '"modules008009": "preserved"'
require "$DEPLOY" '"modules010065": "preserved"'
require "$DEPLOY" 'Roll back web image on failure'
require "$DEPLOY" 'wait-containerapp-ready-revision.sh'
require "$DEPLOY" 'sessrb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'

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

require "$VALIDATE" 'name: Validate Global Session Invalidation Test Deployment'
require "$VALIDATE" 'push:'
require "$VALIDATE" 'pull_request:'
require "$VALIDATE" 'Enforce deployment-control allowlist'
require "$VALIDATE" 'scripts/validate-global-session-invalidation-test-deployment.sh'
require "$VALIDATE" 'npm run validate:global-session-invalidation'
require "$VALIDATE" 'npm run build'
require "$VALIDATE" 'global-session-invalidation/deployment-controls'
require "$VALIDATE" '.github/workflows/projectpulse-deploy-global-session-invalidation-test.yml'
require "$VALIDATE" '.github/workflows/validate-global-session-invalidation-test-deployment.yml'
require "$VALIDATE" 'scripts/validate-global-session-invalidation-test-deployment.sh'

reject "$VALIDATE" 'az[[:space:]]+containerapp[[:space:]]+update'
reject "$VALIDATE" 'azure/login'
reject "$VALIDATE" 'environment:[[:space:]]*production'

echo 'GLOBAL_SESSION_INVALIDATION_TEST_DEPLOYMENT_GUARD=PASS'
