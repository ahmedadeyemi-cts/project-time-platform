#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY="$ROOT/.github/workflows/projectpulse-deploy-global-session-invalidation-test.yml"
VALIDATE="$ROOT/.github/workflows/validate-global-session-invalidation-test-deployment.yml"
EXPECTED_RELEASE="9ba6d676189ebca39f408c3bc7a71aa652cea8bf"

fail() { echo "ERROR: $*" >&2; exit 1; }
require_file() { [[ -f "$1" ]] || fail "Missing required file: ${1#$ROOT/}"; }
require() { grep -Fq -- "$2" "$1" || fail "Missing contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }

require_file "$DEPLOY"
require_file "$VALIDATE"

require "$DEPLOY" 'name: ProjectPulse Emergency Restore Pre-Session-Invalidation Web Test'
require "$DEPLOY" 'workflow_dispatch:'
require "$DEPLOY" "default: $EXPECTED_RELEASE"
require "$DEPLOY" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$DEPLOY" 'RESTORE-PRE-SESSION-INVALIDATION-WEB-TO-TEST'
require "$DEPLOY" 'environment: test'
require "$DEPLOY" 'refs/heads/main'
require "$DEPLOY" 'group: projectpulse-deploy-global-session-invalidation-test'
require "$DEPLOY" 'cancel-in-progress: false'
require "$DEPLOY" 'DISPATCH_RELEASE_COMMIT: ${{ inputs.release_commit }}'
require "$DEPLOY" 'DISPATCH_CONFIRMATION: ${{ inputs.confirmation }}'
require "$DEPLOY" 'WORKFLOW_SOURCE_REF: ${{ github.ref }}'
require "$DEPLOY" 'WORKFLOW_SOURCE_SHA: ${{ github.sha }}'
require "$DEPLOY" 'git -C control merge-base --is-ancestor'
require "$DEPLOY" 'Only the approved pre-session-invalidation rollback release may deploy.'

require "$DEPLOY" "grep -Fq 'projectpulse-authoritative-xhr-v1' \"\$SOURCE\""
require "$DEPLOY" "request.setRequestHeader('X-ProjectPulse-Session', token);"
require "$DEPLOY" "request.setRequestHeader('X-Project-Pulse-Session', token);"
require "$DEPLOY" "request.setRequestHeader('X-Session-Token', token);"
require "$DEPLOY" "! grep -Fq 'projectpulse-authoritative-session-invalidation-v1' \"\$SOURCE\""
require "$DEPLOY" "! grep -Fq 'projectpulse:session-invalidated' \"\$SOURCE\""
require "$DEPLOY" "! grep -Fq 'invalidateProjectPulseSession' \"\$SOURCE\""
require "$DEPLOY" "! grep -Fq 'window.location.reload();' \"\$SOURCE\""
require "$DEPLOY" '/api/admin/audit-history/events'
require "$DEPLOY" 'Manager team scope'

require "$DEPLOY" 'Capture current immutable web image'
require "$DEPLOY" 'Build exact rollback web candidate'
require "$DEPLOY" 'Restore pre-session-invalidation web candidate only'
require "$DEPLOY" 'Wait for exact restored web revision'
require "$DEPLOY" 'Validate served rollback bundle'
require "$DEPLOY" 'PRE_SESSION_INVALIDATION_WEB_RESTORED'
require "$DEPLOY" 'EMERGENCY_LOGIN_WEB_RESTORE_VALIDATION=PASS'
require "$DEPLOY" '"globalSessionInvalidation": "absent-from-restored-bundle"'
require "$DEPLOY" '"apiDeployment": "unchanged"'
require "$DEPLOY" '"migrations": "unchanged"'
require "$DEPLOY" '"database": "unchanged"'
require "$DEPLOY" 'Restore current web image if emergency rollback fails'
require "$DEPLOY" 'wait-containerapp-ready-revision.sh'
require "$DEPLOY" 'loginrestorefail-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'

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

echo 'EMERGENCY_LOGIN_WEB_RESTORE_DEPLOYMENT_GUARD=PASS'
