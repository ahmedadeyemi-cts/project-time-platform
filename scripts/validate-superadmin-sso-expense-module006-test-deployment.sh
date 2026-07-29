#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEPLOY="$ROOT/.github/workflows/projectpulse-deploy-superadmin-sso-expense-module006-test.yml"
VALIDATE="$ROOT/.github/workflows/validate-superadmin-sso-expense-module006-test-deployment.yml"
RUNNER="$ROOT/scripts/run-superadmin-sso-expense-module006-test-deployment.sh"
SELF="$ROOT/scripts/validate-superadmin-sso-expense-module006-test-deployment.sh"
EXPECTED_RELEASE="185a0030dbc96813c8cd46498668ca289805a4d7"

fail() { echo "ERROR: $*" >&2; exit 1; }
require_file() { [[ -f "$1" ]] || fail "Missing required file: ${1#$ROOT/}"; }
require() { grep -Fq -- "$2" "$1" || fail "Missing contract in ${1#$ROOT/}: $2"; }
reject() { ! grep -Eiq -- "$2" "$1" || fail "Forbidden pattern in ${1#$ROOT/}: $2"; }
line_of() { grep -Fn -- "$2" "$1" | head -1 | cut -d: -f1; }

for file in "$DEPLOY" "$VALIDATE" "$RUNNER" "$SELF"; do
  require_file "$file"
done

require "$DEPLOY" 'name: ProjectPulse Deploy Super Admin, SSO, Expense Billing, and Module 006 Test'
require "$DEPLOY" 'workflow_dispatch:'
require "$DEPLOY" "default: $EXPECTED_RELEASE"
require "$DEPLOY" "EXPECTED_RELEASE_COMMIT: $EXPECTED_RELEASE"
require "$DEPLOY" 'DEPLOY-SUPERADMIN-SSO-EXPENSE-MODULE006-TO-TEST'
require "$DEPLOY" 'environment: test'
require "$DEPLOY" 'group: projectpulse-deploy-superadmin-sso-expense-module006-test'
require "$DEPLOY" 'cancel-in-progress: false'
require "$DEPLOY" "[[ \"\$WORKFLOW_SOURCE_REF\" == 'refs/heads/main' ]]"
require "$DEPLOY" 'release_commit must be a complete 40-character SHA.'
require "$DEPLOY" 'Only the verified Super Administrator, SSO, expense billing, Module 006, and Module 003 release may deploy.'
require "$DEPLOY" 'git -C control merge-base --is-ancestor "$TARGET_COMMIT" HEAD'
require "$DEPLOY" 'ProjectPulseActualSessionAuthority.cs'
require "$DEPLOY" 'ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync'
require "$DEPLOY" 'actual_session_super_administrator'
require "$DEPLOY" 'canManage = authority.Allowed'
require "$DEPLOY" 'viewAsTransfersMutationAuthority = false'
require "$DEPLOY" 'app.UseMicrosoftPublicSsoOriginCompatibility();'
require "$DEPLOY" 'private const string InteractiveSsoPrefix = "/api/auth/sso/"'
require "$DEPLOY" 'ForwardedValues(request.Headers["X-Forwarded-Host"].ToString())'
require "$DEPLOY" "! grep -Fq '.azurecontainerapps.io'"
require "$DEPLOY" 'ExpectedRedirect(context, environmentMode, profile)'
require "$DEPLOY" 'trusted_public_origin_unavailable'
require "$DEPLOY" 'stored_environment_profile'
require "$DEPLOY" '/api/project-expenses/projects/{projectId:guid}/billing-context'
require "$DEPLOY" '/api/project-expenses/projects/{projectId:guid}/billing-acknowledgement'
require "$DEPLOY" 'expense-only-pass-through'
require "$DEPLOY" 'expense-included-fixed-price'
require "$DEPLOY" "SET review_status = 'blocked'"
require "$DEPLOY" 'UseProjectExpenseBillingReadinessContinuitySafe'
require "$DEPLOY" 'app.MapModule005ProjectExpenseBillingAcknowledgementEndpoints();'
require "$DEPLOY" 'data-project-expense-cross-module="non-invasive-v2"'
require "$DEPLOY" 'Choose a project only when expense context is needed'
require "$DEPLOY" "! grep -Fq \"result.projects?.[0]?.projectId\""
require "$DEPLOY" '.expense-cross-module-shell.is-open'
require "$DEPLOY" "displayName: 'Toyota & Hyundai Pipeline'"
require "$DEPLOY" "group: 'Sales & Opportunities'"
require "$DEPLOY" 'MODULE_006_TOYOTA_HYUNDAI_PIPELINE_GENERATION=PASS'
require "$DEPLOY" "'006': 'Toyota & Hyundai Pipeline'"
require "$DEPLOY" '<table className="engineering-utilization-table">'
require "$DEPLOY" 'ProjectTime.Api.RouteResultExecutionTests.csproj'
require "$DEPLOY" 'ProjectTime.Api.AuthorizationTests.csproj'
require "$DEPLOY" 'test-projectpulse-api-startup.sh'
require "$DEPLOY" 'npm run validate:superadmin-sso-expense-module006'
require "$DEPLOY" 'npm run validate:module005'
require "$DEPLOY" 'npm run validate:module026'
require "$DEPLOY" 'npm run validate:modules003004-rolling-years'
require "$DEPLOY" 'npm run validate:microsoft-connection'
require "$DEPLOY" 'npm run validate:microsoft-sso-runtime'
require "$DEPLOY" 'npm run validate:dynamic-rbac'
require "$DEPLOY" 'validate-group-2a-provider-neutral-platform-operations.mjs'
require "$DEPLOY" 'cat dist/assets/*.js'
require "$DEPLOY" 'cat dist/assets/*.css'
require "$DEPLOY" 'azure/login@v2'
require "$DEPLOY" 'run-superadmin-sso-expense-module006-test-deployment.sh'
require "$DEPLOY" 'control/evidence/superadmin-sso-expense-module006-test-deployment.json'
reject "$DEPLOY" '^[[:space:]]*push:'
reject "$DEPLOY" '^[[:space:]]*schedule:'
reject "$DEPLOY" 'environment:[[:space:]]*production'
reject "$DEPLOY" 'PROJECTPULSE_ENVIRONMENT=production'
reject "$DEPLOY" 'PROJECTPULSE_TEST_DATABASE_URL'
reject "$DEPLOY" '\bpsql\b'
reject "$DEPLOY" 'database/migrations/[0-9]'
reject "$DEPLOY" 'az[[:space:]]+role[[:space:]]+assignment'

