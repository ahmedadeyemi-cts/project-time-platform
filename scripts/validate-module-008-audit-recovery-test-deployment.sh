#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY="$ROOT/.github/workflows/projectpulse-deploy-module-008-audit-recovery-test.yml"
VALIDATE="$ROOT/.github/workflows/validate-module-008-audit-recovery-test-deployment.yml"
EXPECTED_RELEASE="9ba6d676189ebca39f408c3bc7a71aa652cea8bf"

fail() { echo "ERROR: $*" >&2; exit 1; }
require_file() { [[ -f "$1" ]] || fail "Missing required file: ${1#$ROOT/}"; }
require() { grep -Fq -- "$2" "$1" || fail "Missing contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }

require_file "$DEPLOY"
require_file "$VALIDATE"

require "$DEPLOY" 'name: ProjectPulse Deploy Module 008 Audit Recovery Test'
require "$DEPLOY" 'workflow_dispatch:'
require "$DEPLOY" "default: $EXPECTED_RELEASE"
require "$DEPLOY" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$DEPLOY" 'DEPLOY-MODULE-008-AUDIT-RECOVERY-TO-TEST'
require "$DEPLOY" 'environment: test'
require "$DEPLOY" 'refs/heads/main'
require "$DEPLOY" 'DISPATCH_RELEASE_COMMIT: ${{ inputs.release_commit }}'
require "$DEPLOY" 'DISPATCH_CONFIRMATION: ${{ inputs.confirmation }}'
require "$DEPLOY" 'WORKFLOW_SOURCE_REF: ${{ github.ref }}'
require "$DEPLOY" 'WORKFLOW_SOURCE_SHA: ${{ github.sha }}'
require "$DEPLOY" 'git -C control merge-base --is-ancestor'
require "$DEPLOY" 'Only the verified Module 008 audit-route recovery release may deploy.'

require "$DEPLOY" 'src/frontend/project-time-web/src/AuditHistoryPanel.jsx'
require "$DEPLOY" 'validate-modules-008-009-admin-experience.mjs'
require "$DEPLOY" "readStoredJson('projectPulseViewAsUser')"
require "$DEPLOY" 'if (!readProjectPulseAuthSession() || readModule008ViewAsUser())'
require "$DEPLOY" "data-module-008-route-recovery-host"
require "$DEPLOY" 'root.render(<AuditHistoryPanel recoveryMode />)'
require "$DEPLOY" '! grep -Fq '\''window.setTimeout(synchronize, 50)'\'''
require "$DEPLOY" '! grep -Fq '\''retryTimer'\'''

require "$DEPLOY" 'Capture current immutable web image'
require "$DEPLOY" 'Build exact Module 008 web candidate'
require "$DEPLOY" 'Deploy Module 008 web candidate only'
require "$DEPLOY" 'Wait for exact Module 008 web revision'
require "$DEPLOY" 'Validate served Module 008 recovery assets'
require "$DEPLOY" '/api/admin/audit-history/events'
require "$DEPLOY" 'projectPulseViewAsUser'
require "$DEPLOY" 'projectPulseAuthSession'
require "$DEPLOY" 'X-ProjectPulse-Session'
require "$DEPLOY" 'Manager team scope'
require "$DEPLOY" 'MODULE_008_RECOVERY_WEB_VALIDATION=PASS'
require "$DEPLOY" '"apiDeployment": "unchanged"'
require "$DEPLOY" '"migrations": "unchanged"'
require "$DEPLOY" '"module009": "unchanged"'
require "$DEPLOY" 'Roll back web image on failure'
require "$DEPLOY" 'wait-containerapp-ready-revision.sh'
require "$DEPLOY" 'aud008rb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'

reject "$DEPLOY" '^[[:space:]]*push:'
reject "$DEPLOY" '^[[:space:]]*schedule:'
reject "$DEPLOY" 'environment:[[:space:]]*production'
reject "$DEPLOY" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$DEPLOY" 'AZURE_API_APP'
reject "$DEPLOY" 'Deploy API|api_image|apiImage'
reject "$DEPLOY" 'psql|PROJECTPULSE_TEST_DATABASE_URL|PTP_DB_'
reject "$DEPLOY" 'database/migrations|database/rollback|migration 048 image|Apply or verify migration'
reject "$DEPLOY" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$DEPLOY" 'functionalUatStatus":"passed"'

require "$VALIDATE" 'name: Validate Module 008 Audit Recovery Test Deployment'
require "$VALIDATE" 'push:'
require "$VALIDATE" 'pull_request:'
require "$VALIDATE" 'Enforce exact deployment-control scope'
require "$VALIDATE" 'scripts/validate-module-008-audit-recovery-test-deployment.sh'
require "$VALIDATE" 'npm run validate:modules008009'
require "$VALIDATE" 'npm run build'
require "$VALIDATE" 'module-008-audit-recovery/deployment-controls'
require "$VALIDATE" '.github/workflows/projectpulse-deploy-module-008-audit-recovery-test.yml'
require "$VALIDATE" '.github/workflows/validate-module-008-audit-recovery-test-deployment.yml'
require "$VALIDATE" 'scripts/validate-module-008-audit-recovery-test-deployment.sh'

reject "$VALIDATE" 'az[[:space:]]+containerapp[[:space:]]+update'
reject "$VALIDATE" 'azure/login'
reject "$VALIDATE" 'environment:[[:space:]]*production'

echo 'MODULE_008_AUDIT_RECOVERY_TEST_DEPLOYMENT_GUARD=PASS'
