#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/projectpulse-deploy-microsoft-preview-mail-runtime-test.yml"
EXPECTED_RELEASE="c4a97ac1ee3309be8d216380f2f18810a4362f21"

fail() { echo "ERROR: $*" >&2; exit 1; }
[[ -f "$WORKFLOW" ]] || fail "Missing deployment workflow: ${WORKFLOW#$ROOT/}"
require() { grep -Fq -- "$2" "$1" || fail "Missing required contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }

require "$WORKFLOW" 'name: ProjectPulse Deploy Microsoft Preview Mail Runtime Test'
require "$WORKFLOW" 'workflow_dispatch:'
require "$WORKFLOW" "default: $EXPECTED_RELEASE"
require "$WORKFLOW" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$WORKFLOW" 'DEPLOY-MICROSOFT-PREVIEW-MAIL-RUNTIME-TO-TEST'
require "$WORKFLOW" 'environment: test'
require "$WORKFLOW" 'refs/heads/main'
require "$WORKFLOW" 'DISPATCH_RELEASE_COMMIT: ${{ inputs.release_commit }}'
require "$WORKFLOW" 'DISPATCH_CONFIRMATION: ${{ inputs.confirmation }}'
require "$WORKFLOW" 'WORKFLOW_SOURCE_REF: ${{ github.ref }}'
require "$WORKFLOW" 'WORKFLOW_SOURCE_SHA: ${{ github.sha }}'
require "$WORKFLOW" 'Only the verified Microsoft Preview and mail-runtime release may deploy.'
require "$WORKFLOW" 'git -C control merge-base --is-ancestor'
require "$WORKFLOW" 'test -f control/scripts/wait-containerapp-ready-revision.sh'
require "$WORKFLOW" 'bash control/scripts/wait-containerapp-ready-revision.sh'

require "$WORKFLOW" 'MicrosoftMailRuntimeConfigurationModule.cs'
require "$WORKFLOW" 'MicrosoftSmtpCredentialProjectionCompatibility.cs'
require "$WORKFLOW" 'UseMicrosoftSmtpCredentialProjectionCompatibility'
require "$WORKFLOW" 'OriginalActiveEnvironment'
require "$WORKFLOW" 'OriginalLegacyUsername'
require "$WORKFLOW" 'OriginalLegacyPassword'
require "$WORKFLOW" 'ReadValidatedSelectionFromResponseAsync'
require "$WORKFLOW" 'JsonDocument.ParseAsync'
require "$WORKFLOW" 'ClearLegacyCredential'
require "$WORKFLOW" 'microsoft-mail-runtime-activation.js'
require "$WORKFLOW" 'function restoreModule010Preview'
require "$WORKFLOW" 'data-module-010-preview-preserved'
require "$WORKFLOW" 'configuration.RecipientBoundary == "production_governed"'
require "$WORKFLOW" 'PROJECTPULSE_MAIL_RECIPIENT_BOUNDARY'
require "$WORKFLOW" 'outbox_only'
require "$WORKFLOW" 'PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET'
require "$WORKFLOW" 'PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET'
require "$WORKFLOW" 'PROJECTPULSE_TEST_SMTP_'
require "$WORKFLOW" 'PROJECTPULSE_PRODUCTION_SMTP_'
require "$WORKFLOW" 'projectpulse:microsoft-mail-runtime-status'
require "$WORKFLOW" "import './microsoft-mail-runtime-activation.js';"
require "$WORKFLOW" "! grep -Fq '.route-azure-admin .azure-admin-heading-actions .primary-action'"
require "$WORKFLOW" "! grep -Fq 'Request.EnableBuffering' \"\$PROJECTION\""
require "$WORKFLOW" "! grep -Fq 'context.Request.Body' \"\$PROJECTION\""

require "$WORKFLOW" 'Capture current immutable API and web images'
require "$WORKFLOW" 'Build immutable API and web candidates'
require "$WORKFLOW" 'Deploy API candidate'
require "$WORKFLOW" 'Wait for exact API candidate revision'
require "$WORKFLOW" 'Validate protected Microsoft Preview mail API routes'
require "$WORKFLOW" '/api/microsoft-integration/mail-runtime'
require "$WORKFLOW" '/api/microsoft-integration/directory-users/import-selected'
require "$WORKFLOW" 'AUTHENTICATED_MICROSOFT_PREVIEW_MAIL_UAT=REQUIRED'
require "$WORKFLOW" 'Deploy web candidate'
require "$WORKFLOW" 'Wait for exact web candidate revision'
require "$WORKFLOW" 'Validate served Preview and mail-runtime assets'
require "$WORKFLOW" '.route-azure-admin .azure-preview-card'
require "$WORKFLOW" '.route-azure-admin .azure-preview-card .azure-admin-heading-actions button'
require "$WORKFLOW" 'ready_api_revision'
require "$WORKFLOW" 'ready_web_revision'
require "$WORKFLOW" 'ready_api_image'
require "$WORKFLOW" 'ready_web_image'
require "$WORKFLOW" 'AUTHENTICATED_PREVIEW_GRAPH_SMTP_UAT=REQUIRED'

require "$WORKFLOW" '"deploymentType":"api-web-no-migration"'
require "$WORKFLOW" '"databaseMutation":false'
require "$WORKFLOW" '"functionalUatStatus":"pending-user-session-validation"'
require "$WORKFLOW" 'microsoft-preview-mail-runtime-test-${{ github.run_id }}-${{ github.run_attempt }}'
require "$WORKFLOW" 'Roll back API and web images on failure'
require "$WORKFLOW" "if [[ '\${{ steps.deploy_api.outputs.started }}' == 'true' ]]; then"
require "$WORKFLOW" "if [[ '\${{ steps.deploy_web.outputs.started }}' == 'true' ]]; then"
require "$WORKFLOW" 'mspmailapirb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
require "$WORKFLOW" 'mspmailwebrb-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'

reject "$WORKFLOW" 'push:'
reject "$WORKFLOW" 'schedule:'
reject "$WORKFLOW" 'environment:[[:space:]]*production'
reject "$WORKFLOW" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$WORKFLOW" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$WORKFLOW" 'automatic_sync_enabled[[:space:]]*=[[:space:]]*TRUE'
reject "$WORKFLOW" '\[\[[^\n]*\$\{\{[[:space:]]*inputs\.'
reject "$WORKFLOW" 'TARGET_COMMIT[^\n]*\$\{\{[[:space:]]*inputs\.'
reject "$WORKFLOW" 'psql|PROJECTPULSE_TEST_DATABASE_URL|PTP_DB_PASSWORD'
reject "$WORKFLOW" 'database/migrations|database/rollback|schema_migrations'
reject "$WORKFLOW" 'AUTHENTICATED_PREVIEW_GRAPH_SMTP_UAT=PASS'
reject "$WORKFLOW" 'functionalUatStatus":"passed"'
reject "$WORKFLOW" 'databaseMutation":true'
reject "$WORKFLOW" 'ACTIVE_API=.*properties[.]template[.]containers\[0\][.]image'
reject "$WORKFLOW" 'ACTIVE_WEB=.*properties[.]template[.]containers\[0\][.]image'
reject "$WORKFLOW" 'test[[:space:]]+-x[[:space:]]+control/scripts/wait-containerapp-ready-revision[.]sh'
reject "$WORKFLOW" '50e415d2053610a10d3ba6ca3662d6414b55ec25'

INPUT_REFERENCE_COUNT="$(grep -Fc '${{ inputs.' "$WORKFLOW")"
[[ "$INPUT_REFERENCE_COUNT" == "3" ]] || fail "Expected exactly three non-shell input references; found $INPUT_REFERENCE_COUNT."

echo 'MICROSOFT_PREVIEW_MAIL_RUNTIME_TEST_DEPLOYMENT_GUARD=PASS'