SOURCE_CHECK_LINE="$(line_of "$DEPLOY" 'SUPERADMIN_SSO_EXPENSE_MODULE006_SOURCE_VALIDATION=PASS')"
AZURE_LOGIN_LINE="$(line_of "$DEPLOY" 'azure/login@v2')"
[[ -n "$SOURCE_CHECK_LINE" && -n "$AZURE_LOGIN_LINE" && "$SOURCE_CHECK_LINE" -lt "$AZURE_LOGIN_LINE" ]] ||
  fail 'All source validation must complete before Azure login.'

require "$RUNNER" "EXPECTED_RELEASE_COMMIT=\"$EXPECTED_RELEASE\""
require "$RUNNER" 'CURRENT_API_IMAGE="$(resolve_digest "$RAW_API_IMAGE")"'
require "$RUNNER" 'CURRENT_WEB_IMAGE="$(resolve_digest "$RAW_WEB_IMAGE")"'
require "$RUNNER" '[[ "${API_MODE,,}" == single && "${WEB_MODE,,}" == single ]]'
require "$RUNNER" 'restore_web'
require "$RUNNER" 'restore_api'
require "$RUNNER" 'Web rollback skipped because another image is active'
require "$RUNNER" 'API rollback skipped because another image is active'
require "$RUNNER" 'superadmin-sso-expense-module006-$TARGET_COMMIT'
require "$RUNNER" "probe_api module026_providers GET '/api/integrations/026/providers' '401,403'"
require "$RUNNER" "probe_api expense_context GET '/api/project-expenses/context' '401,403'"
require "$RUNNER" "probe_api expense_billing_context GET '/api/project-expenses/projects/00000000-0000-0000-0000-000000000000/billing-context' '401,403'"
require "$RUNNER" "probe_api expense_ack POST '/api/project-expenses/projects/00000000-0000-0000-0000-000000000000/billing-acknowledgement' '401,403'"
require "$RUNNER" "probe_api billing_candidates GET '/api/billing/candidates' '401,403'"
require "$RUNNER" "probe_api rbac_bootstrap GET '/api/rbac/v1/bootstrap' '401,403'"
require "$RUNNER" "probe_api platform_overview GET '/api/platform-operations/overview' '401,403'"
require "$RUNNER" 'probe_sso_redirect api'
require "$RUNNER" 'probe_sso_redirect web'
require "$RUNNER" 'login.microsoftonline.com'
require "$RUNNER" 'redirect_uri'
require "$RUNNER" 'EXPECTED_CALLBACK="$BASE_URL/api/auth/sso/callback"'
require "$RUNNER" "grep -Eiq 'azurecontainerapps\\.io|\\.internal\\.'"
require "$RUNNER" 'fetch_served_asset_graph'
require "$RUNNER" 'SERVED_JS_ASSET_COUNT'
require "$RUNNER" 'SERVED_CSS_ASSET_COUNT'
require "$RUNNER" 'MODULE026_EDIT|Edit connection'
require "$RUNNER" 'EXPENSE_CONTEXT|/billing-context'
require "$RUNNER" 'EXPENSE_ACK|/billing-acknowledgement'
require "$RUNNER" 'EXPENSE_NON_INVASIVE|non-invasive-v2'
require "$RUNNER" 'MODULE006_NAME|Toyota & Hyundai Pipeline'
require "$RUNNER" 'MODULE003_TABLE|engineering-utilization-manager-table'
require "$RUNNER" 'EXPENSE_OPEN_STYLE|.expense-cross-module-shell.is-open'
require "$RUNNER" 'MODULE003_TABLE_STYLE|.engineering-utilization-table-wrap'
require "$RUNNER" 'SUPERADMIN_SSO_EXPENSE_MODULE006_API_PROTECTED_BOUNDARY=PASS'
require "$RUNNER" 'SUPERADMIN_SSO_EXPENSE_MODULE006_API_VALIDATION=PASS'
require "$RUNNER" 'SUPERADMIN_SSO_EXPENSE_MODULE006_WEB_VALIDATION=PASS'
require "$RUNNER" 'superadmin-sso-expense-module006-test-deployment.json'
require "$RUNNER" '"databaseMigration": false'
require "$RUNNER" '"superAdministratorAuthority": "actual-session-permanent-full-control"'
require "$RUNNER" '"viewAsMutationAuthority": false'
require "$RUNNER" '"expenseDrawer": "collapsed-non-invasive-explicit-project-selection"'
require "$RUNNER" '"expenseAcknowledgementPerformedByDeployment": false'
require "$RUNNER" '"interactiveSsoCompletedByDeployment": false'
require "$RUNNER" '"graphCallPerformedByDeployment": false'
require "$RUNNER" '"emailSentByDeployment": false'
require "$RUNNER" '"imageRollbackOnFailure": "candidate-only"'
require "$RUNNER" 'SUPERADMIN_SSO_EXPENSE_MODULE006_TEST_DEPLOYMENT=COMPLETE'
reject "$RUNNER" 'PROJECTPULSE_TEST_DATABASE_URL'
reject "$RUNNER" '\bpsql\b'
reject "$RUNNER" 'database/migrations/[0-9]'
reject "$RUNNER" 'az[[:space:]]+role[[:space:]]+assignment'
reject "$RUNNER" 'environment:[[:space:]]*production'
reject "$RUNNER" 'graph\.microsoft\.com|/sendMail|smtp\.office365\.com|api\.getbase\.com'

