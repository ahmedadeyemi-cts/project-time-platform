#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY="$ROOT/.github/workflows/projectpulse-deploy-open-pr-reconciliation-test.yml"
VALIDATE="$ROOT/.github/workflows/validate-open-pr-reconciliation-test-deployment.yml"
APPLY="$ROOT/scripts/apply-open-pr-reconciliation-test-migration-049.sh"
JOB="$ROOT/scripts/run-open-pr-reconciliation-test-migration-job.sh"
RUN="$ROOT/scripts/run-open-pr-reconciliation-test-deployment.sh"
EXPECTED_RELEASE="b24ea3db7d0c839d03804975e87b7929dba8c7f6"

fail() { echo "ERROR: $*" >&2; exit 1; }
require_file() { [[ -f "$1" ]] || fail "Missing required file: ${1#$ROOT/}"; }
require() { grep -Fq -- "$2" "$1" || fail "Missing contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }

for file in "$DEPLOY" "$VALIDATE" "$APPLY" "$JOB" "$RUN"; do
  require_file "$file"
done

require "$DEPLOY" 'name: ProjectPulse Deploy Open PR Reconciliation Test'
require "$DEPLOY" 'workflow_dispatch:'
require "$DEPLOY" "default: $EXPECTED_RELEASE"
require "$DEPLOY" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$DEPLOY" 'DEPLOY-OPEN-PR-RECONCILIATION-TO-TEST'
require "$DEPLOY" 'refs/heads/main'
require "$DEPLOY" 'environment: test'
require "$DEPLOY" 'group: projectpulse-deploy-open-pr-reconciliation-test'
require "$DEPLOY" 'cancel-in-progress: false'
require "$DEPLOY" 'Only the verified open-PR reconciliation source release may deploy.'
require "$DEPLOY" 'git -C control merge-base --is-ancestor'
require "$DEPLOY" 'MapCustomerDirectorySellSyncEndpoints'
require "$DEPLOY" 'X-ProjectPulse-View-As-User'
require "$DEPLOY" 'Search module number or page name'
require "$DEPLOY" "'work-task-builder': 'work-register'"
require "$DEPLOY" 'Synchronization history is consolidated in Module 008'
require "$DEPLOY" '049_module_021_sell_customer_sync'
require "$DEPLOY" 'validate-admin-runtime-stability.mjs'
require "$DEPLOY" 'validate-group-1-navigation-work-consolidation.mjs'
require "$DEPLOY" 'validate-modules-021-026-sell-customer-sync.mjs'
require "$DEPLOY" 'npm run validate:microsoft-connection'
require "$DEPLOY" 'npm run validate:microsoft-sso-runtime'
require "$DEPLOY" 'npm run build'
require "$DEPLOY" 'azure/login@v2'
require "$DEPLOY" 'export-pr55-test-database-url.sh'
require "$DEPLOY" 'run-open-pr-reconciliation-test-deployment.sh'
require "$DEPLOY" 'control/evidence/open-pr-reconciliation-test-deployment.json'
reject "$DEPLOY" '^[[:space:]]*push:'
reject "$DEPLOY" '^[[:space:]]*schedule:'
reject "$DEPLOY" 'environment:[[:space:]]*production'
reject "$DEPLOY" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$DEPLOY" 'az[[:space:]]+role[[:space:]]+assignment'

require "$APPLY" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$APPLY" 'MIGRATION_FILE="049_module_021_sell_customer_sync.sql"'
require "$APPLY" 'Migration checksum manifest must contain exactly one SQL file.'
require "$APPLY" 'sha256sum --check --strict SHA256SUMS'
require "$APPLY" 'schema_migrations app_users clients client_contacts projects crm_integration_providers'
require "$APPLY" 'Migration 049 changed app_users row count.'
require "$APPLY" 'Migration 049 changed clients row count.'
require "$APPLY" 'Migration 049 changed client_contacts row count.'
require "$APPLY" 'Migration 049 changed projects row count.'
require "$APPLY" 'Migration 049 changed CRM provider row count.'
require "$APPLY" 'ux_customer_directory_source_links_client'
require "$APPLY" 'ix_customer_directory_source_links_sync'
require "$APPLY" 'ix_customer_directory_sync_runs_provider'
require "$APPLY" "ARRAY['clientSecret','apiKey','accessToken','refreshToken']"
require "$APPLY" 'MODULE_021_026_SELL_SYNC_MIGRATION_049=APPLIED_OR_VERIFIED'
reject "$APPLY" 'database/rollback'
reject "$APPLY" 'DROP[[:space:]]+TABLE|TRUNCATE[[:space:]]+TABLE'
reject "$APPLY" 'curl|wget|api\.getbase\.com'

require "$JOB" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$JOB" 'OPEN_PR_RECONCILIATION_MIGRATION_IMAGE'
require "$JOB" 'OPEN_PR_RECONCILIATION_MIGRATION_JOB_NAME'
require "$JOB" 'The migration image must be an immutable digest from the approved ACR.'
require "$JOB" 'az acr login'
require "$JOB" 'EPHEMERAL_AZURE_TOKEN'
require "$JOB" 'az containerapp job create'
require "$JOB" 'az containerapp job start'
require "$JOB" 'az containerapp job delete'
require "$JOB" 'OPEN_PR_RECONCILIATION_MIGRATION_JOB_CLEANUP=COMPLETE'
require "$JOB" 'projectpulse-migration=049'
reject "$JOB" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$JOB" 'environment:[[:space:]]*production'

require "$RUN" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$RUN" 'CURRENT_API_IMAGE="$(resolve_digest "$RAW_API_IMAGE")"'
require "$RUN" 'CURRENT_WEB_IMAGE="$(resolve_digest "$RAW_WEB_IMAGE")"'
require "$RUN" '[[ -n "$CURRENT_API_IMAGE" && -n "$CURRENT_WEB_IMAGE" ]]'
require "$RUN" 'project-health-dashboard-open-pr-reconciliation-migrator'
require "$RUN" 'run-open-pr-reconciliation-test-migration-job.sh'
require "$RUN" 'OPEN_PR_RECONCILIATION_MIGRATION_049=APPLIED_OR_VERIFIED'
require "$RUN" 'API_SUFFIX="oprrapi-${RUN_ID}-${RUN_ATTEMPT}"'
require "$RUN" "probe_api sell-status GET '/api/customers/sell/status' '401'"
require "$RUN" "probe_api sell-preview POST '/api/customers/sell/preview' '401' '{}'"
require "$RUN" "probe_api sell-import POST '/api/customers/sell/import' '401' '{}'"
require "$RUN" "probe_api audit-history GET '/api/admin/audit-history/events' '401'"
require "$RUN" "probe_api module065-mail-test POST '/api/microsoft-integration/mail-runtime/test' '401,403' '{}'"
require "$RUN" 'Search module number or page name'
require "$RUN" 'X-ProjectPulse-View-As-User'
require "$RUN" 'Project creation and project/task management moved to Modules 055D and 055C.'
require "$RUN" 'OPEN_PR_RECONCILIATION_API_VALIDATION=PASS'
require "$RUN" 'OPEN_PR_RECONCILIATION_WEB_VALIDATION=PASS'
require "$RUN" 'restore_web'
require "$RUN" 'restore_api'
require "$RUN" 'Web rollback skipped because another image is active'
require "$RUN" 'API rollback skipped because another image is active'
require "$RUN" '"migrationRollbackOnFailure": "not-automatic-additive-schema-remains"'
require "$RUN" '"sellExternalApiCalledByDeployment": false'
require "$RUN" '"providerCredentialsChanged": false'
require "$RUN" '"module011DataDeleted": false'
reject "$RUN" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$RUN" 'environment:[[:space:]]*production'
reject "$RUN" 'api\.getbase\.com'

require "$VALIDATE" 'name: Validate Open PR Reconciliation Test Deployment'
require "$VALIDATE" 'Enforce exact deployment-control scope'
require "$VALIDATE" 'scripts/validate-open-pr-reconciliation-test-deployment.sh'
require "$VALIDATE" 'bash -n scripts/apply-open-pr-reconciliation-test-migration-049.sh'
require "$VALIDATE" 'bash -n scripts/run-open-pr-reconciliation-test-migration-job.sh'
require "$VALIDATE" 'bash -n scripts/run-open-pr-reconciliation-test-deployment.sh'
require "$VALIDATE" 'ProjectTime.Api.AuthorizationTests.csproj'
require "$VALIDATE" 'validate-admin-runtime-stability.mjs'
require "$VALIDATE" 'validate-group-1-navigation-work-consolidation.mjs'
require "$VALIDATE" 'validate-modules-021-026-sell-customer-sync.mjs'
require "$VALIDATE" 'npm run build'
require "$VALIDATE" 'open-pr-reconciliation/deployment-controls'
reject "$VALIDATE" 'azure/login'
reject "$VALIDATE" 'az[[:space:]]+containerapp[[:space:]]+(update|job)'
reject "$VALIDATE" 'environment:[[:space:]]*production'

echo 'OPEN_PR_RECONCILIATION_TEST_DEPLOYMENT_GUARD=PASS'
