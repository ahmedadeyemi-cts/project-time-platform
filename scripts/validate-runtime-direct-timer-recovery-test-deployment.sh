#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-runtime-direct-timer-recovery-test.yml"
EXPECTED_RELEASE="23e97e68f741bcfb27df4baf4b95397eda54fcb2"

fail() { echo "ERROR: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" ]] || fail "Missing deployment workflow: ${WORKFLOW#$ROOT/}"
require() { grep -Fq -- "$2" "$1" || fail "Missing required contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }

require "$WORKFLOW" 'name: ProjectPulse Deploy Runtime Direct Timer Recovery Test'
require "$WORKFLOW" 'workflow_dispatch:'
require "$WORKFLOW" "default: $EXPECTED_RELEASE"
require "$WORKFLOW" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$WORKFLOW" 'DEPLOY-RUNTIME-DIRECT-TIMER-RECOVERY-TO-TEST'
require "$WORKFLOW" 'environment: test'
require "$WORKFLOW" 'refs/heads/main'
require "$WORKFLOW" 'Check out exact verified release'
require "$WORKFLOW" 'Only the verified runtime-direct release may deploy.'
require "$WORKFLOW" 'git -C control merge-base --is-ancestor'

require "$WORKFLOW" 'projectpulse-critical-runtime-direct-2026-07-26'
require "$WORKFLOW" 'window.__projectPulseOriginalFetch'
require "$WORKFLOW" 'x-projectpulse-authoritative-path'
require "$WORKFLOW" 'direct authoritative response'
require "$WORKFLOW" "'/api/runtime/v2/timesheet/steward/users': '/api/timesheet/ptc/users'"
require "$WORKFLOW" 'allActiveUsersAllowed: true'

require "$WORKFLOW" 'Module001ActiveTimerRecoveryPortal.jsx'
require "$WORKFLOW" '/api/timesheet/timers/active'
require "$WORKFLOW" 'window.setInterval(load, 5000)'
require "$WORKFLOW" 'window.setInterval(() => setClock(new Date()), 1000)'
require "$WORKFLOW" 'Running timer recovered'
require "$WORKFLOW" "description: timer.description || ''"
require "$WORKFLOW" 'No work description is recorded.'
require "$WORKFLOW" 'Stop timer'
require "$WORKFLOW" 'Discard'
require "$WORKFLOW" "! grep -Fq 'Open timer view'"
require "$WORKFLOW" "! grep -Fq 'Stopped from the active timer recovery surface.'"

require "$WORKFLOW" 'main.app-shell.route-certify-integration .certify-integration-center'
require "$WORKFLOW" 'max-height:calc(100dvh - 15rem)'
require "$WORKFLOW" 'overflow-y:auto'
require "$WORKFLOW" 'overscroll-behavior:contain'

require "$WORKFLOW" 'Capture current immutable web image'
require "$WORKFLOW" 'Build immutable runtime-direct web image'
require "$WORKFLOW" 'Deploy runtime-direct web release'
require "$WORKFLOW" 'Validate served runtime, timer recovery, and Module 038 assets'
require "$WORKFLOW" 'RUNTIME_DIRECT_TIMER_RECOVERY_LIVE_WEB=READY'
require "$WORKFLOW" 'deploymentType":"web-only"'
require "$WORKFLOW" 'Roll back web image on failure'
require "$WORKFLOW" 'rtdir-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'rtdirrb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'runtime-direct-timer-recovery-test-${{ github.run_id }}-${{ github.run_attempt }}'

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

echo 'RUNTIME_DIRECT_TIMER_RECOVERY_TEST_DEPLOYMENT_GUARD=PASS'