require "$VALIDATE" 'name: Validate Super Admin, SSO, Expense Billing, and Module 006 Test Deployment'
require "$VALIDATE" 'release/superadmin-sso-expense-module006-test-*'
require "$VALIDATE" 'Enforce exact four-file deployment-control scope'
require "$VALIDATE" 'scripts/validate-superadmin-sso-expense-module006-test-deployment.sh'
require "$VALIDATE" 'bash -n scripts/run-superadmin-sso-expense-module006-test-deployment.sh'
require "$VALIDATE" 'ProjectTime.Api.RouteResultExecutionTests.csproj'
require "$VALIDATE" 'ProjectTime.Api.AuthorizationTests.csproj'
require "$VALIDATE" 'test-projectpulse-api-startup.sh'
require "$VALIDATE" 'validate-superadmin-sso-expense-module006-package.mjs'
require "$VALIDATE" 'validate-module-005-project-expense-upload.mjs'
require "$VALIDATE" 'validate-module-026-crm-erp-integrations.mjs'
require "$VALIDATE" 'validate-modules-003-004-rolling-years.mjs'
require "$VALIDATE" 'validate-microsoft-integration-authoritative-connection.mjs'
require "$VALIDATE" 'validate-microsoft-sso-runtime-wiring.mjs'
require "$VALIDATE" 'validate-dynamic-rbac-administration.mjs'
require "$VALIDATE" 'validate-group-2a-provider-neutral-platform-operations.mjs'
require "$VALIDATE" 'Build complete frontend production bundle'
require "$VALIDATE" 'Validate complete split frontend asset set'
reject "$VALIDATE" 'azure/login'
reject "$VALIDATE" 'az[[:space:]]+containerapp[[:space:]]+(update|job)'
reject "$VALIDATE" 'environment:[[:space:]]*production'
reject "$VALIDATE" 'PROJECTPULSE_TEST_DATABASE_URL'
reject "$VALIDATE" '\bpsql\b'

bash -n "$RUNNER"
bash -n "$SELF"
echo 'SUPERADMIN_SSO_EXPENSE_MODULE006_TEST_DEPLOYMENT_GUARD=PASS'
