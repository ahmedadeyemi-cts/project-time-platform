#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-runtime-direct-timer-recovery-test.yml"
EXPECTED_RELEASE="b1dfbde5f0dc6cfe5cbae44001f7e1258b6e453f"

fail() { echo "ERROR: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" ]] || fail "Missing deployment workflow: ${WORKFLOW#$ROOT/}"
require() { grep -Fq -- "$2" "$1" || fail "Missing required contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }

require "$WORKFLOW" 'name: ProjectPulse Deploy Functional Runtime UAT Candidate Test'
require "$WORKFLOW" 'workflow_dispatch:'
require "$WORKFLOW" "default: $EXPECTED_RELEASE"
require "$WORKFLOW" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$WORKFLOW" 'DEPLOY-FUNCTIONAL-RUNTIME-UAT-CANDIDATE-TO-TEST'
require "$WORKFLOW" 'environment: test'
require "$WORKFLOW" 'refs/heads/main'
require "$WORKFLOW" 'Check out exact verified release'
require "$WORKFLOW" 'Only the verified functional runtime UAT release may deploy.'
require "$WORKFLOW" 'git -C control merge-base --is-ancestor'
require "$WORKFLOW" 'DISPATCH_RELEASE_COMMIT: ${{ inputs.release_commit }}'
require "$WORKFLOW" 'DISPATCH_CONFIRMATION: ${{ inputs.confirmation }}'
require "$WORKFLOW" 'WORKFLOW_SOURCE_REF: ${{ github.ref }}'
require "$WORKFLOW" 'WORKFLOW_SOURCE_SHA: ${{ github.sha }}'
require "$WORKFLOW" '[[ "$WORKFLOW_SOURCE_REF" =='
require "$WORKFLOW" '[[ "$DISPATCH_CONFIRMATION" =='
require "$WORKFLOW" '[[ "$DISPATCH_RELEASE_COMMIT" =~ ^[0-9a-f]{40}$ ]]'
require "$WORKFLOW" '[[ "$CONTROL_COMMIT" == "$WORKFLOW_SOURCE_SHA" ]]'
require "$WORKFLOW" '[[ "$TARGET_COMMIT" == "$DISPATCH_RELEASE_COMMIT" ]]'

require "$WORKFLOW" "const DIAGNOSTIC_MARKER = 'projectpulse-authoritative-xhr-v1'"
require "$WORKFLOW" 'new XMLHttpRequest()'
require "$WORKFLOW" "request.setRequestHeader('X-ProjectPulse-Module-Number', moduleNumber)"
require "$WORKFLOW" 'projectpulse:authoritative-api-diagnostic'
require "$WORKFLOW" '__projectPulseAuthoritativeApiDiagnostics'
require "$WORKFLOW" "! grep -Fq 'window.fetch('"

require "$WORKFLOW" "import { authoritativeApi } from './projectpulse-authoritative-api.js';"
require "$WORKFLOW" "'/api/role-policy/summary': '/api/runtime/v2/role-policy/summary'"
require "$WORKFLOW" "'/api/role-policy/matrix': '/api/runtime/v2/role-policy/matrix'"
require "$WORKFLOW" "'/api/runtime/v2/timesheet/steward/users': '/api/timesheet/ptc/users'"
require "$WORKFLOW" 'projectpulse-authoritative-xhr-compatibility-v2'
require "$WORKFLOW" "! grep -Fq 'window.__projectPulseOriginalFetch'"

require "$WORKFLOW" '/api/timesheet/timers/targets?weekStart='
require "$WORKFLOW" "requiredCollections: ['targets']"
require "$WORKFLOW" 'timerTargetCounts'
require "$WORKFLOW" 'timerTargetAuthoritativeSources'
require "$WORKFLOW" '/api/timesheet/timers/active'
require "$WORKFLOW" 'window.setInterval(() => void load(), 5000)'
require "$WORKFLOW" 'window.setInterval(() => setClock(new Date()), 1000)'
require "$WORKFLOW" 'Timer status check failed'
require "$WORKFLOW" 'Try timer check again'
require "$WORKFLOW" 'Timer automatically stopped'
require "$WORKFLOW" 'module001-active-timer-action-error'
require "$WORKFLOW" 'Stop timer'
require "$WORKFLOW" 'Discard'

require "$WORKFLOW" "const PREFIX = 'projectpulse-route-'"
require "$WORKFLOW" 'body.projectpulse-route-certify-integration .module-grid'
require "$WORKFLOW" 'max-height:calc(100dvh - 13.5rem)'
require "$WORKFLOW" 'overscroll-behavior:contain'
require "$WORKFLOW" '<CriticalRoutePresentationBoundary />'
require "$WORKFLOW" "grep -Fq 'projectpulse-route-certify-integration' /tmp/functional-runtime-uat-validation/app.css"

require "$WORKFLOW" 'Capture current immutable web image'
require "$WORKFLOW" 'Build immutable functional runtime UAT web image'
require "$WORKFLOW" 'Deploy functional runtime UAT candidate web release'
require "$WORKFLOW" 'Validate served candidate assets — authenticated UAT still required'
require "$WORKFLOW" 'FUNCTIONAL_RUNTIME_UAT_CANDIDATE_ASSETS=DEPLOYED'
require "$WORKFLOW" 'AUTHENTICATED_FUNCTIONAL_UAT=REQUIRED'
require "$WORKFLOW" 'authenticatedUatRequired":true'
require "$WORKFLOW" 'functionalUatStatus":"pending-user-session-validation"'
require "$WORKFLOW" 'Roll back web image on deployment-integrity failure'
require "$WORKFLOW" 'fruat-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'fruatrb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'functional-runtime-uat-candidate-test-${{ github.run_id }}-${{ github.run_attempt }}'
require "$WORKFLOW" 'A green run verifies deployment integrity and candidate assets only.'
require "$WORKFLOW" 'It **does not** authenticate as a ProjectPulse user'

reject "$WORKFLOW" 'push:'
reject "$WORKFLOW" 'schedule:'
reject "$WORKFLOW" 'environment:[[:space:]]*production'
reject "$WORKFLOW" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$WORKFLOW" 'AZURE_API_APP'
reject "$WORKFLOW" 'containerapp[[:space:]]+update[^\n]*AZURE_API_APP'
reject "$WORKFLOW" 'database/migrations'
reject "$WORKFLOW" 'psql[[:space:]]'
reject "$WORKFLOW" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$WORKFLOW" 'automatic_sync_enabled[[:space:]]*=[[:space:]]*TRUE'
reject "$WORKFLOW" '\[\[[^\n]*\$\{\{[[:space:]]*inputs\.'
reject "$WORKFLOW" 'TARGET_COMMIT[^\n]*\$\{\{[[:space:]]*inputs\.'
reject "$WORKFLOW" "grep -Fq 'projectpulse-route-certify-integration' /tmp/functional-runtime-uat-validation/app.js"
reject "$WORKFLOW" 'AUTHENTICATED_FUNCTIONAL_UAT=PASS'
reject "$WORKFLOW" 'FUNCTIONAL_RUNTIME_UAT=READY'
reject "$WORKFLOW" 'functionalUatStatus":"passed"'

INPUT_REFERENCE_COUNT="$(grep -Fc '${{ inputs.' "$WORKFLOW")"
[[ "$INPUT_REFERENCE_COUNT" == "3" ]] || fail "Expected exactly three non-shell input references (checkout ref and two environment mappings); found $INPUT_REFERENCE_COUNT."

echo 'FUNCTIONAL_RUNTIME_UAT_CANDIDATE_TEST_DEPLOYMENT_GUARD=PASS'
